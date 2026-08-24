using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Catalog;

public enum PublishedDataEventStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}

public sealed class PublishedDataEvent : Entity
{
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid? AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public PublishedDataEventStatus Status { get; set; } = PublishedDataEventStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
    public string? CorrelationId { get; set; }
}
