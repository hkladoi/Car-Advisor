using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Api.Features.Catalog;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class CatalogSearchSyncTests
{
    [Fact]
    public void EnqueueCreatesPendingTransactionalProjectionEvent()
    {
        using var database = CreateContext();
        var aggregateId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 24, 3, 0, 0, TimeSpan.Zero);

        var dataEvent = CatalogSearchSync.Enqueue(
            database,
            "CatalogTrimUpdated",
            "Trim",
            aggregateId,
            "trace-v34",
            now,
            new { field = "search_text" });

        Assert.Equal("CatalogSearchSync.CatalogTrimUpdated", dataEvent.EventType);
        Assert.Equal("Trim", dataEvent.AggregateType);
        Assert.Equal(aggregateId, dataEvent.AggregateId);
        Assert.Equal(PublishedDataEventStatus.Pending, dataEvent.Status);
        Assert.Equal(0, dataEvent.Attempts);
        Assert.Equal(now, dataEvent.AvailableAt);
        Assert.Contains("search_text", dataEvent.PayloadJson, StringComparison.Ordinal);
        Assert.Equal(EntityState.Added, database.Entry(dataEvent).State);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model_only;Password=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }
}
