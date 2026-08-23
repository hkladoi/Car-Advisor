using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Sources;

public enum IngestionRunStatus
{
    Running,
    Succeeded,
    Failed,
    Partial,
}

public enum MonitoringAlertStatus
{
    Open,
    Acknowledged,
    Resolved,
}

public enum MonitoringAlertSeverity
{
    Low,
    Medium,
    High,
    Critical,
}

public sealed class IngestionJobRun : Entity
{
    public string JobType { get; set; } = string.Empty;
    public string MonitorKind { get; set; } = string.Empty;
    public string? SourceKey { get; set; }
    public Guid? SourceId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public IngestionRunStatus Status { get; set; } = IngestionRunStatus.Running;
    public int? HttpStatus { get; set; }
    public string? ParseStatus { get; set; }
    public bool? ContentChanged { get; set; }
    public string? ErrorStage { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationMilliseconds { get; set; }
}

public sealed class MonitoringAlert : Entity
{
    public string Fingerprint { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public MonitoringAlertSeverity Severity { get; set; }
    public MonitoringAlertStatus Status { get; set; } = MonitoringAlertStatus.Open;
    public string? SourceKey { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? JobRunId { get; set; }
    public string Message { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; } = 1;
    public DateTimeOffset FirstTriggeredAt { get; set; }
    public DateTimeOffset LastTriggeredAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
