using VietnamCarPlatform.Domain.Admin;

namespace VietnamCarPlatform.Api.Features.Admin;

public sealed record AdminLoginRequest(string Email, string Password);

public sealed record AdminLoginResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string Email,
    string DisplayName,
    string Role);

public sealed record AdminSessionResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    DateTimeOffset ExpiresAt);

public sealed record AdminActor(
    Guid UserId,
    Guid SessionId,
    string Email,
    string DisplayName,
    AdministratorRole Role,
    DateTimeOffset ExpiresAt);

public sealed record AdminReasonRequest(string Reason);

public sealed record AdminSourceRequest(
    string Name,
    string Url,
    string AuthorityLevel,
    string ContentType,
    string? RobotsNote,
    string? TermsNote,
    bool Active,
    int Priority,
    int RefreshIntervalHours,
    string Reason);

public sealed record AdminSourceResponse(
    Guid Id,
    string Name,
    string Url,
    string Domain,
    string AuthorityLevel,
    string ContentType,
    bool Active,
    int Priority,
    int RefreshIntervalHours,
    DateTimeOffset? LastFetchedAt,
    bool Stale,
    int SnapshotCount,
    string? RobotsNote,
    string? TermsNote);

public sealed record AdminTrimDraftRequest(
    string BrandName,
    string BrandSlug,
    string? BrandCountryCode,
    string? BrandOfficialUrl,
    string ModelName,
    string ModelSlug,
    string BodyType,
    string Segment,
    string GenerationCode,
    int GenerationStartYear,
    int ModelYear,
    string TrimName,
    string TrimSlug,
    string MarketStatus,
    string Reason);

public sealed record AdminTrimUpdateRequest(
    string Name,
    string Slug,
    string MarketStatus,
    DateOnly? LaunchedAt,
    DateOnly? DiscontinuedAt,
    string Reason);

public sealed record AdminTrimRow(
    Guid TrimId,
    string BrandName,
    string ModelName,
    string GenerationCode,
    int ModelYear,
    string TrimName,
    string Slug,
    string MarketStatus,
    string BodyType,
    string Segment,
    DateTimeOffset UpdatedAt);

public sealed record AdminManualImportRequest(
    string FileName,
    string Content,
    string Reason);

public sealed record AdminImportValidationIssue(
    int? Row,
    string Field,
    string Code,
    string Severity,
    string Message);

public sealed record AdminManualImportResponse(
    Guid Id,
    string FileName,
    string Format,
    string Status,
    string ContentHash,
    int RecordCount,
    IReadOnlyList<AdminImportValidationIssue> Issues,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StagedAt);

public sealed record AdminOverrideRequest(
    string EntityType,
    Guid EntityId,
    string FieldPath,
    string NewValue,
    string Reason,
    bool LockField,
    DateTimeOffset? LockExpiresAt);

public sealed record AdminFieldLockResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string FieldPath,
    string Reason,
    string Actor,
    DateTimeOffset? ExpiresAt,
    bool Active);

public sealed record AdminReviewDecisionRequest(string Reason, string? EditedValue);

public sealed record AdminReviewItem(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string FieldPath,
    string? OldValue,
    string? NewValue,
    string RiskLevel,
    string Status,
    DateTimeOffset DetectedAt,
    string? AnomalyCode,
    string? DetectionContext,
    object? Source,
    bool FieldLocked);

public sealed record AdminPublicationResponse(
    Guid Id,
    Guid DataChangeId,
    string EntityType,
    Guid EntityId,
    string FieldPath,
    string? BeforeValue,
    string? AfterValue,
    Guid? BeforeSourceFactId,
    Guid? SourceFactId,
    string Status,
    DateTimeOffset PublishedAt,
    string PublishedBy,
    DateTimeOffset? RolledBackAt,
    string? RolledBackBy,
    string? RollbackReason);

public sealed record AdminRollbackRequest(string Reason);

public sealed record AdminCoverageBrand(
    Guid BrandId,
    string BrandName,
    bool Included,
    int Discovered,
    int Mapped,
    int Published,
    int Blocked,
    int Stale,
    decimal Completeness,
    decimal Freshness,
    int MissingCoreCount);

