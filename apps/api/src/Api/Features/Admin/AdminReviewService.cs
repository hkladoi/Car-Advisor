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
    Task DecideAsync(Guid id, bool approved, AdminReviewDecisionRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
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
                            source.Name,
                            source.Url,
                            Authority = source.AuthorityLevel.ToString(),
                            snapshot.FetchedAt,
                            snapshot.ContentHash,
                            snapshot.ObjectKey,
                            snapshot.ParserVersion,
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
            value.SourceFactId is Guid sourceFactId
                ? sources.GetValueOrDefault(sourceFactId)
                : sourceEntityProvenance.GetValueOrDefault(value.EntityId),
            locks.Any(fieldLock => fieldLock.EntityId == value.EntityId
                && fieldLock.EntityType.Equals(value.EntityType, StringComparison.OrdinalIgnoreCase)
                && fieldLock.FieldPath.Equals(value.FieldPath, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
    }

    public async Task DecideAsync(
        Guid id,
        bool approved,
        AdminReviewDecisionRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(request.Reason);
        var change = await database.DataChanges.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_CHANGE_NOT_FOUND", "Review item was not found.");
        if (change.Status is not (ChangeStatus.PendingReview or ChangeStatus.Detected))
        {
            throw new AdminOperationException(409, "ADMIN_CHANGE_ALREADY_REVIEWED", "Review item already has a terminal decision.");
        }
        var now = timeProvider.GetUtcNow();
        object? published = null;
        if (approved && request.EditedValue is not null)
        {
            published = await ApplyOverrideValueAsync(change.EntityType, change.EntityId, change.FieldPath, request.EditedValue, request.Reason, cancellationToken);
        }
        change.Status = approved ? ChangeStatus.Approved : ChangeStatus.Rejected;
        change.NewValue = request.EditedValue ?? change.NewValue;
        change.UpdatedAt = now;
        var audit = AdminCatalogService.Audit(
            actor,
            approved ? (request.EditedValue is null ? "DataChangeApproved" : "DataChangeEditedAndPublished") : "DataChangeRejected",
            "DataChange",
            change.Id,
            new { Status = "PendingReview", change.OldValue, change.NewValue },
            new { Status = change.Status.ToString(), EditedValue = request.EditedValue, Published = published },
            request.Reason,
            context,
            now);
        database.AuditEvents.Add(audit);
        change.ReviewedAuditEventId = audit.Id;
        await database.SaveChangesAsync(cancellationToken);
        if (published is not null && IsCatalogReadModelEntity(change.EntityType))
        {
            await AdminCatalogService.RefreshCatalogReadModelAsync(database, cancellationToken);
        }
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
        await database.SaveChangesAsync(cancellationToken);
        if (IsCatalogReadModelEntity(request.EntityType))
        {
            await AdminCatalogService.RefreshCatalogReadModelAsync(database, cancellationToken);
        }
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
