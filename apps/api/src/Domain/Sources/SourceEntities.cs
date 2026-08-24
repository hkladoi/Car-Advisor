using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Sources;

public enum SourceAuthorityLevel
{
    Unknown,
    CompetentAuthority,
    BrandOfficial,
    DistributorOfficial,
    DealerOfficial,
    TrustedSecondary,
    DiscoveryOnly,
}

public enum SourceContentType
{
    Html,
    Pdf,
    Json,
    Xml,
    Csv,
    Image,
    ManualDocument,
}

public enum ChangeRiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}

public enum ChangeStatus
{
    Detected,
    AutoPublished,
    PendingReview,
    Approved,
    Rejected,
    Superseded,
    Published,
    RolledBack,
}

public enum PublicationStatus
{
    Published,
    RolledBack,
}

public sealed class Source : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Category { get; set; } = "unknown";
    public SourceAuthorityLevel AuthorityLevel { get; set; } = SourceAuthorityLevel.Unknown;
    public SourceContentType ContentType { get; set; }
    public string? RobotsNote { get; set; }
    public string? TermsNote { get; set; }
    public bool Active { get; set; } = true;
    public int Priority { get; set; }
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromDays(1);
    public DateTimeOffset? LastFetchedAt { get; set; }
}

public sealed class SourceSnapshot : Entity
{
    public Guid SourceId { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public int HttpStatus { get; set; }
    public string ParserVersion { get; set; } = string.Empty;
    public string? Etag { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public string? FetchError { get; set; }
}

public sealed class SourceFact : Entity
{
    public Guid SnapshotId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string FieldPath { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public string? NormalizedValue { get; set; }
    public FactStatus Status { get; set; } = FactStatus.Unknown;
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Unknown;
    public string? ExtractionContext { get; set; }
}

public sealed class DataChange : Entity
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string FieldPath { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public ChangeRiskLevel RiskLevel { get; set; }
    public ChangeStatus Status { get; set; } = ChangeStatus.Detected;
    public DateTimeOffset DetectedAt { get; set; }
    public string? AnomalyCode { get; set; }
    public string? DetectionContext { get; set; }
    public Guid? SourceFactId { get; set; }
    public Guid? ReviewedAuditEventId { get; set; }
}

public sealed class PublicationVersion : Entity
{
    public Guid DataChangeId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string FieldPath { get; set; } = string.Empty;
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public Guid? BeforeSourceFactId { get; set; }
    public Guid? SourceFactId { get; set; }
    public PublicationStatus Status { get; set; } = PublicationStatus.Published;
    public DateTimeOffset PublishedAt { get; set; }
    public string PublishedBy { get; set; } = string.Empty;
    public DateTimeOffset? RolledBackAt { get; set; }
    public string? RolledBackBy { get; set; }
    public string? RollbackReason { get; set; }
}

public sealed class AuditEvent : Entity
{
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class CoverageMetric : Entity
{
    public Guid? BrandId { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? TrimId { get; set; }
    public decimal Completeness { get; set; }
    public decimal Freshness { get; set; }
    public int MissingCoreCount { get; set; }
    public int DiscoveredCount { get; set; }
    public int MappedCount { get; set; }
    public int PublishedCount { get; set; }
    public int BlockedCount { get; set; }
    public int StaleCount { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
}

public enum MarketCandidateKind
{
    Model,
    Trim,
}

public enum MarketCandidateResolution
{
    Published,
    BlockedWithReason,
}

public enum TrimInventoryStatus
{
    NotApplicable,
    Complete,
    BlockedWithReason,
}

public sealed class MarketCandidate : Entity
{
    public string Market { get; set; } = "VN";
    public Guid BrandId { get; set; }
    public Guid SourceId { get; set; }
    public Guid EvidenceSnapshotId { get; set; }
    public string ExternalKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentExternalKey { get; set; }
    public MarketCandidateKind Kind { get; set; }
    public MarketStatus MarketStatus { get; set; } = MarketStatus.Unknown;
    public MarketCandidateResolution Resolution { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? TrimId { get; set; }
    public string? BlockedReason { get; set; }
    public TrimInventoryStatus TrimInventoryStatus { get; set; }
    public string? TrimInventoryReason { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ReviewedAt { get; set; }
    public string ReviewedBy { get; set; } = string.Empty;
}

public sealed class MarketScopeReview : Entity
{
    public string Market { get; set; } = "VN";
    public string SchemaVersion { get; set; } = string.Empty;
    public string ManifestHash { get; set; } = string.Empty;
    public int ReviewedBrandCount { get; set; }
    public int IncludedBrandCount { get; set; }
    public int ExcludedBrandCount { get; set; }
    public int ModelCandidateCount { get; set; }
    public int TrimCandidateCount { get; set; }
    public Guid PolicySourceId { get; set; }
    public Guid PolicySnapshotId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset ReviewedAt { get; set; }
    public string ReviewedBy { get; set; } = string.Empty;
    public string ReviewReason { get; set; } = string.Empty;
}
