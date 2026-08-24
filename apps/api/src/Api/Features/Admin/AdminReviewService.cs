using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Api.Features.Catalog;
using VietnamCarPlatform.Domain.Admin;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Admin;

public interface IAdminReviewService
{
    Task<IReadOnlyList<AdminReviewItem>> GetQueueAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminPublicationResponse>> GetPublicationsAsync(int take, CancellationToken cancellationToken);
    Task DecideAsync(Guid id, bool approved, AdminReviewDecisionRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task RollbackAsync(Guid publicationId, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<AdminFieldLockResponse?> OverrideAsync(AdminOverrideRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminFieldLockResponse>> GetLocksAsync(CancellationToken cancellationToken);
    Task UnlockAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
}

public sealed class AdminReviewService(AppDbContext database, TimeProvider timeProvider, CatalogCache catalogCache) : IAdminReviewService
{
    public async Task<IReadOnlyList<AdminReviewItem>> GetQueueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await database.DataChanges.AsNoTracking()
            .Where(value => value.Status == ChangeStatus.PendingReview || value.Status == ChangeStatus.Detected)
            .OrderByDescending(value => value.RiskLevel)
            .ThenBy(value => value.DetectedAt)
            .Take(500)
            .ToArrayAsync(cancellationToken);
        var sourceFactIds = rows.Where(value => value.SourceFactId is not null).Select(value => value.SourceFactId!.Value).Distinct().ToArray();
        var sources = sourceFactIds.Length == 0
            ? new Dictionary<Guid, object>()
            : await (
                    from fact in database.SourceFacts.AsNoTracking()
                    join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                    join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                    where sourceFactIds.Contains(fact.Id)
                    select new
                    {
                        fact.Id,
                        Value = (object)new
                        {
                            SourceFactId = fact.Id,
                            SnapshotId = snapshot.Id,
                            source.Name,
                            source.Url,
                            Authority = source.AuthorityLevel.ToString(),
                            snapshot.FetchedAt,
                            snapshot.ContentHash,
                            snapshot.ObjectKey,
                            snapshot.ParserVersion,
                            fact.RawValue,
                            fact.NormalizedValue,
                            fact.ExtractionContext,
                            FactStatus = fact.Status.ToString(),
                            Confidence = fact.Confidence.ToString(),
                        },
                    })
                .ToDictionaryAsync(value => value.Id, value => value.Value, cancellationToken);
        var sourceEntityIds = rows
            .Where(value => value.EntityType.Equals("Source", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.EntityId)
            .Distinct()
            .ToArray();
        var sourceEntities = sourceEntityIds.Length == 0
            ? []
            : await database.Sources.AsNoTracking()
                .Where(value => sourceEntityIds.Contains(value.Id))
                .ToArrayAsync(cancellationToken);
        var sourceSnapshots = sourceEntityIds.Length == 0
            ? []
            : await database.SourceSnapshots.AsNoTracking()
                .Where(value => sourceEntityIds.Contains(value.SourceId))
                .OrderByDescending(value => value.FetchedAt)
                .ToArrayAsync(cancellationToken);
        var sourceEntityProvenance = sourceEntities.ToDictionary(
            source => source.Id,
            source =>
            {
                var snapshot = sourceSnapshots.FirstOrDefault(value => value.SourceId == source.Id);
                return (object)new
                {
                    source.Name,
                    source.Url,
                    Authority = source.AuthorityLevel.ToString(),
                    FetchedAt = snapshot?.FetchedAt ?? source.LastFetchedAt,
                    snapshot?.ContentHash,
                    snapshot?.ObjectKey,
                    snapshot?.ParserVersion,
                    SnapshotId = snapshot?.Id,
                };
            });
        var locks = await database.FieldLocks.AsNoTracking()
            .Where(value => value.Active && (value.ExpiresAt == null || value.ExpiresAt > now))
            .Select(value => new { value.EntityType, value.EntityId, value.FieldPath })
            .ToArrayAsync(cancellationToken);
        return rows.Select(value => new AdminReviewItem(
            value.Id,
            value.EntityType,
            value.EntityId,
            value.FieldPath,
            value.OldValue,
            value.NewValue,
            value.RiskLevel.ToString(),
            value.Status.ToString(),
            value.DetectedAt,
            value.AnomalyCode,
            value.DetectionContext,
            value.SourceFactId is Guid sourceFactId
                ? sources.GetValueOrDefault(sourceFactId)
                : sourceEntityProvenance.GetValueOrDefault(value.EntityId),
            locks.Any(fieldLock => fieldLock.EntityId == value.EntityId
                && fieldLock.EntityType.Equals(value.EntityType, StringComparison.OrdinalIgnoreCase)
                && fieldLock.FieldPath.Equals(value.FieldPath, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
    }

    public async Task<IReadOnlyList<AdminPublicationResponse>> GetPublicationsAsync(
        int take,
        CancellationToken cancellationToken) =>
        await database.PublicationVersions.AsNoTracking()
            .OrderByDescending(value => value.PublishedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(value => new AdminPublicationResponse(
                value.Id,
                value.DataChangeId,
                value.EntityType,
                value.EntityId,
                value.FieldPath,
                value.BeforeValue,
                value.AfterValue,
                value.BeforeSourceFactId,
                value.SourceFactId,
                value.Status.ToString(),
                value.PublishedAt,
                value.PublishedBy,
                value.RolledBackAt,
                value.RolledBackBy,
                value.RollbackReason))
            .ToArrayAsync(cancellationToken);

    public async Task DecideAsync(
        Guid id,
        bool approved,
        AdminReviewDecisionRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(request.Reason);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var change = await database.DataChanges.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_CHANGE_NOT_FOUND", "Review item was not found.");
        if (change.Status is not (ChangeStatus.PendingReview or ChangeStatus.Detected))
        {
            throw new AdminOperationException(409, "ADMIN_CHANGE_ALREADY_REVIEWED", "Review item already has a terminal decision.");
        }
        var now = timeProvider.GetUtcNow();
        var originalStatus = change.Status;
        var originalNewValue = change.NewValue;
        object? published = null;
        PublicationVersion? publication = null;
        if (approved && change.SourceFactId is not null
            && change.EntityType.Equals("Trim", StringComparison.OrdinalIgnoreCase))
        {
            var publishedValue = request.EditedValue ?? change.NewValue
                ?? throw new AdminOperationException(400, "ADMIN_CANDIDATE_VALUE_REQUIRED", "Candidate publication requires a normalized value.");
            publication = await PublishCandidateAsync(change, publishedValue, request.EditedValue is null ? null : request.Reason, actor, now, cancellationToken);
            database.PublicationVersions.Add(publication);
            published = new
            {
                publication.Id,
                publication.EntityType,
                publication.EntityId,
                publication.FieldPath,
                publication.BeforeValue,
                publication.AfterValue,
                publication.SourceFactId,
            };
        }
        else if (approved && request.EditedValue is not null)
        {
            published = await ApplyOverrideValueAsync(change.EntityType, change.EntityId, change.FieldPath, request.EditedValue, request.Reason, cancellationToken);
        }
        change.Status = !approved
            ? ChangeStatus.Rejected
            : publication is null ? ChangeStatus.Approved : ChangeStatus.Published;
        change.NewValue = request.EditedValue ?? change.NewValue;
        change.UpdatedAt = now;
        var audit = AdminCatalogService.Audit(
            actor,
            approved
                ? publication is null ? "DataChangeApproved" : request.EditedValue is null ? "DataChangePublished" : "DataChangeEditedAndPublished"
                : "DataChangeRejected",
            "DataChange",
            change.Id,
            new { Status = originalStatus.ToString(), change.OldValue, NewValue = originalNewValue },
            new { Status = change.Status.ToString(), EditedValue = request.EditedValue, Published = published },
            request.Reason,
            context,
            now);
        database.AuditEvents.Add(audit);
        change.ReviewedAuditEventId = audit.Id;
        var searchProjectionChanged = publication is not null
            || (published is not null && IsCatalogReadModelEntity(change.EntityType));
        if (searchProjectionChanged)
        {
            CatalogSearchSync.Enqueue(
                database,
                publication is null ? "ManualOverridePublished" : "ReviewedChangePublished",
                change.EntityType,
                change.EntityId,
                context.TraceIdentifier,
                now,
                new { change.FieldPath, change.Status, publication?.Id });
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
    }

    public async Task RollbackAsync(
        Guid publicationId,
        string reason,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(reason);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var publication = await database.PublicationVersions
            .SingleOrDefaultAsync(value => value.Id == publicationId, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_PUBLICATION_NOT_FOUND", "Published version was not found.");
        if (publication.Status != PublicationStatus.Published)
        {
            throw new AdminOperationException(409, "ADMIN_PUBLICATION_ALREADY_ROLLED_BACK", "Published version has already been rolled back.");
        }
        var latest = await database.PublicationVersions.AsNoTracking()
            .Where(value => value.EntityType == publication.EntityType
                && value.EntityId == publication.EntityId
                && value.FieldPath == publication.FieldPath)
            .OrderByDescending(value => value.PublishedAt)
            .ThenByDescending(value => value.CreatedAt)
            .Select(value => value.Id)
            .FirstAsync(cancellationToken);
        if (latest != publication.Id)
        {
            throw new AdminOperationException(409, "ADMIN_ROLLBACK_NOT_LATEST", "Only the latest publication for an entity field can be rolled back.");
        }
        if (!publication.EntityType.Equals("Trim", StringComparison.OrdinalIgnoreCase))
        {
            throw new AdminOperationException(400, "ADMIN_ROLLBACK_MAPPING_UNSUPPORTED", "This publication does not have a typed rollback mapping.");
        }

        var now = timeProvider.GetUtcNow();
        var beforeRollback = await ApplyCanonicalValueAsync(
            publication.EntityId,
            publication.FieldPath,
            publication.BeforeValue,
            publication.BeforeSourceFactId,
            reason,
            now,
            cancellationToken);
        var previousPublicationStatus = publication.Status;
        publication.Status = PublicationStatus.RolledBack;
        publication.RolledBackAt = now;
        publication.RolledBackBy = actor.Email;
        publication.RollbackReason = reason.Trim();
        publication.UpdatedAt = now;
        var change = await database.DataChanges.SingleAsync(value => value.Id == publication.DataChangeId, cancellationToken);
        change.Status = ChangeStatus.RolledBack;
        change.UpdatedAt = now;
        database.AuditEvents.Add(AdminCatalogService.Audit(
            actor,
            "PublicationRolledBack",
            "PublicationVersion",
            publication.Id,
            new { Status = previousPublicationStatus.ToString(), CurrentValue = beforeRollback.BeforeValue, CurrentSourceFactId = beforeRollback.BeforeSourceFactId },
            new { Status = PublicationStatus.RolledBack.ToString(), RestoredValue = publication.BeforeValue, publication.BeforeSourceFactId },
            reason,
            context,
            now));
        CatalogSearchSync.Enqueue(
            database, "PublicationRolledBack", publication.EntityType, publication.EntityId,
            context.TraceIdentifier, now, new { publication.FieldPath, publication.Id });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
    }

    public async Task<AdminFieldLockResponse?> OverrideAsync(
        AdminOverrideRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(request.Reason);
        if (request.LockExpiresAt <= timeProvider.GetUtcNow())
        {
            throw new AdminOperationException(400, "ADMIN_LOCK_EXPIRY_INVALID", "Field lock expiry must be in the future.");
        }
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var beforeAfter = await ApplyOverrideValueAsync(request.EntityType, request.EntityId, request.FieldPath, request.NewValue, request.Reason, cancellationToken);
        FieldLock? created = null;
        if (request.LockField)
        {
            var previous = await database.FieldLocks
                .Where(value => value.EntityType == request.EntityType && value.EntityId == request.EntityId && value.FieldPath == request.FieldPath && value.Active)
                .ToArrayAsync(cancellationToken);
            foreach (var fieldLock in previous)
            {
                fieldLock.Active = false;
                fieldLock.UpdatedAt = now;
            }
            created = new FieldLock
            {
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                FieldPath = request.FieldPath,
                Reason = request.Reason.Trim(),
                Actor = actor.Email,
                ExpiresAt = request.LockExpiresAt,
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            database.FieldLocks.Add(created);
        }
        database.AuditEvents.Add(AdminCatalogService.Audit(
            actor,
            request.LockField ? "ManualOverrideWithFieldLock" : "ManualOverride",
            request.EntityType,
            request.EntityId,
            null,
            new { request.FieldPath, request.NewValue, Applied = beforeAfter, FieldLockId = created?.Id, request.LockExpiresAt },
            request.Reason,
            context,
            now));
        if (IsCatalogReadModelEntity(request.EntityType))
        {
            CatalogSearchSync.Enqueue(
                database, "ManualOverridePublished", request.EntityType, request.EntityId,
                context.TraceIdentifier, now, new { request.FieldPath, request.LockField });
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await catalogCache.InvalidateAsync(cancellationToken);
        return created is null ? null : ToResponse(created);
    }

    public async Task<IReadOnlyList<AdminFieldLockResponse>> GetLocksAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await database.FieldLocks.AsNoTracking()
            .Where(value => value.Active && (value.ExpiresAt == null || value.ExpiresAt > now))
            .OrderBy(value => value.EntityType)
            .ThenBy(value => value.FieldPath)
            .Select(value => new AdminFieldLockResponse(value.Id, value.EntityType, value.EntityId, value.FieldPath, value.Reason, value.Actor, value.ExpiresAt, value.Active))
            .ToArrayAsync(cancellationToken);
    }

    public async Task UnlockAsync(
        Guid id,
        string reason,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(reason);
        var fieldLock = await database.FieldLocks.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_FIELD_LOCK_NOT_FOUND", "Field lock was not found.");
        var now = timeProvider.GetUtcNow();
        fieldLock.Active = false;
        fieldLock.UpdatedAt = now;
        database.AuditEvents.Add(AdminCatalogService.Audit(
            actor,
            "FieldUnlocked",
            fieldLock.EntityType,
            fieldLock.EntityId,
            new { fieldLock.Id, fieldLock.FieldPath, fieldLock.ExpiresAt, Active = true },
            new { fieldLock.Id, fieldLock.FieldPath, Active = false },
            reason,
            context,
            now));
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<PublicationVersion> PublishCandidateAsync(
        DataChange change,
        string value,
        string? manualReason,
        AdminActor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fact = await database.SourceFacts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == change.SourceFactId, cancellationToken)
            ?? throw new AdminOperationException(409, "ADMIN_CANDIDATE_FACT_MISSING", "Candidate SourceFact no longer exists.");
        if (!fact.EntityType.Equals("Trim", StringComparison.OrdinalIgnoreCase) || fact.EntityId != change.EntityId)
        {
            throw new AdminOperationException(409, "ADMIN_CANDIDATE_ENTITY_MISMATCH", "Candidate SourceFact is not mapped to this trim.");
        }
        if (manualReason is null && !string.Equals(fact.NormalizedValue, value, StringComparison.Ordinal))
        {
            throw new AdminOperationException(409, "ADMIN_CANDIDATE_VALUE_MISMATCH", "Candidate value no longer matches its SourceFact.");
        }
        var nowUtc = timeProvider.GetUtcNow();
        var locked = await database.FieldLocks.AsNoTracking().AnyAsync(fieldLock =>
            fieldLock.EntityType == "Trim"
            && fieldLock.EntityId == change.EntityId
            && fieldLock.FieldPath == change.FieldPath
            && fieldLock.Active
            && (fieldLock.ExpiresAt == null || fieldLock.ExpiresAt > nowUtc), cancellationToken);
        if (locked)
        {
            throw new AdminOperationException(409, "ADMIN_FIELD_LOCKED", "An active field lock blocks candidate publication.");
        }

        var applied = await ApplyCanonicalValueAsync(
            change.EntityId,
            change.FieldPath,
            value,
            fact.Id,
            manualReason,
            now,
            cancellationToken);
        if (!ValuesEqual(applied.BeforeValue, change.OldValue))
        {
            throw new AdminOperationException(409, "ADMIN_CANDIDATE_STALE", "Canonical value changed after detection; run change detection again.");
        }
        return new PublicationVersion
        {
            DataChangeId = change.Id,
            EntityType = "Trim",
            EntityId = change.EntityId,
            FieldPath = change.FieldPath,
            BeforeValue = applied.BeforeValue,
            AfterValue = CanonicalDecimal(value),
            BeforeSourceFactId = applied.BeforeSourceFactId,
            SourceFactId = fact.Id,
            Status = PublicationStatus.Published,
            PublishedAt = now,
            PublishedBy = actor.Email,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task<AppliedCanonicalValue> ApplyCanonicalValueAsync(
        Guid trimId,
        string fieldPath,
        string? newValue,
        Guid? sourceFactId,
        string? manualReason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var decimalValue = ParseCanonicalDecimal(newValue, fieldPath);
        if (fieldPath.Equals("price.msrp_vnd", StringComparison.OrdinalIgnoreCase))
        {
            var price = await database.Prices
                .Where(value => value.TrimId == trimId
                    && value.PriceType == PriceType.Msrp
                    && value.Status == PriceStatus.Official
                    && value.EffectiveFrom <= now
                    && (value.EffectiveTo == null || value.EffectiveTo > now))
                .OrderByDescending(value => value.Priority)
                .ThenByDescending(value => value.Version)
                .FirstOrDefaultAsync(cancellationToken);
            var before = price?.Amount is decimal amount ? CanonicalDecimal(amount) : null;
            var beforeSource = price?.SourceFactId;
            if (price is null)
            {
                if (decimalValue is null)
                {
                    return new AppliedCanonicalValue(null, null);
                }
                var version = await database.Prices
                    .Where(value => value.TrimId == trimId && value.PriceType == PriceType.Msrp && value.RegionScope == "VN")
                    .Select(value => (int?)value.Version)
                    .MaxAsync(cancellationToken) ?? 0;
                price = new Price
                {
                    TrimId = trimId,
                    PriceType = PriceType.Msrp,
                    Amount = decimalValue,
                    Currency = "VND",
                    RegionScope = "VN",
                    Status = PriceStatus.Official,
                    Priority = 100,
                    Version = version + 1,
                    EffectiveFrom = now,
                    SourceFactId = sourceFactId,
                    ManualOverrideReason = CleanReason(manualReason),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                database.Prices.Add(price);
            }
            else
            {
                ArchivePrice(price, now);
                if (decimalValue is null)
                {
                    price.Status = PriceStatus.Withdrawn;
                    price.EffectiveTo = now;
                }
                else
                {
                    price.Amount = decimalValue;
                    price.SourceFactId = sourceFactId;
                    price.ManualOverrideReason = CleanReason(manualReason);
                }
                price.UpdatedAt = now;
            }
            return new AppliedCanonicalValue(before, beforeSource);
        }

        var specCode = fieldPath.ToLowerInvariant() switch
        {
            "spec.length_mm" => "LENGTH_MM",
            "spec.width_mm" => "WIDTH_MM",
            "spec.height_mm" => "HEIGHT_MM",
            "spec.wheelbase_mm" => "WHEELBASE_MM",
            "spec.seats" => "SEATS",
            _ => null,
        };
        if (specCode is not null)
        {
            var definition = await database.SpecDefinitions.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Code == specCode, cancellationToken)
                ?? throw new AdminOperationException(409, "ADMIN_CANONICAL_MAPPING_MISSING", $"Spec definition {specCode} is not seeded.");
            var spec = await database.TrimSpecs.SingleOrDefaultAsync(
                value => value.TrimId == trimId && value.SpecDefinitionId == definition.Id,
                cancellationToken);
            var before = spec?.NumericValue is decimal amount ? CanonicalDecimal(amount) : null;
            var beforeSource = spec?.SourceFactId;
            if (spec is null)
            {
                spec = new TrimSpec
                {
                    TrimId = trimId,
                    SpecDefinitionId = definition.Id,
                    CreatedAt = now,
                };
                database.TrimSpecs.Add(spec);
            }
            spec.Status = decimalValue is null ? FactStatus.Unknown : FactStatus.Official;
            spec.NumericValue = decimalValue;
            spec.TextValue = null;
            spec.EnumValue = null;
            spec.OriginalValue = newValue;
            spec.OriginalUnit = specCode.EndsWith("_MM", StringComparison.Ordinal) ? "mm" : "seat";
            spec.SourceFactId = sourceFactId;
            spec.ManualOverrideReason = CleanReason(manualReason);
            spec.UpdatedAt = now;
            return new AppliedCanonicalValue(before, beforeSource);
        }

        if (fieldPath is "powertrain.power_kw" or "powertrain.torque_nm")
        {
            var profile = await database.PowertrainProfiles.SingleOrDefaultAsync(value => value.TrimId == trimId, cancellationToken);
            var before = profile is null
                ? null
                : fieldPath == "powertrain.power_kw"
                    ? profile.CombinedPowerKw ?? profile.MotorPowerKw ?? profile.EnginePowerKw
                    : profile.TorqueNm;
            var beforeSource = profile?.SourceFactId;
            if (profile is null)
            {
                profile = new PowertrainProfile { TrimId = trimId, Type = PowertrainType.Unknown, CreatedAt = now };
                database.PowertrainProfiles.Add(profile);
            }
            if (fieldPath == "powertrain.power_kw") profile.CombinedPowerKw = decimalValue;
            else profile.TorqueNm = decimalValue;
            profile.SourceFactId = sourceFactId;
            profile.ManualOverrideReason = CleanReason(manualReason);
            profile.UpdatedAt = now;
            return new AppliedCanonicalValue(before is null ? null : CanonicalDecimal(before.Value), beforeSource);
        }

        if (fieldPath.StartsWith("energy.", StringComparison.OrdinalIgnoreCase))
        {
            var profile = await database.EnergyProfiles.SingleOrDefaultAsync(value => value.TrimId == trimId, cancellationToken);
            decimal? before = profile is null ? null : fieldPath switch
            {
                "energy.usable_battery_kwh" => profile.UsableBatteryKwh,
                "energy.official_range_km" => profile.OfficialRangeKm,
                "energy.fuel_litres_per_100km" => profile.OfficialFuelLitresPer100Km,
                "energy.electric_kwh_per_100km" => profile.OfficialElectricKwhPer100Km,
                _ => throw Unsupported("Trim", fieldPath),
            };
            var beforeSource = profile?.SourceFactId;
            if (profile is null)
            {
                profile = new EnergyProfile { TrimId = trimId, CreatedAt = now };
                database.EnergyProfiles.Add(profile);
            }
            switch (fieldPath)
            {
                case "energy.usable_battery_kwh": profile.UsableBatteryKwh = decimalValue; break;
                case "energy.official_range_km": profile.OfficialRangeKm = decimalValue; break;
                case "energy.fuel_litres_per_100km": profile.OfficialFuelLitresPer100Km = decimalValue; break;
                case "energy.electric_kwh_per_100km": profile.OfficialElectricKwhPer100Km = decimalValue; break;
                default: throw Unsupported("Trim", fieldPath);
            }
            profile.SourceFactId = sourceFactId;
            profile.ManualOverrideReason = CleanReason(manualReason);
            profile.UpdatedAt = now;
            return new AppliedCanonicalValue(before is null ? null : CanonicalDecimal(before.Value), beforeSource);
        }

        throw new AdminOperationException(
            400,
            "ADMIN_CANDIDATE_FIELD_UNSUPPORTED",
            $"Candidate field Trim.{fieldPath} has no typed V2.4 publication mapping.");
    }

    private void ArchivePrice(Price price, DateTimeOffset now) => database.PriceHistory.Add(new PriceHistory
    {
        PriceId = price.Id,
        TrimId = price.TrimId,
        PriceType = price.PriceType,
        Amount = price.Amount,
        Currency = price.Currency,
        RegionScope = price.RegionScope,
        Status = price.Status,
        EffectiveFrom = price.EffectiveFrom,
        EffectiveTo = price.EffectiveTo ?? now,
        SourceFactId = price.SourceFactId,
        ManualOverrideReason = price.ManualOverrideReason,
        ArchivedAt = now,
        CreatedAt = now,
        UpdatedAt = now,
    });

    private static decimal? ParseCanonicalDecimal(string? value, string fieldPath)
    {
        if (value is null) return null;
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) || result <= 0)
        {
            throw new AdminOperationException(400, "ADMIN_CANDIDATE_VALUE_INVALID", $"{fieldPath} must be a positive invariant decimal.");
        }
        if (fieldPath.Equals("spec.seats", StringComparison.OrdinalIgnoreCase) && decimal.Truncate(result) != result)
        {
            throw new AdminOperationException(400, "ADMIN_CANDIDATE_VALUE_INVALID", "Seat count must be an integer.");
        }
        return result;
    }

    private static string CanonicalDecimal(string value) =>
        CanonicalDecimal(decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture));

    private static string CanonicalDecimal(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string? CleanReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static bool ValuesEqual(string? left, string? right)
    {
        if (left is null || right is null) return left == right;
        return decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(right, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber)
                ? leftNumber == rightNumber
                : string.Equals(left, right, StringComparison.Ordinal);
    }

    private sealed record AppliedCanonicalValue(string? BeforeValue, Guid? BeforeSourceFactId);

    private async Task<object> ApplyOverrideValueAsync(
        string entityType,
        Guid entityId,
        string fieldPath,
        string newValue,
        string reason,
        CancellationToken cancellationToken)
    {
        if (entityType.Equals("Trim", StringComparison.OrdinalIgnoreCase))
        {
            var trim = await database.Trims.SingleOrDefaultAsync(value => value.Id == entityId, cancellationToken)
                ?? throw new AdminOperationException(404, "ADMIN_OVERRIDE_ENTITY_NOT_FOUND", "Trim was not found.");
            var before = fieldPath.ToLowerInvariant() switch
            {
                "name" => trim.Name,
                "marketstatus" => trim.MarketStatus.ToString(),
                _ => throw Unsupported(entityType, fieldPath),
            };
            if (fieldPath.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                trim.Name = RequiredValue(newValue);
            }
            else if (Enum.TryParse<MarketStatus>(newValue, true, out var status))
            {
                trim.MarketStatus = status;
            }
            else
            {
                throw new AdminOperationException(400, "ADMIN_OVERRIDE_VALUE_INVALID", "Market status is not canonical.");
            }
            trim.UpdatedAt = timeProvider.GetUtcNow();
            return new { Before = before, After = newValue };
        }
        if (entityType.Equals("Price", StringComparison.OrdinalIgnoreCase))
        {
            var price = await database.Prices.SingleOrDefaultAsync(value => value.Id == entityId, cancellationToken)
                ?? throw new AdminOperationException(404, "ADMIN_OVERRIDE_ENTITY_NOT_FOUND", "Price was not found.");
            object? before;
            switch (fieldPath.ToLowerInvariant())
            {
                case "amount":
                    before = price.Amount;
                    if (!decimal.TryParse(newValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                    {
                        throw new AdminOperationException(400, "ADMIN_OVERRIDE_VALUE_INVALID", "Price amount must be a positive invariant decimal.");
                    }
                    price.Amount = amount;
                    break;
                case "status":
                    before = price.Status.ToString();
                    if (!Enum.TryParse<PriceStatus>(newValue, true, out var priceStatus))
                    {
                        throw new AdminOperationException(400, "ADMIN_OVERRIDE_VALUE_INVALID", "Price status is not canonical.");
                    }
                    price.Status = priceStatus;
                    break;
                case "effectiveto":
                    before = price.EffectiveTo;
                    if (!DateTimeOffset.TryParse(newValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var effectiveTo)
                        || effectiveTo <= price.EffectiveFrom)
                    {
                        throw new AdminOperationException(400, "ADMIN_OVERRIDE_VALUE_INVALID", "EffectiveTo must be an ISO timestamp after EffectiveFrom.");
                    }
                    price.EffectiveTo = effectiveTo;
                    break;
                default:
                    throw Unsupported(entityType, fieldPath);
            }
            price.ManualOverrideReason = reason.Trim();
            price.UpdatedAt = timeProvider.GetUtcNow();
            return new { Before = before, After = newValue };
        }
        if (entityType.Equals("Source", StringComparison.OrdinalIgnoreCase))
        {
            var source = await database.Sources.SingleOrDefaultAsync(value => value.Id == entityId, cancellationToken)
                ?? throw new AdminOperationException(404, "ADMIN_OVERRIDE_ENTITY_NOT_FOUND", "Source was not found.");
            object before;
            switch (fieldPath.ToLowerInvariant())
            {
                case "active":
                    before = source.Active;
                    if (!bool.TryParse(newValue, out var active))
                    {
                        throw new AdminOperationException(400, "ADMIN_OVERRIDE_VALUE_INVALID", "Source active override must be true or false.");
                    }
                    source.Active = active;
                    break;
                case "priority":
                    before = source.Priority;
                    if (!int.TryParse(newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority) || priority < 0)
                    {
                        throw new AdminOperationException(400, "ADMIN_OVERRIDE_VALUE_INVALID", "Source priority must be a nonnegative integer.");
                    }
                    source.Priority = priority;
                    break;
                default:
                    throw Unsupported(entityType, fieldPath);
            }
            source.UpdatedAt = timeProvider.GetUtcNow();
            return new { Before = before, After = newValue };
        }
        throw Unsupported(entityType, fieldPath);
    }

    private static AdminOperationException Unsupported(string entityType, string fieldPath) => new(
        400,
        "ADMIN_OVERRIDE_FIELD_UNSUPPORTED",
        $"Manual override is not allowed for {entityType}.{fieldPath}; use a typed editor or stage a reviewed import.");

    private static bool IsCatalogReadModelEntity(string entityType) =>
        entityType.Equals("Trim", StringComparison.OrdinalIgnoreCase)
        || entityType.Equals("Price", StringComparison.OrdinalIgnoreCase);

    private static string RequiredValue(string value) => !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : throw new AdminOperationException(400, "ADMIN_OVERRIDE_VALUE_INVALID", "Override value cannot be blank.");

    private static AdminFieldLockResponse ToResponse(FieldLock value) =>
        new(value.Id, value.EntityType, value.EntityId, value.FieldPath, value.Reason, value.Actor, value.ExpiresAt, value.Active);
}
