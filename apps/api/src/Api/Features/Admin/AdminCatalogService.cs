using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Api.Features.Catalog;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Admin;

public interface IAdminCatalogService
{
    Task<IReadOnlyList<AdminTrimRow>> GetTrimsAsync(CancellationToken cancellationToken);
    Task<AdminTrimRow> CreateTrimAsync(AdminTrimDraftRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<AdminTrimRow> UpdateTrimAsync(Guid id, AdminTrimUpdateRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task DeleteTrimAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminSourceResponse>> GetSourcesAsync(CancellationToken cancellationToken);
    Task<AdminSourceResponse> CreateSourceAsync(AdminSourceRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<AdminSourceResponse> UpdateSourceAsync(Guid id, AdminSourceRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task DeactivateSourceAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
}

public sealed class AdminCatalogService(AppDbContext database, TimeProvider timeProvider, CatalogCache catalogCache) : IAdminCatalogService
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<AdminTrimRow>> GetTrimsAsync(CancellationToken cancellationToken) =>
        await (
                from trim in database.Trims.AsNoTracking()
                join modelYear in database.ModelYears.AsNoTracking() on trim.ModelYearId equals modelYear.Id
                join generation in database.Generations.AsNoTracking() on modelYear.GenerationId equals generation.Id
                join model in database.Models.AsNoTracking() on generation.ModelId equals model.Id
                join brand in database.Brands.AsNoTracking() on model.BrandId equals brand.Id
                orderby brand.Name, model.Name, modelYear.Year descending, trim.Name
                select new AdminTrimRow(
                    trim.Id,
                    brand.Name,
                    model.Name,
                    generation.Code,
                    modelYear.Year,
                    trim.Name,
                    trim.Slug,
                    trim.MarketStatus.ToString(),
                    model.BodyType.ToString(),
                    model.Segment.ToString(),
                    trim.UpdatedAt))
            .Take(1000)
            .ToArrayAsync(cancellationToken);

    public async Task<AdminTrimRow> CreateTrimAsync(
        AdminTrimDraftRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ValidateReason(request.Reason);
        ValidateSlug(request.BrandSlug, nameof(request.BrandSlug));
        ValidateSlug(request.ModelSlug, nameof(request.ModelSlug));
        ValidateSlug(request.TrimSlug, nameof(request.TrimSlug));
        if (request.ModelYear is < 1990 or > 2100 || request.GenerationStartYear is < 1950 or > 2100)
        {
            throw new AdminOperationException(400, "ADMIN_CATALOG_INVALID", "Model year or generation start year is outside the accepted range.");
        }
        if (!Enum.TryParse<BodyType>(request.BodyType, true, out var bodyType)
            || !Enum.TryParse<VehicleSegment>(request.Segment, true, out var segment)
            || !Enum.TryParse<MarketStatus>(request.MarketStatus, true, out var marketStatus))
        {
            throw new AdminOperationException(400, "ADMIN_CATALOG_INVALID", "Body type, segment or market status is not a canonical value.");
        }
        if (!string.IsNullOrWhiteSpace(request.BrandOfficialUrl)
            && (!Uri.TryCreate(request.BrandOfficialUrl, UriKind.Absolute, out var officialUri) || officialUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AdminOperationException(400, "ADMIN_CATALOG_INVALID", "Brand official URL must be an absolute HTTPS URL.");
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var brand = await database.Brands.SingleOrDefaultAsync(value => value.Slug == request.BrandSlug, cancellationToken);
        if (brand is null)
        {
            brand = new Brand
            {
                Name = request.BrandName.Trim(),
                Slug = request.BrandSlug,
                CountryCode = request.BrandCountryCode?.Trim().ToUpperInvariant(),
                OfficialUrl = request.BrandOfficialUrl?.Trim(),
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            database.Brands.Add(brand);
        }
        var model = await database.Models.SingleOrDefaultAsync(
            value => value.BrandId == brand.Id && value.Slug == request.ModelSlug,
            cancellationToken);
        if (model is null)
        {
            model = new VehicleModel
            {
                BrandId = brand.Id,
                Name = request.ModelName.Trim(),
                Slug = request.ModelSlug,
                BodyType = bodyType,
                Segment = segment,
                SearchText = $"{brand.Name} {request.ModelName}".ToLowerInvariant(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            database.Models.Add(model);
        }
        else if (model.BodyType != bodyType || model.Segment != segment)
        {
            throw new AdminOperationException(409, "ADMIN_MODEL_CONFLICT", "Existing model has a different canonical body type or segment.");
        }
        var generation = await database.Generations.SingleOrDefaultAsync(
            value => value.ModelId == model.Id && value.Code == request.GenerationCode,
            cancellationToken);
        if (generation is null)
        {
            generation = new Generation
            {
                ModelId = model.Id,
                Code = request.GenerationCode.Trim(),
                StartYear = request.GenerationStartYear,
                CreatedAt = now,
                UpdatedAt = now,
            };
            database.Generations.Add(generation);
        }
        var modelYear = await database.ModelYears.SingleOrDefaultAsync(
            value => value.GenerationId == generation.Id && value.Year == request.ModelYear && value.Market == "VN",
            cancellationToken);
        if (modelYear is null)
        {
            modelYear = new ModelYear
            {
                GenerationId = generation.Id,
                Year = request.ModelYear,
                Market = "VN",
                CreatedAt = now,
                UpdatedAt = now,
            };
            database.ModelYears.Add(modelYear);
        }
        var normalizedKey = $"{request.BrandSlug}:{request.ModelSlug}:{request.GenerationCode.Trim().ToLowerInvariant()}:{request.ModelYear}:{request.TrimSlug}";
        if (await database.Trims.AnyAsync(value => value.ModelYearId == modelYear.Id && value.NormalizedKey == normalizedKey, cancellationToken))
        {
            throw new AdminOperationException(409, "ADMIN_TRIM_DUPLICATE", "The normalized model-year/trim identity already exists.");
        }
        var trim = new Trim
        {
            ModelYearId = modelYear.Id,
            Name = request.TrimName.Trim(),
            Slug = request.TrimSlug,
            NormalizedKey = normalizedKey,
            MarketStatus = marketStatus,
            SearchText = $"{brand.Name} {model.Name} {request.TrimName}".ToLowerInvariant(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.Trims.Add(trim);
        database.AuditEvents.Add(Audit(actor, "CatalogTrimCreated", "Trim", trim.Id, null, new
        {
            Brand = brand.Name,
            Model = model.Name,
            Generation = generation.Code,
            request.ModelYear,
            Trim = trim.Name,
            MarketStatus = trim.MarketStatus.ToString(),
        }, request.Reason, context, now));
        CatalogSearchSync.Enqueue(
            database, "CatalogTrimCreated", "Trim", trim.Id, context.TraceIdentifier, now,
            new { trim.Name, trim.Slug, MarketStatus = trim.MarketStatus.ToString() });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return new AdminTrimRow(trim.Id, brand.Name, model.Name, generation.Code, modelYear.Year, trim.Name, trim.Slug, trim.MarketStatus.ToString(), model.BodyType.ToString(), model.Segment.ToString(), trim.UpdatedAt);
    }

    public async Task<AdminTrimRow> UpdateTrimAsync(
        Guid id,
        AdminTrimUpdateRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ValidateReason(request.Reason);
        ValidateSlug(request.Slug, nameof(request.Slug));
        if (!Enum.TryParse<MarketStatus>(request.MarketStatus, true, out var status))
        {
            throw new AdminOperationException(400, "ADMIN_CATALOG_INVALID", "Market status is not a canonical value.");
        }
        if (request.DiscontinuedAt is not null && request.LaunchedAt is not null && request.DiscontinuedAt < request.LaunchedAt)
        {
            throw new AdminOperationException(400, "ADMIN_CATALOG_INVALID", "Discontinued date cannot precede launch date.");
        }
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var row = await (
                from trim in database.Trims
                join modelYear in database.ModelYears on trim.ModelYearId equals modelYear.Id
                join generation in database.Generations on modelYear.GenerationId equals generation.Id
                join model in database.Models on generation.ModelId equals model.Id
                join brand in database.Brands on model.BrandId equals brand.Id
                where trim.Id == id
                select new { Trim = trim, ModelYear = modelYear, Generation = generation, Model = model, Brand = brand })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_TRIM_NOT_FOUND", "Trim was not found.");
        var before = new { row.Trim.Name, row.Trim.Slug, MarketStatus = row.Trim.MarketStatus.ToString(), row.Trim.LaunchedAt, row.Trim.DiscontinuedAt };
        row.Trim.Name = request.Name.Trim();
        row.Trim.Slug = request.Slug;
        row.Trim.MarketStatus = status;
        row.Trim.LaunchedAt = request.LaunchedAt;
        row.Trim.DiscontinuedAt = request.DiscontinuedAt;
        row.Trim.SearchText = $"{row.Brand.Name} {row.Model.Name} {row.Trim.Name}".ToLowerInvariant();
        row.Trim.UpdatedAt = timeProvider.GetUtcNow();
        database.AuditEvents.Add(Audit(actor, "CatalogTrimUpdated", "Trim", row.Trim.Id, before, new
        {
            row.Trim.Name,
            row.Trim.Slug,
            MarketStatus = row.Trim.MarketStatus.ToString(),
            row.Trim.LaunchedAt,
            row.Trim.DiscontinuedAt,
        }, request.Reason, context, row.Trim.UpdatedAt));
        CatalogSearchSync.Enqueue(
            database, "CatalogTrimUpdated", "Trim", row.Trim.Id, context.TraceIdentifier, row.Trim.UpdatedAt,
            new { row.Trim.Name, row.Trim.Slug, MarketStatus = row.Trim.MarketStatus.ToString() });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return new AdminTrimRow(row.Trim.Id, row.Brand.Name, row.Model.Name, row.Generation.Code, row.ModelYear.Year, row.Trim.Name, row.Trim.Slug, row.Trim.MarketStatus.ToString(), row.Model.BodyType.ToString(), row.Model.Segment.ToString(), row.Trim.UpdatedAt);
    }

    public async Task DeleteTrimAsync(
        Guid id,
        string reason,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ValidateReason(reason);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var trim = await database.Trims.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_TRIM_NOT_FOUND", "Trim was not found.");
        var hasDependents = await database.Prices.AnyAsync(value => value.TrimId == id, cancellationToken)
            || await database.TrimSpecs.AnyAsync(value => value.TrimId == id, cancellationToken)
            || await database.TrimFeatures.AnyAsync(value => value.TrimId == id, cancellationToken)
            || await database.DealerOffers.AnyAsync(value => value.TrimId == id, cancellationToken)
            || await database.PowertrainProfiles.AnyAsync(value => value.TrimId == id, cancellationToken)
            || await database.EnergyProfiles.AnyAsync(value => value.TrimId == id, cancellationToken);
        if (hasDependents)
        {
            throw new AdminOperationException(409, "ADMIN_TRIM_HAS_DEPENDENCIES", "Published/sourced trims cannot be deleted; change market status instead.");
        }
        var now = timeProvider.GetUtcNow();
        database.Trims.Remove(trim);
        database.AuditEvents.Add(Audit(actor, "CatalogTrimDeleted", "Trim", trim.Id, new { trim.Name, trim.Slug, MarketStatus = trim.MarketStatus.ToString() }, null, reason, context, now));
        CatalogSearchSync.Enqueue(
            database, "CatalogTrimDeleted", "Trim", trim.Id, context.TraceIdentifier, now,
            new { trim.Name, trim.Slug, MarketStatus = trim.MarketStatus.ToString() });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminSourceResponse>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var sources = await database.Sources.AsNoTracking().OrderBy(value => value.Priority).ThenBy(value => value.Name).ToArrayAsync(cancellationToken);
        var snapshots = await database.SourceSnapshots.AsNoTracking()
            .GroupBy(value => value.SourceId)
            .Select(group => new { SourceId = group.Key, Count = group.Count(), LastFetchedAt = group.Max(value => value.FetchedAt) })
            .ToArrayAsync(cancellationToken);
        return sources.Select(value =>
        {
            var evidence = snapshots.FirstOrDefault(snapshot => snapshot.SourceId == value.Id);
            var lastFetched = value.LastFetchedAt ?? evidence?.LastFetchedAt;
            return new AdminSourceResponse(
                value.Id, value.Name, value.Url, value.Domain, value.AuthorityLevel.ToString(), value.ContentType.ToString(),
                value.Active, value.Priority, (int)value.RefreshInterval.TotalHours, lastFetched,
                value.Active && (lastFetched is null || lastFetched + value.RefreshInterval < now),
                evidence?.Count ?? 0, value.RobotsNote, value.TermsNote);
        }).ToArray();
    }

    public async Task<AdminSourceResponse> CreateSourceAsync(
        AdminSourceRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ValidateSource(request, out var uri, out var authority, out var contentType);
        if (await database.Sources.AnyAsync(value => value.Url == uri.AbsoluteUri, cancellationToken))
        {
            throw new AdminOperationException(409, "ADMIN_SOURCE_DUPLICATE", "Source URL already exists.");
        }
        var now = timeProvider.GetUtcNow();
        var source = new Source
        {
            Name = request.Name.Trim(),
            Url = uri.AbsoluteUri,
            Domain = uri.IdnHost.ToLowerInvariant(),
            AuthorityLevel = authority,
            ContentType = contentType,
            RobotsNote = request.RobotsNote?.Trim(),
            TermsNote = request.TermsNote?.Trim(),
            Active = request.Active,
            Priority = request.Priority,
            RefreshInterval = TimeSpan.FromHours(request.RefreshIntervalHours),
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.Sources.Add(source);
        database.AuditEvents.Add(Audit(actor, "SourceCreated", "Source", source.Id, null, SourceAudit(source), request.Reason, context, now));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return await GetSourceAsync(source.Id, cancellationToken);
    }

    public async Task<AdminSourceResponse> UpdateSourceAsync(
        Guid id,
        AdminSourceRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ValidateSource(request, out var uri, out var authority, out var contentType);
        var source = await database.Sources.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_SOURCE_NOT_FOUND", "Source was not found.");
        if (await database.Sources.AnyAsync(value => value.Id != id && value.Url == uri.AbsoluteUri, cancellationToken))
        {
            throw new AdminOperationException(409, "ADMIN_SOURCE_DUPLICATE", "Source URL already exists.");
        }
        var before = SourceAudit(source);
        source.Name = request.Name.Trim();
        source.Url = uri.AbsoluteUri;
        source.Domain = uri.IdnHost.ToLowerInvariant();
        source.AuthorityLevel = authority;
        source.ContentType = contentType;
        source.RobotsNote = request.RobotsNote?.Trim();
        source.TermsNote = request.TermsNote?.Trim();
        source.Active = request.Active;
        source.Priority = request.Priority;
        source.RefreshInterval = TimeSpan.FromHours(request.RefreshIntervalHours);
        source.UpdatedAt = timeProvider.GetUtcNow();
        database.AuditEvents.Add(Audit(actor, "SourceUpdated", "Source", source.Id, before, SourceAudit(source), request.Reason, context, source.UpdatedAt));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return await GetSourceAsync(source.Id, cancellationToken);
    }

    public async Task DeactivateSourceAsync(
        Guid id,
        string reason,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ValidateReason(reason);
        var source = await database.Sources.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_SOURCE_NOT_FOUND", "Source was not found.");
        var before = SourceAudit(source);
        source.Active = false;
        source.UpdatedAt = timeProvider.GetUtcNow();
        database.AuditEvents.Add(Audit(actor, "SourceDeactivated", "Source", source.Id, before, SourceAudit(source), reason, context, source.UpdatedAt));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
    }

    private async Task<AdminSourceResponse> GetSourceAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var value = await database.Sources.AsNoTracking().SingleAsync(value => value.Id == id, cancellationToken);
        var evidence = await database.SourceSnapshots.AsNoTracking().Where(snapshot => snapshot.SourceId == id)
            .GroupBy(snapshot => snapshot.SourceId)
            .Select(group => new { Count = group.Count(), LastFetchedAt = group.Max(snapshot => snapshot.FetchedAt) })
            .SingleOrDefaultAsync(cancellationToken);
        var lastFetched = value.LastFetchedAt ?? evidence?.LastFetchedAt;
        return new AdminSourceResponse(
            value.Id, value.Name, value.Url, value.Domain, value.AuthorityLevel.ToString(), value.ContentType.ToString(),
            value.Active, value.Priority, (int)value.RefreshInterval.TotalHours, lastFetched,
            value.Active && (lastFetched is null || lastFetched + value.RefreshInterval < now),
            evidence?.Count ?? 0, value.RobotsNote, value.TermsNote);
    }

    private static void ValidateSource(
        AdminSourceRequest request,
        out Uri uri,
        out SourceAuthorityLevel authority,
        out SourceContentType contentType)
    {
        ValidateReason(request.Reason);
        if (string.IsNullOrWhiteSpace(request.Name)
            || !Uri.TryCreate(request.Url, UriKind.Absolute, out uri!)
            || uri.Scheme != Uri.UriSchemeHttps
            || !Enum.TryParse(request.AuthorityLevel, true, out authority)
            || !Enum.TryParse(request.ContentType, true, out contentType)
            || request.Priority < 0
            || request.RefreshIntervalHours is < 1 or > 8760)
        {
            throw new AdminOperationException(400, "ADMIN_SOURCE_INVALID", "Source requires a name, HTTPS URL, canonical authority/content type, nonnegative priority and a 1-8760 hour refresh interval.");
        }
    }

    internal static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
        {
            throw new AdminOperationException(400, "ADMIN_REASON_REQUIRED", "A specific audit reason of at least 10 characters is required.");
        }
    }

    private static void ValidateSlug(string slug, string field)
    {
        if (!SlugPattern.IsMatch(slug))
        {
            throw new AdminOperationException(400, "ADMIN_SLUG_INVALID", $"{field} must be a lowercase canonical slug.");
        }
    }

    internal static AuditEvent Audit(AdminActor actor, string action, string entityType, Guid entityId, object? before, object? after, string reason, HttpContext context, DateTimeOffset now) => new()
    {
        Actor = actor.Email,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
        AfterJson = after is null ? null : JsonSerializer.Serialize(after),
        Reason = reason.Trim(),
        OccurredAt = now,
        CorrelationId = context.TraceIdentifier,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static object SourceAudit(Source source) => new
    {
        source.Name,
        source.Url,
        AuthorityLevel = source.AuthorityLevel.ToString(),
        ContentType = source.ContentType.ToString(),
        source.Active,
        source.Priority,
        RefreshIntervalHours = (int)source.RefreshInterval.TotalHours,
    };
}

public sealed class AdminOperationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
