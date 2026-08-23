using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Admin;

public interface IAdminMonitoringService
{
    Task<AdminMonitoringResponse> GetAsync(CancellationToken cancellationToken);
    Task AcknowledgeAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
}

public sealed class AdminMonitoringService(AppDbContext database, TimeProvider timeProvider) : IAdminMonitoringService
{
    public async Task<AdminMonitoringResponse> GetAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var since = now.AddHours(-24);
        var lastDay = await database.IngestionJobRuns.AsNoTracking()
            .Where(value => value.StartedAt >= since)
            .ToArrayAsync(cancellationToken);
        var recent = await database.IngestionJobRuns.AsNoTracking()
            .OrderByDescending(value => value.StartedAt)
            .Take(250)
            .ToArrayAsync(cancellationToken);
        var alerts = await database.MonitoringAlerts.AsNoTracking()
            .OrderBy(value => value.Status == MonitoringAlertStatus.Resolved)
            .ThenByDescending(value => value.Severity)
            .ThenByDescending(value => value.LastTriggeredAt)
            .Take(250)
            .ToArrayAsync(cancellationToken);
        var openAlerts = await database.MonitoringAlerts.AsNoTracking()
            .CountAsync(value => value.Status != MonitoringAlertStatus.Resolved, cancellationToken);
        var highCriticalAlerts = await database.MonitoringAlerts.AsNoTracking()
            .CountAsync(
                value => value.Status != MonitoringAlertStatus.Resolved
                    && (value.Severity == MonitoringAlertSeverity.High
                        || value.Severity == MonitoringAlertSeverity.Critical),
                cancellationToken);
        var monitorKinds = lastDay
            .GroupBy(value => value.MonitorKind, StringComparer.Ordinal)
            .Select(group =>
            {
                var succeeded = group.Count(value => value.Status == IngestionRunStatus.Succeeded);
                return new AdminMonitorKindResponse(
                    group.Key,
                    group.Count(),
                    succeeded,
                    decimal.Round((decimal)succeeded / group.Count(), 4),
                    group.Max(value => (DateTimeOffset?)value.StartedAt),
                    group.Where(value => value.Status == IngestionRunStatus.Succeeded)
                        .Max(value => (DateTimeOffset?)value.CompletedAt));
            })
            .OrderBy(value => value.MonitorKind)
            .ToArray();
        return new AdminMonitoringResponse(
            lastDay.Length,
            lastDay.Count(value => value.Status == IngestionRunStatus.Succeeded),
            lastDay.Count(value => value.Status == IngestionRunStatus.Failed),
            lastDay.Count(value => value.Status == IngestionRunStatus.Partial),
            lastDay.Count(value => value.ContentChanged == true),
            openAlerts,
            highCriticalAlerts,
            monitorKinds,
            recent.Select(ToRun).ToArray(),
            alerts.Select(ToAlert).ToArray(),
            now);
    }

    public async Task AcknowledgeAsync(
        Guid id,
        string reason,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(reason);
        var alert = await database.MonitoringAlerts.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_MONITORING_ALERT_NOT_FOUND", "Monitoring alert was not found.");
        if (alert.Status == MonitoringAlertStatus.Resolved)
        {
            throw new AdminOperationException(409, "ADMIN_MONITORING_ALERT_RESOLVED", "Resolved alerts cannot be acknowledged.");
        }
        if (alert.Status == MonitoringAlertStatus.Acknowledged)
        {
            throw new AdminOperationException(409, "ADMIN_MONITORING_ALERT_ALREADY_ACKNOWLEDGED", "Monitoring alert is already acknowledged.");
        }
        var now = timeProvider.GetUtcNow();
        alert.Status = MonitoringAlertStatus.Acknowledged;
        alert.AcknowledgedAt = now;
        alert.AcknowledgedBy = actor.Email;
        alert.UpdatedAt = now;
        database.AuditEvents.Add(AdminCatalogService.Audit(
            actor,
            "MonitoringAlertAcknowledged",
            "MonitoringAlert",
            alert.Id,
            new { Status = MonitoringAlertStatus.Open.ToString(), alert.AlertType, alert.SourceKey },
            new { Status = alert.Status.ToString(), alert.AcknowledgedAt, alert.AcknowledgedBy },
            reason,
            context,
            now));
        await database.SaveChangesAsync(cancellationToken);
    }

    private static AdminMonitoringRunResponse ToRun(IngestionJobRun value) => new(
        value.Id,
        value.JobType,
        value.MonitorKind,
        value.SourceKey,
        value.Status.ToString(),
        value.RequestedAt,
        value.StartedAt,
        value.CompletedAt,
        value.HttpStatus,
        value.ParseStatus,
        value.ContentChanged,
        value.ErrorStage,
        value.ErrorCode,
        value.DurationMilliseconds);

    private static AdminMonitoringAlertResponse ToAlert(MonitoringAlert value) => new(
        value.Id,
        value.AlertType,
        value.Severity.ToString(),
        value.Status.ToString(),
        value.SourceKey,
        value.JobRunId,
        value.Message,
        value.OccurrenceCount,
        value.FirstTriggeredAt,
        value.LastTriggeredAt,
        value.AcknowledgedAt,
        value.AcknowledgedBy,
        value.ResolvedAt);
}
