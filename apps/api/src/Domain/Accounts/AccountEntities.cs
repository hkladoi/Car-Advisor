using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Accounts;

public sealed class UserAccount : Entity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset ConsentedAt { get; set; }
    public string PrivacyPolicyVersion { get; set; } = string.Empty;
}

public sealed class UserSession : Entity
{
    public Guid UserAccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? ClientFingerprintHash { get; set; }
}

public sealed class SavedComparison : Entity
{
    public Guid UserAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TrimIdsJson { get; set; } = "[]";
    public string RegionCode { get; set; } = string.Empty;
    public string ProfilePreset { get; set; } = string.Empty;
    public string FinancingPreset { get; set; } = string.Empty;
}

public sealed class WatchlistEntry : Entity
{
    public Guid UserAccountId { get; set; }
    public Guid TrimId { get; set; }
    public string RegionCode { get; set; } = "VN";
    public decimal? TargetPrice { get; set; }
    public bool PriceAlerts { get; set; } = true;
    public bool PromotionAlerts { get; set; } = true;
    public bool DealerOfferAlerts { get; set; } = true;
}
