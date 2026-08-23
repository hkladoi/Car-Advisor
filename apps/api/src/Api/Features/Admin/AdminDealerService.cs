using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Api.Features.Catalog;
using VietnamCarPlatform.Domain.Admin;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Admin;

public interface IAdminDealerService
{
    Task<IReadOnlyList<AdminDealerResponse>> GetDealersAsync(CancellationToken cancellationToken);
    Task<AdminDealerResponse> CreateDealerAsync(AdminDealerRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<AdminDealerResponse> UpdateDealerAsync(Guid id, AdminDealerRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task DeleteDealerAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminDealerBranchResponse>> GetBranchesAsync(CancellationToken cancellationToken);
    Task<AdminDealerBranchResponse> CreateBranchAsync(AdminDealerBranchRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<AdminDealerBranchResponse> UpdateBranchAsync(Guid id, AdminDealerBranchRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task DeleteBranchAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminDealerOfferResponse>> GetOffersAsync(CancellationToken cancellationToken);
    Task<AdminDealerOfferResponse> CreateOfferAsync(AdminDealerOfferRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<AdminDealerOfferResponse> UpdateOfferAsync(Guid id, AdminDealerOfferRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task DeleteOfferAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
}

public sealed class AdminDealerService(
    AppDbContext database,
    TimeProvider timeProvider,
    CatalogCache catalogCache) : IAdminDealerService
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<AdminDealerResponse>> GetDealersAsync(CancellationToken cancellationToken) =>
        await (
                from dealer in database.Dealers.AsNoTracking()
                join brand in database.Brands.AsNoTracking() on dealer.BrandId equals brand.Id
                orderby brand.Name, dealer.Name
                select new AdminDealerResponse(
                    dealer.Id,
                    dealer.BrandId,
                    brand.Name,
                    dealer.Name,
                    dealer.Slug,
                    dealer.OfficialStatus,
                    dealer.OfficialUrl,
                    database.DealerBranches.Count(branch => branch.DealerId == dealer.Id),
                    (from branch in database.DealerBranches where branch.DealerId == dealer.Id
                     join offer in database.DealerOffers on branch.Id equals offer.BranchId select offer).Count()))
            .Take(1000)
            .ToArrayAsync(cancellationToken);

    public async Task<AdminDealerResponse> CreateDealerAsync(
        AdminDealerRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ValidateDealer(request);
        if (!await database.Brands.AnyAsync(value => value.Id == request.BrandId, cancellationToken))
        {
            throw Error(404, "ADMIN_DEALER_BRAND_NOT_FOUND", "Dealer brand was not found.");
        }
        if (await database.Dealers.AnyAsync(value => value.BrandId == request.BrandId && value.Slug == request.Slug, cancellationToken))
        {
            throw Error(409, "ADMIN_DEALER_DUPLICATE", "Dealer slug already exists for this brand.");
        }
        var now = timeProvider.GetUtcNow();
        var dealer = new Dealer
        {
            BrandId = request.BrandId,
            Name = request.Name.Trim(),
            Slug = request.Slug,
            OfficialStatus = request.OfficialStatus,
            OfficialUrl = request.OfficialUrl?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.Dealers.Add(dealer);
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerCreated", "Dealer", dealer.Id, null, DealerAudit(dealer), request.Reason, context, now));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return await GetDealerAsync(dealer.Id, cancellationToken);
    }

    public async Task<AdminDealerResponse> UpdateDealerAsync(
        Guid id,
        AdminDealerRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ValidateDealer(request);
        var dealer = await database.Dealers.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw Error(404, "ADMIN_DEALER_NOT_FOUND", "Dealer was not found.");
        if (!await database.Brands.AnyAsync(value => value.Id == request.BrandId, cancellationToken))
        {
            throw Error(404, "ADMIN_DEALER_BRAND_NOT_FOUND", "Dealer brand was not found.");
        }
        if (await database.Dealers.AnyAsync(value => value.Id != id && value.BrandId == request.BrandId && value.Slug == request.Slug, cancellationToken))
        {
            throw Error(409, "ADMIN_DEALER_DUPLICATE", "Dealer slug already exists for this brand.");
        }
        var before = DealerAudit(dealer);
        dealer.BrandId = request.BrandId;
        dealer.Name = request.Name.Trim();
        dealer.Slug = request.Slug;
        dealer.OfficialStatus = request.OfficialStatus;
        dealer.OfficialUrl = request.OfficialUrl?.Trim();
        dealer.UpdatedAt = timeProvider.GetUtcNow();
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerUpdated", "Dealer", dealer.Id, before, DealerAudit(dealer), request.Reason, context, dealer.UpdatedAt));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return await GetDealerAsync(dealer.Id, cancellationToken);
    }

    public async Task DeleteDealerAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(reason);
        var dealer = await database.Dealers.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw Error(404, "ADMIN_DEALER_NOT_FOUND", "Dealer was not found.");
        var branchIds = await database.DealerBranches.Where(value => value.DealerId == id).Select(value => value.Id).ToArrayAsync(cancellationToken);
        if (await database.DealerOffers.AnyAsync(value => branchIds.Contains(value.BranchId) && value.Status == OfferStatus.Published, cancellationToken))
        {
            throw Error(409, "ADMIN_DEALER_HAS_PUBLISHED_OFFERS", "A dealer with published offer history cannot be deleted; expire the offer and retain provenance.");
        }
        var before = DealerAudit(dealer);
        database.Dealers.Remove(dealer);
        var now = timeProvider.GetUtcNow();
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerDeleted", "Dealer", dealer.Id, before, null, reason, context, now));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminDealerBranchResponse>> GetBranchesAsync(CancellationToken cancellationToken) =>
        await (
                from branch in database.DealerBranches.AsNoTracking()
                join dealer in database.Dealers.AsNoTracking() on branch.DealerId equals dealer.Id
                orderby dealer.Name, branch.ProvinceCode, branch.Name
                select new AdminDealerBranchResponse(
                    branch.Id, branch.DealerId, dealer.Name, branch.Name, branch.ProvinceCode, branch.Address,
                    branch.Latitude, branch.Longitude, database.DealerOffers.Count(offer => offer.BranchId == branch.Id)))
            .Take(2000)
            .ToArrayAsync(cancellationToken);

    public async Task<AdminDealerBranchResponse> CreateBranchAsync(
        AdminDealerBranchRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        await ValidateBranchAsync(request, null, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var branch = new DealerBranch
        {
            DealerId = request.DealerId,
            Name = request.Name.Trim(),
            ProvinceCode = request.ProvinceCode.Trim().ToUpperInvariant(),
            Address = request.Address.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.DealerBranches.Add(branch);
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerBranchCreated", "DealerBranch", branch.Id, null, BranchAudit(branch), request.Reason, context, now));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return await GetBranchAsync(branch.Id, cancellationToken);
    }

    public async Task<AdminDealerBranchResponse> UpdateBranchAsync(
        Guid id,
        AdminDealerBranchRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        await ValidateBranchAsync(request, id, cancellationToken);
        var branch = await database.DealerBranches.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw Error(404, "ADMIN_DEALER_BRANCH_NOT_FOUND", "Dealer branch was not found.");
        var before = BranchAudit(branch);
        branch.DealerId = request.DealerId;
        branch.Name = request.Name.Trim();
        branch.ProvinceCode = request.ProvinceCode.Trim().ToUpperInvariant();
        branch.Address = request.Address.Trim();
        branch.Latitude = request.Latitude;
        branch.Longitude = request.Longitude;
        branch.UpdatedAt = timeProvider.GetUtcNow();
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerBranchUpdated", "DealerBranch", branch.Id, before, BranchAudit(branch), request.Reason, context, branch.UpdatedAt));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return await GetBranchAsync(branch.Id, cancellationToken);
    }

    public async Task DeleteBranchAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(reason);
        var branch = await database.DealerBranches.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw Error(404, "ADMIN_DEALER_BRANCH_NOT_FOUND", "Dealer branch was not found.");
        if (await database.DealerOffers.AnyAsync(value => value.BranchId == id && value.Status == OfferStatus.Published, cancellationToken))
        {
            throw Error(409, "ADMIN_BRANCH_HAS_PUBLISHED_OFFERS", "A branch with published offer history cannot be deleted.");
        }
        var before = BranchAudit(branch);
        database.DealerBranches.Remove(branch);
        var now = timeProvider.GetUtcNow();
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerBranchDeleted", "DealerBranch", branch.Id, before, null, reason, context, now));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminDealerOfferResponse>> GetOffersAsync(CancellationToken cancellationToken)
    {
        var rows = (await OfferQuery().Take(2000).ToArrayAsync(cancellationToken))
            .OrderByDescending(value => value.Offer.EffectiveFrom)
            .ToArray();
        return await MapOffersAsync(rows, cancellationToken);
    }

    public async Task<AdminDealerOfferResponse> CreateOfferAsync(
        AdminDealerOfferRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var status = await ValidateOfferAsync(request, null, cancellationToken);
        var now = timeProvider.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var offer = new DealerOffer
        {
            BranchId = request.BranchId,
            TrimId = request.TrimId,
            Headline = request.Headline.Trim(),
            CombinabilityGroup = NullIfBlank(request.CombinabilityGroup),
            ConditionsJson = CanonicalJson(request.ConditionsJson),
            Status = status,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            SourceFactId = request.SourceFactId,
            ManualOverrideReason = request.SourceFactId is null ? request.Reason.Trim() : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.DealerOffers.Add(offer);
        foreach (var benefit in request.Benefits)
        {
            database.DealerOfferBenefits.Add(ToBenefit(offer.Id, benefit, now));
        }
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerOfferCreated", "DealerOffer", offer.Id, null, OfferAudit(offer, request.Benefits), request.Reason, context, now));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return await GetOfferAsync(offer.Id, cancellationToken);
    }

    public async Task<AdminDealerOfferResponse> UpdateOfferAsync(
        Guid id,
        AdminDealerOfferRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var status = await ValidateOfferAsync(request, id, cancellationToken);
        var offer = await database.DealerOffers.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw Error(404, "ADMIN_DEALER_OFFER_NOT_FOUND", "Dealer offer was not found.");
        var previousBenefits = await database.DealerOfferBenefits.Where(value => value.OfferId == id).ToArrayAsync(cancellationToken);
        var before = OfferAudit(offer, previousBenefits.Select(ToBenefitRequest).ToArray());
        offer.BranchId = request.BranchId;
        offer.TrimId = request.TrimId;
        offer.Headline = request.Headline.Trim();
        offer.CombinabilityGroup = NullIfBlank(request.CombinabilityGroup);
        offer.ConditionsJson = CanonicalJson(request.ConditionsJson);
        offer.Status = status;
        offer.EffectiveFrom = request.EffectiveFrom;
        offer.EffectiveTo = request.EffectiveTo;
        offer.SourceFactId = request.SourceFactId;
        offer.ManualOverrideReason = request.SourceFactId is null ? request.Reason.Trim() : null;
        offer.UpdatedAt = timeProvider.GetUtcNow();
        database.DealerOfferBenefits.RemoveRange(previousBenefits);
        foreach (var benefit in request.Benefits)
        {
            database.DealerOfferBenefits.Add(ToBenefit(offer.Id, benefit, offer.UpdatedAt));
        }
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerOfferUpdated", "DealerOffer", offer.Id, before, OfferAudit(offer, request.Benefits), request.Reason, context, offer.UpdatedAt));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return await GetOfferAsync(offer.Id, cancellationToken);
    }

    public async Task DeleteOfferAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(reason);
        var offer = await database.DealerOffers.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw Error(404, "ADMIN_DEALER_OFFER_NOT_FOUND", "Dealer offer was not found.");
        if (offer.Status == OfferStatus.Published)
        {
            throw Error(409, "ADMIN_PUBLISHED_OFFER_DELETE_FORBIDDEN", "Published offers are history; set status to Expired instead of deleting them.");
        }
        var benefits = await database.DealerOfferBenefits.Where(value => value.OfferId == id).ToArrayAsync(cancellationToken);
        var before = OfferAudit(offer, benefits.Select(ToBenefitRequest).ToArray());
        database.DealerOffers.Remove(offer);
        var now = timeProvider.GetUtcNow();
        database.AuditEvents.Add(AdminCatalogService.Audit(actor, "DealerOfferDeleted", "DealerOffer", offer.Id, before, null, reason, context, now));
        await database.SaveChangesAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
    }

    private async Task ValidateBranchAsync(AdminDealerBranchRequest request, Guid? id, CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(request.Reason);
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Address))
        {
            throw Error(400, "ADMIN_DEALER_BRANCH_INVALID", "Branch name and address are required.");
        }
        var provinceCode = request.ProvinceCode.Trim().ToUpperInvariant();
        if (!await database.Dealers.AnyAsync(value => value.Id == request.DealerId, cancellationToken)
            || !await database.Regions.AnyAsync(value => value.Code == provinceCode, cancellationToken))
        {
            throw Error(404, "ADMIN_DEALER_BRANCH_REFERENCE_NOT_FOUND", "Dealer or canonical province code was not found.");
        }
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
        {
            throw Error(400, "ADMIN_DEALER_BRANCH_INVALID", "Latitude or longitude is outside its canonical range.");
        }
        if (await database.DealerBranches.AnyAsync(value => value.Id != id && value.DealerId == request.DealerId && value.Name == request.Name.Trim() && value.ProvinceCode == provinceCode, cancellationToken))
        {
            throw Error(409, "ADMIN_DEALER_BRANCH_DUPLICATE", "Dealer branch identity already exists.");
        }
    }

    private async Task<OfferStatus> ValidateOfferAsync(AdminDealerOfferRequest request, Guid? id, CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(request.Reason);
        if (string.IsNullOrWhiteSpace(request.Headline) || request.Headline.Trim().Length > 500)
        {
            throw Error(400, "ADMIN_DEALER_OFFER_INVALID", "Offer headline is required and limited to 500 characters.");
        }
        if (request.EffectiveTo is not null && request.EffectiveFrom >= request.EffectiveTo)
        {
            throw Error(400, "ADMIN_DEALER_OFFER_INVALID", "Offer effectiveTo must be later than effectiveFrom.");
        }
        if (!Enum.TryParse<OfferStatus>(request.Status, true, out var status))
        {
            throw Error(400, "ADMIN_DEALER_OFFER_INVALID", "Offer status is not canonical.");
        }
        var branch = await database.DealerBranches.AsNoTracking().SingleOrDefaultAsync(value => value.Id == request.BranchId, cancellationToken)
            ?? throw Error(404, "ADMIN_DEALER_BRANCH_NOT_FOUND", "Dealer branch was not found.");
        if (!await database.Trims.AnyAsync(value => value.Id == request.TrimId, cancellationToken))
        {
            throw Error(404, "ADMIN_TRIM_NOT_FOUND", "Offer trim was not found.");
        }
        if (request.SourceFactId is Guid sourceFactId && !await database.SourceFacts.AnyAsync(value => value.Id == sourceFactId, cancellationToken))
        {
            throw Error(404, "ADMIN_SOURCE_FACT_NOT_FOUND", "Offer source fact was not found.");
        }
        _ = CanonicalJson(request.ConditionsJson);
        if (request.Benefits.Count == 0 || request.Benefits.Count > 50)
        {
            throw Error(400, "ADMIN_DEALER_OFFER_INVALID", "An offer requires 1-50 structured benefits.");
        }
        foreach (var benefit in request.Benefits)
        {
            if (!Enum.TryParse<BenefitType>(benefit.Type, true, out _)
                || benefit.CashValue is < 0 || benefit.StatedValue is < 0
                || benefit.Currency.Trim().Length != 3
                || (benefit.IsCashEquivalent && benefit.CashValue is null))
            {
                throw Error(400, "ADMIN_DEALER_OFFER_BENEFIT_INVALID", "Benefit type, nonnegative values, currency and cash-equivalent semantics are required.");
            }
        }
        var quality = DealerOfferQualityEvaluator.Evaluate(
            new DealerOfferQualityInput(
                id ?? Guid.NewGuid(), request.BranchId, branch.ProvinceCode, request.TrimId,
                request.EffectiveFrom, request.EffectiveTo, status.ToString(), CanonicalJson(request.ConditionsJson),
                request.SourceFactId is not null || !string.IsNullOrWhiteSpace(request.Reason),
                request.Benefits.Select(value => new DealerOfferBenefitQualityInput(
                    Guid.NewGuid(), value.Type, value.ExclusivityGroup, value.CashValue, value.StatedValue)).ToArray()),
            timeProvider.GetUtcNow());
        if (quality.Count > 0)
        {
            throw new AdminOperationException(400, "ADMIN_DEALER_OFFER_QUALITY_FAILED", string.Join(" ", quality.Select(value => $"{value.Code}: {value.Message}")));
        }
        return status;
    }

    private static void ValidateDealer(AdminDealerRequest request)
    {
        AdminCatalogService.ValidateReason(request.Reason);
        if (string.IsNullOrWhiteSpace(request.Name) || !SlugPattern.IsMatch(request.Slug))
        {
            throw Error(400, "ADMIN_DEALER_INVALID", "Dealer name and canonical slug are required.");
        }
        if (request.OfficialUrl is not null
            && (!Uri.TryCreate(request.OfficialUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            throw Error(400, "ADMIN_DEALER_INVALID", "Dealer official URL must be absolute HTTPS.");
        }
    }

    private IQueryable<OfferRow> OfferQuery() =>
        from offer in database.DealerOffers.AsNoTracking()
        join branch in database.DealerBranches.AsNoTracking() on offer.BranchId equals branch.Id
        join dealer in database.Dealers.AsNoTracking() on branch.DealerId equals dealer.Id
        join trim in database.Trims.AsNoTracking() on offer.TrimId equals trim.Id
        select new OfferRow(offer, branch, dealer, trim);

    private async Task<IReadOnlyList<AdminDealerOfferResponse>> MapOffersAsync(OfferRow[] rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(value => value.Offer.Id).ToArray();
        var benefits = await database.DealerOfferBenefits.AsNoTracking().Where(value => ids.Contains(value.OfferId)).OrderBy(value => value.CreatedAt).ToArrayAsync(cancellationToken);
        return rows.Select(row => new AdminDealerOfferResponse(
            row.Offer.Id, row.Branch.Id, row.Dealer.Name, row.Branch.Name, row.Branch.ProvinceCode,
            row.Trim.Id, row.Trim.Name, row.Offer.Headline, row.Offer.CombinabilityGroup, row.Offer.ConditionsJson,
            row.Offer.Status.ToString(), row.Offer.EffectiveFrom, row.Offer.EffectiveTo, row.Offer.SourceFactId,
            benefits.Where(value => value.OfferId == row.Offer.Id).Select(ToBenefitRequest).ToArray())).ToArray();
    }

    private async Task<AdminDealerResponse> GetDealerAsync(Guid id, CancellationToken cancellationToken) =>
        (await GetDealersAsync(cancellationToken)).Single(value => value.Id == id);

    private async Task<AdminDealerBranchResponse> GetBranchAsync(Guid id, CancellationToken cancellationToken) =>
        (await GetBranchesAsync(cancellationToken)).Single(value => value.Id == id);

    private async Task<AdminDealerOfferResponse> GetOfferAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await (
                from offer in database.DealerOffers.AsNoTracking()
                join branch in database.DealerBranches.AsNoTracking() on offer.BranchId equals branch.Id
                join dealer in database.Dealers.AsNoTracking() on branch.DealerId equals dealer.Id
                join trim in database.Trims.AsNoTracking() on offer.TrimId equals trim.Id
                where offer.Id == id
                select new OfferRow(offer, branch, dealer, trim))
            .SingleAsync(cancellationToken);
        return (await MapOffersAsync([row], cancellationToken)).Single();
    }

    private static DealerOfferBenefit ToBenefit(Guid offerId, AdminDealerOfferBenefitRequest request, DateTimeOffset now) => new()
    {
        OfferId = offerId,
        Type = Enum.Parse<BenefitType>(request.Type, true),
        CashValue = request.CashValue,
        StatedValue = request.StatedValue,
        Currency = request.Currency.Trim().ToUpperInvariant(),
        IsCashEquivalent = request.IsCashEquivalent,
        ExclusivityGroup = NullIfBlank(request.ExclusivityGroup),
        Note = NullIfBlank(request.Note),
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static AdminDealerOfferBenefitRequest ToBenefitRequest(DealerOfferBenefit value) =>
        new(value.Type.ToString(), value.CashValue, value.StatedValue, value.Currency, value.IsCashEquivalent, value.ExclusivityGroup, value.Note);

    private static object DealerAudit(Dealer value) => new { value.BrandId, value.Name, value.Slug, value.OfficialStatus, value.OfficialUrl };
    private static object BranchAudit(DealerBranch value) => new { value.DealerId, value.Name, value.ProvinceCode, value.Address, value.Latitude, value.Longitude };
    private static object OfferAudit(DealerOffer value, IReadOnlyList<AdminDealerOfferBenefitRequest> benefits) => new
    {
        value.BranchId, value.TrimId, value.Headline, value.CombinabilityGroup, value.ConditionsJson,
        Status = value.Status.ToString(), value.EffectiveFrom, value.EffectiveTo, value.SourceFactId, Benefits = benefits,
    };

    private static string CanonicalJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Conditions root must be an object.");
            }
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw Error(400, "ADMIN_DEALER_OFFER_CONDITIONS_INVALID", "Offer conditions must be a valid JSON object.");
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AdminOperationException Error(int status, string code, string message) => new(status, code, message);
    private sealed record OfferRow(DealerOffer Offer, DealerBranch Branch, Dealer Dealer, Trim Trim);
}
