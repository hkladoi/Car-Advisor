using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Admin;

public enum AdministratorRole
{
    Viewer,
    Editor,
    Reviewer,
    Administrator,
}

public enum ManualImportStatus
{
    Invalid,
    Validated,
    StagedForReview,
    Rejected,
}

public sealed class AdminUser : Entity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AdministratorRole Role { get; set; } = AdministratorRole.Viewer;
    public bool Active { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class AdminSession : Entity
{
    public Guid AdminUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? ClientFingerprintHash { get; set; }

    public bool IsActiveAt(DateTimeOffset instant) => RevokedAt is null && ExpiresAt > instant;
}

public sealed class FieldLock : Entity
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string FieldPath { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Active { get; set; } = true;

    public bool IsActiveAt(DateTimeOffset instant) => Active && (ExpiresAt is null || ExpiresAt > instant);
}

public sealed class ManualImport : Entity
{
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string ContentText { get; set; } = string.Empty;
    public ManualImportStatus Status { get; set; }
    public string ValidationReportJson { get; set; } = "{}";
    public string SubmittedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? StagedAt { get; set; }
}
