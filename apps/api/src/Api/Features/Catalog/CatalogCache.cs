using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace VietnamCarPlatform.Api.Features.Catalog;

public sealed partial class CatalogCache(IDistributedCache cache, ILogger<CatalogCache> logger)
{
    private const string GenerationKey = "catalog:v1:generation";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    public static string RequestKey(CatalogFilter filter)
    {
        var canonical = JsonSerializer.Serialize(filter, JsonOptions);
        return $"catalog:v1:cars:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            var versionedKey = await VersionedKeyAsync(key, cancellationToken);
            var json = await cache.GetStringAsync(versionedKey, cancellationToken);
            return json is null ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception exception)
        {
            Log.CacheReadFailed(logger, key, exception);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        try
        {
            var versionedKey = await VersionedKeyAsync(key, cancellationToken);
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await cache.SetStringAsync(versionedKey, json, CacheOptions, cancellationToken);
        }
        catch (Exception exception)
        {
            Log.CacheWriteFailed(logger, key, exception);
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(
                GenerationKey,
                Guid.NewGuid().ToString("N"),
                new DistributedCacheEntryOptions(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            Log.CacheInvalidationFailed(logger, exception);
        }
    }

    private async Task<string> VersionedKeyAsync(string key, CancellationToken cancellationToken)
    {
        var generation = await cache.GetStringAsync(GenerationKey, cancellationToken) ?? "initial";
        return $"{generation}:{key}";
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Catalog cache read failed for {CacheKey}; continuing with PostgreSQL")]
        public static partial void CacheReadFailed(ILogger logger, string cacheKey, Exception exception);

        [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "Catalog cache write failed for {CacheKey}; response remains valid")]
        public static partial void CacheWriteFailed(ILogger logger, string cacheKey, Exception exception);

        [LoggerMessage(EventId = 1303, Level = LogLevel.Warning, Message = "Catalog cache invalidation failed; entries retain their five-minute absolute expiry")]
        public static partial void CacheInvalidationFailed(ILogger logger, Exception exception);
    }
}
