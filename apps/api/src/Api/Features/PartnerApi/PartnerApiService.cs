using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Api.Features.Admin;
using VietnamCarPlatform.Domain.Partners;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.PartnerApi;

public sealed record PartnerApiAccess(
    Guid KeyId,
    string Name,
    string KeyPrefix,
    string Scope,
    string PlanCode,
    int RequestsPerMinute,
    long RequestsPerMonth,
    int MaxPageSize,
    DateTimeOffset? ExpiresAt);

public interface IPartnerApiService
{
    Task<PartnerApiAccess?> AuthenticateAsync(string token, CancellationToken cancellationToken);
    Task<PartnerApiPolicyResponse> GetPolicyAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminPartnerApiKeyResponse>> GetKeysAsync(CancellationToken cancellationToken);
    Task<AdminPartnerApiKeyIssuedResponse> IssueKeyAsync(
        AdminPartnerApiKeyCreateRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken);
    Task<AdminPartnerApiKeyResponse> RevokeKeyAsync(
        Guid id,
        string reason,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken);
}

public sealed class PartnerApiService(
    AppDbContext database,
    TimeProvider timeProvider) : IPartnerApiService
{
    public async Task<PartnerApiAccess?> AuthenticateAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (!PartnerApiKeyMaterial.TryGetPrefix(token, out var prefix))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var row = await (
                from key in database.PartnerApiKeys.AsNoTracking()
                join plan in database.PartnerApiUsagePlans.AsNoTracking()
                    on key.UsagePlanId equals plan.Id
                where key.KeyPrefix == prefix
                select new { Key = key, Plan = plan })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null
            || !row.Plan.Active
            || !row.Key.IsActiveAt(now)
            || row.Key.Scope != PartnerApiPolicy.ReadScope
            || row.Key.PolicyVersion != PartnerApiPolicy.PolicyVersion
            || !PartnerApiKeyMaterial.FixedTimeEquals(row.Key.KeyHash, token))
        {
            return null;
        }

        return new PartnerApiAccess(
            row.Key.Id,
            row.Key.Name,
            row.Key.KeyPrefix,
            row.Key.Scope,
            row.Plan.Code,
            row.Plan.RequestsPerMinute,
            row.Plan.RequestsPerMonth,
            row.Plan.MaxPageSize,
            row.Key.ExpiresAt);
    }

    public async Task<PartnerApiPolicyResponse> GetPolicyAsync(CancellationToken cancellationToken)
    {
        var plans = await database.PartnerApiUsagePlans.AsNoTracking()
            .Where(value => value.Active)
            .OrderBy(value => value.RequestsPerMinute)
            .Select(value => new PartnerApiUsagePlanResponse(
                value.Code,
                value.Name,
                value.RequestsPerMinute,
                value.RequestsPerMonth,
                value.MaxPageSize))
            .ToArrayAsync(cancellationToken);

        return new PartnerApiPolicyResponse(
            PartnerApiPolicy.ContractVersion,
            PartnerApiPolicy.PolicyVersion,
            PartnerApiPolicy.ReadScope,
            PartnerApiPolicy.LicenseId,
            true,
            PartnerApiPolicy.Attribution,
            "docs/api/data-attribution-policy.md",
            [
                "Read normalized published facts and their provenance for catalog research and integration.",
                "Cache responses within source-specific terms while retaining effective dates and source references."
            ],
            [
                "Do not republish source page copy, images or assets unless a separate rights record permits it.",
                "Do not remove source attribution, convert unknowns to facts or relabel cohort data as trim-specific.",
                "Do not use an API key for write operations or expose it in browser bundles, URLs or logs."
            ],
            plans,
            timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<AdminPartnerApiKeyResponse>> GetKeysAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await (
                from key in database.PartnerApiKeys.AsNoTracking()
                join plan in database.PartnerApiUsagePlans.AsNoTracking()
                    on key.UsagePlanId equals plan.Id
                orderby key.IssuedAt descending
                select new { Key = key, PlanCode = plan.Code })
            .ToArrayAsync(cancellationToken);
        return rows.Select(value => ToResponse(value.Key, value.PlanCode, now)).ToArray();
    }

    public async Task<AdminPartnerApiKeyIssuedResponse> IssueKeyAsync(
        AdminPartnerApiKeyCreateRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(request.Reason);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AdminOperationException(
                400, "PARTNER_API_KEY_NAME_INVALID", "API key name must contain 3 to 160 characters.");
        }
        var name = request.Name.Trim();
        if (name.Length is < 3 or > 160)
        {
            throw new AdminOperationException(
                400, "PARTNER_API_KEY_NAME_INVALID", "API key name must contain 3 to 160 characters.");
        }
        if (request.PolicyVersion != PartnerApiPolicy.PolicyVersion)
        {
            throw new AdminOperationException(
                409,
                "PARTNER_API_POLICY_NOT_ACCEPTED",
                $"Policy version {PartnerApiPolicy.PolicyVersion} must be accepted before issuing a key.");
        }

        if (string.IsNullOrWhiteSpace(request.PlanCode))
        {
            throw new AdminOperationException(
                400, "PARTNER_API_PLAN_INVALID", "The requested active usage plan was not found.");
        }
        var planCode = request.PlanCode.Trim().ToLowerInvariant();
        var plan = await database.PartnerApiUsagePlans
            .SingleOrDefaultAsync(value => value.Code == planCode && value.Active, cancellationToken)
            ?? throw new AdminOperationException(
                400, "PARTNER_API_PLAN_INVALID", "The requested active usage plan was not found.");
        var now = timeProvider.GetUtcNow();
        if (request.ExpiresAt is not null && request.ExpiresAt <= now)
        {
            throw new AdminOperationException(
                400, "PARTNER_API_KEY_EXPIRY_INVALID", "API key expiry must be in the future.");
        }

        var token = string.Empty;
        var prefix = string.Empty;
        var hash = string.Empty;
        var allocated = false;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (token, prefix, hash) = PartnerApiKeyMaterial.Generate();
            if (!await database.PartnerApiKeys.AnyAsync(
                    value => value.KeyPrefix == prefix,
                    cancellationToken))
            {
                allocated = true;
                break;
            }
        }
        if (!allocated)
        {
            throw new InvalidOperationException("Could not allocate a unique partner API key prefix.");
        }

        var key = new PartnerApiKey
        {
            UsagePlanId = plan.Id,
            Name = name,
            KeyPrefix = prefix,
            KeyHash = hash,
            Scope = PartnerApiPolicy.ReadScope,
            PolicyVersion = PartnerApiPolicy.PolicyVersion,
            IssuedAt = now,
            IssuedBy = actor.Email,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.PartnerApiKeys.Add(key);
        database.AuditEvents.Add(AdminCatalogService.Audit(
            actor,
            "PartnerApiKeyIssued",
            "PartnerApiKey",
            key.Id,
            null,
            new
            {
                key.Name,
                key.KeyPrefix,
                key.Scope,
                Plan = plan.Code,
                key.PolicyVersion,
                key.ExpiresAt,
            },
            request.Reason,
            context,
            now));
        await database.SaveChangesAsync(cancellationToken);

        return new AdminPartnerApiKeyIssuedResponse(
            ToResponse(key, plan.Code, now),
            token,
            "Copy this value now. Only its SHA-256 hash is stored and the plaintext key cannot be recovered.");
    }

    public async Task<AdminPartnerApiKeyResponse> RevokeKeyAsync(
        Guid id,
        string reason,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(reason);
        var row = await (
                from key in database.PartnerApiKeys
                join plan in database.PartnerApiUsagePlans on key.UsagePlanId equals plan.Id
                where key.Id == id
                select new { Key = key, PlanCode = plan.Code })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new AdminOperationException(
                404, "PARTNER_API_KEY_NOT_FOUND", "Partner API key was not found.");
        if (row.Key.RevokedAt is not null)
        {
            throw new AdminOperationException(
                409, "PARTNER_API_KEY_ALREADY_REVOKED", "Partner API key is already revoked.");
        }

        var now = timeProvider.GetUtcNow();
        row.Key.RevokedAt = now;
        row.Key.RevokedBy = actor.Email;
        row.Key.RevocationReason = reason.Trim();
        row.Key.UpdatedAt = now;
        database.AuditEvents.Add(AdminCatalogService.Audit(
            actor,
            "PartnerApiKeyRevoked",
            "PartnerApiKey",
            row.Key.Id,
            new { Status = "Active", row.Key.KeyPrefix, Plan = row.PlanCode },
            new { Status = "Revoked", row.Key.RevokedAt, row.Key.RevokedBy },
            reason,
            context,
            now));
        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(row.Key, row.PlanCode, now);
    }

    public static PartnerApiMetadata Metadata(DateTimeOffset generatedAt) => new(
        PartnerApiPolicy.ContractVersion,
        PartnerApiPolicy.PolicyVersion,
        PartnerApiPolicy.LicenseId,
        PartnerApiPolicy.Attribution,
        PartnerApiPolicy.PolicyPath,
        generatedAt);

    private static AdminPartnerApiKeyResponse ToResponse(
        PartnerApiKey key,
        string planCode,
        DateTimeOffset now) => new(
        key.Id,
        key.Name,
        key.KeyPrefix,
        key.Scope,
        planCode,
        key.PolicyVersion,
        key.RevokedAt is not null ? "Revoked" : key.ExpiresAt <= now ? "Expired" : "Active",
        key.IssuedAt,
        key.IssuedBy,
        key.ExpiresAt,
        key.RevokedAt,
        key.RevokedBy);
}
