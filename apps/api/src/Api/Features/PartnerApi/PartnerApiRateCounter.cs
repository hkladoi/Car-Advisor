using StackExchange.Redis;

namespace VietnamCarPlatform.Api.Features.PartnerApi;

public sealed record PartnerRateLimitDecision(
    bool Allowed,
    int MinuteLimit,
    int MinuteRemaining,
    long MonthLimit,
    long MonthRemaining,
    long RetryAfterSeconds,
    DateTimeOffset MinuteResetsAt,
    DateTimeOffset MonthResetsAt);

public interface IPartnerApiRateCounter
{
    Task<PartnerRateLimitDecision> AcquireAsync(
        PartnerApiAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class RedisPartnerApiRateCounter(
    IConnectionMultiplexer connection) : IPartnerApiRateCounter
{
    private const string AcquireScript =
        "local minute=tonumber(redis.call('GET',KEYS[1]) or '0'); "
        + "local month=tonumber(redis.call('GET',KEYS[2]) or '0'); "
        + "if minute>=tonumber(ARGV[1]) or month>=tonumber(ARGV[2]) then "
        + "return {0,minute,month}; end; "
        + "minute=redis.call('INCR',KEYS[1]); "
        + "if minute==1 then redis.call('PEXPIRE',KEYS[1],ARGV[3]); end; "
        + "month=redis.call('INCR',KEYS[2]); "
        + "if month==1 then redis.call('PEXPIRE',KEYS[2],ARGV[4]); end; "
        + "return {1,minute,month};";

    public async Task<PartnerRateLimitDecision> AcquireAsync(
        PartnerApiAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var minuteStart = new DateTimeOffset(
            now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var minuteReset = minuteStart.AddMinutes(1);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthReset = monthStart.AddMonths(1);
        var minuteTtl = Math.Max(1_000L, (long)Math.Ceiling((minuteReset - now).TotalMilliseconds) + 1_000L);
        var monthTtl = Math.Max(1_000L, (long)Math.Ceiling((monthReset - now).TotalMilliseconds) + 1_000L);
        var keyStem = $"partner-api:usage:{access.KeyId:N}";
        var minuteKey = new RedisKey($"{keyStem}:minute:{minuteStart:yyyyMMddHHmm}");
        var monthKey = new RedisKey($"{keyStem}:month:{monthStart:yyyyMM}");

        var raw = (RedisResult[]?)await connection.GetDatabase().ScriptEvaluateAsync(
            AcquireScript,
            [minuteKey, monthKey],
            [access.RequestsPerMinute, access.RequestsPerMonth, minuteTtl, monthTtl]);
        if (raw is null || raw.Length != 3)
        {
            throw new RedisServerException("Partner API rate counter returned an invalid response.");
        }

        var allowed = (long)raw[0] == 1;
        var minuteCount = (long)raw[1];
        var monthCount = (long)raw[2];
        var minuteRemaining = (int)Math.Max(0, access.RequestsPerMinute - minuteCount);
        var monthRemaining = Math.Max(0, access.RequestsPerMonth - monthCount);
        var retryAfter = minuteCount >= access.RequestsPerMinute
            ? (long)Math.Max(1, Math.Ceiling((minuteReset - now).TotalSeconds))
            : (long)Math.Max(1, Math.Ceiling((monthReset - now).TotalSeconds));
        return new PartnerRateLimitDecision(
            allowed,
            access.RequestsPerMinute,
            minuteRemaining,
            access.RequestsPerMonth,
            monthRemaining,
            allowed ? 0 : retryAfter,
            minuteReset,
            monthReset);
    }
}
