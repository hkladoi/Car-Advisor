using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Catalog;

public static class CatalogSearchSync
{
    public static PublishedDataEvent Enqueue(
        AppDbContext database,
        string publicationType,
        string aggregateType,
        Guid? aggregateId,
        string? correlationId,
        DateTimeOffset occurredAt,
        object? payload = null)
    {
        var dataEvent = new PublishedDataEvent
        {
            EventType = $"CatalogSearchSync.{publicationType}",
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            PayloadJson = JsonSerializer.Serialize(payload ?? new { aggregateType, aggregateId }),
            Status = PublishedDataEventStatus.Pending,
            Attempts = 0,
            OccurredAt = occurredAt,
            AvailableAt = occurredAt,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId,
            CreatedAt = occurredAt,
            UpdatedAt = occurredAt,
        };
        database.PublishedDataEvents.Add(dataEvent);
        return dataEvent;
    }
}

public sealed class CatalogSearchProjectionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<CatalogSearchProjectionWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> ProjectionRefreshed = LoggerMessage.Define<int>(
        LogLevel.Information,
        new EventId(1341, nameof(ProjectionRefreshed)),
        "Catalog search projection refreshed from {EventCount} published data events");
    private static readonly Action<ILogger, int, Exception?> ProjectionRefreshFailed = LoggerMessage.Define<int>(
        LogLevel.Warning,
        new EventId(1342, nameof(ProjectionRefreshFailed)),
        "Catalog search projection refresh failed for {EventCount} published data events; database retry policy scheduled");
    private static readonly Action<ILogger, Exception?> WorkerIterationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(1343, nameof(WorkerIterationFailed)),
        "Catalog search projection worker iteration failed");

    private readonly int batchSize = Math.Clamp(
        configuration.GetValue("SEARCH_SYNC_BATCH_SIZE", 250), 1, 1000);
    private readonly TimeSpan idleDelay = TimeSpan.FromMilliseconds(Math.Clamp(
        configuration.GetValue("SEARCH_SYNC_INTERVAL_MILLISECONDS", 500), 100, 10_000));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nextDelay = idleDelay;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var connection = database.Database.GetDbConnection();
                var openedHere = connection.State != ConnectionState.Open;
                if (openedHere)
                {
                    await connection.OpenAsync(stoppingToken);
                }
                int processed;
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT process_catalog_search_events(@batch_size)";
                    command.CommandTimeout = 30;
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "batch_size";
                    parameter.Value = batchSize;
                    command.Parameters.Add(parameter);
                    processed = Convert.ToInt32(
                        await command.ExecuteScalarAsync(stoppingToken),
                        CultureInfo.InvariantCulture);
                }
                finally
                {
                    if (openedHere)
                    {
                        await connection.CloseAsync();
                    }
                }
                if (processed > 0)
                {
                    await scope.ServiceProvider.GetRequiredService<CatalogCache>()
                        .InvalidateAsync(stoppingToken);
                    ProjectionRefreshed(logger, processed, null);
                    nextDelay = TimeSpan.FromMilliseconds(25);
                }
                else if (processed < 0)
                {
                    ProjectionRefreshFailed(logger, -processed, null);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                WorkerIterationFailed(logger, error);
            }

            await Task.Delay(nextDelay, stoppingToken);
        }
    }
}