public sealed record AdminCoverageResponse(
    IReadOnlyList<AdminCoverageBrand> Brands,
    int BrandScopeCount,
    int ActiveModelCount,
    int ActiveTrimCount,
    decimal CoreCompleteness,
    decimal Freshness,
    int UnresolvedDuplicates,
    bool FullMarketGatePassed,
    IReadOnlyList<string> GateFailures,
    DateTimeOffset CalculatedAt);

public sealed record AdminQualityIssue(
    string Code,
    string Severity,
    string EntityType,
    Guid EntityId,
    string FieldPath,
    string Message);

public sealed record AdminQualityResponse(
    IReadOnlyList<AdminQualityIssue> Issues,
    int ImpossibleValues,
    int Duplicates,
    int StaleSources,
    int MissingCoreFields,
    int SourceConflicts,
    int DealerOfferIssues,
    DateTimeOffset CheckedAt);

public sealed record AdminAuditResponse(
    Guid Id,
    string Actor,
    string Action,
    string EntityType,
    Guid EntityId,
    string? BeforeJson,
    string? AfterJson,
    string Reason,
    DateTimeOffset OccurredAt,
    string? CorrelationId);

public sealed record AdminMonitoringRunResponse(
    Guid Id,
    string JobType,
    string MonitorKind,
    string? SourceKey,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? HttpStatus,
    string? ParseStatus,
    bool? ContentChanged,
    string? ErrorStage,
    string? ErrorCode,
    int? DurationMilliseconds);

public sealed record AdminMonitoringAlertResponse(
    Guid Id,
    string AlertType,
    string Severity,
    string Status,
    string? SourceKey,
    Guid? JobRunId,
    string Message,
    int OccurrenceCount,
    DateTimeOffset FirstTriggeredAt,
    DateTimeOffset LastTriggeredAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy,
    DateTimeOffset? ResolvedAt);

public sealed record AdminMonitorKindResponse(
    string MonitorKind,
    int RunsLast24Hours,
    int SucceededLast24Hours,
    decimal SuccessRate,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastSucceededAt);

public sealed record AdminMonitoringResponse(
    int RunsLast24Hours,
    int SucceededLast24Hours,
    int FailedLast24Hours,
    int PartialLast24Hours,
    int ContentChangesLast24Hours,
    int OpenAlerts,
    int HighCriticalAlerts,
    IReadOnlyList<AdminMonitorKindResponse> MonitorKinds,
    IReadOnlyList<AdminMonitoringRunResponse> RecentRuns,
    IReadOnlyList<AdminMonitoringAlertResponse> Alerts,
    DateTimeOffset GeneratedAt);

public sealed record AdminDealerRequest(
    Guid BrandId,
    string Name,
    string Slug,
    bool OfficialStatus,
    string? OfficialUrl,
    string Reason);

public sealed record AdminDealerResponse(
    Guid Id,
    Guid BrandId,
    string BrandName,
    string Name,
    string Slug,
    bool OfficialStatus,
    string? OfficialUrl,
    int BranchCount,
    int OfferCount);

public sealed record AdminDealerBranchRequest(
    Guid DealerId,
    string Name,
    string ProvinceCode,
    string Address,
    decimal? Latitude,
    decimal? Longitude,
    string Reason);

public sealed record AdminDealerBranchResponse(
    Guid Id,
    Guid DealerId,
    string DealerName,
    string Name,
    string ProvinceCode,
    string Address,
    decimal? Latitude,
    decimal? Longitude,
    int OfferCount);

public sealed record AdminDealerOfferBenefitRequest(
    string Type,
    decimal? CashValue,
    decimal? StatedValue,
    string Currency,
    bool IsCashEquivalent,
    string? ExclusivityGroup,
    string? Note);

public sealed record AdminDealerOfferRequest(
    Guid BranchId,
    Guid TrimId,
    string Headline,
    string? CombinabilityGroup,
    string ConditionsJson,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    Guid? SourceFactId,
    IReadOnlyList<AdminDealerOfferBenefitRequest> Benefits,
    string Reason);

public sealed record AdminDealerOfferResponse(
    Guid Id,
    Guid BranchId,
    string DealerName,
    string BranchName,
    string ProvinceCode,
    Guid TrimId,
    string TrimName,
    string Headline,
    string? CombinabilityGroup,
    string ConditionsJson,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    Guid? SourceFactId,
    IReadOnlyList<AdminDealerOfferBenefitRequest> Benefits);
