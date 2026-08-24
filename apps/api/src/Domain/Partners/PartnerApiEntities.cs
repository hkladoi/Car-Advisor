using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Partners;

public sealed class PartnerApiUsagePlan : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RequestsPerMinute { get; set; }
    public long RequestsPerMonth { get; set; }
    public int MaxPageSize { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class PartnerApiKey : Entity
{
    public Guid UsagePlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string Scope { get; set; } = "catalog.read";
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public string IssuedBy { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }

    public bool IsActiveAt(DateTimeOffset instant) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > instant);
}
