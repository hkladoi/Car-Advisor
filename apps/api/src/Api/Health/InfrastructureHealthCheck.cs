using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VietnamCarPlatform.Api.Health;

public sealed class InfrastructureHealthCheck(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<InfrastructureHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var postgresHost = configuration["POSTGRES_HOST"] ?? "postgres";
            var postgresPort = configuration.GetValue("POSTGRES_PORT", 5432);
            var redisHost = configuration["REDIS_HOST"] ?? "redis";
            var redisPort = configuration.GetValue("REDIS_PORT", 6379);
            var objectStorageHealth = configuration["OBJECT_STORAGE_HEALTH_ENDPOINT"]
                ?? "http://minio:9000/minio/health/live";

            await CheckTcpAsync(postgresHost, postgresPort, cancellationToken);
            await CheckTcpAsync(redisHost, redisPort, cancellationToken);

            using var response = await httpClientFactory.CreateClient("readiness")
                .GetAsync(objectStorageHealth, cancellationToken);
            response.EnsureSuccessStatusCode();

            return HealthCheckResult.Healthy("PostgreSQL, Redis and object storage are reachable.");
        }
        catch (Exception exception) when (exception is SocketException or HttpRequestException or TaskCanceledException or TimeoutException)
        {
            InfrastructureHealthLog.ReadinessFailed(logger, exception);
            return HealthCheckResult.Unhealthy("A required infrastructure dependency is unavailable.", exception);
        }
    }

    private static async Task CheckTcpAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await client.ConnectAsync(host, port, timeout.Token);
    }
}

internal static partial class InfrastructureHealthLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Infrastructure readiness check failed.")]
    public static partial void ReadinessFailed(ILogger logger, Exception exception);
}
