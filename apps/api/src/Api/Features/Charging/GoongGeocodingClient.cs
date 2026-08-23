using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;

namespace VietnamCarPlatform.Api.Features.Charging;

public sealed class GoongOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string MapTilesKey { get; set; } = string.Empty;
    public int CacheSeconds { get; set; } = 86_400;
}

public interface IGoongGeocodingClient
{
    bool Enabled { get; }
    bool MapTilesConfigured { get; }
    Task<GeocodeResponse> ForwardAsync(string address, CancellationToken cancellationToken);
}

public sealed class GoongGeocodingClient(
    HttpClient http,
    GoongOptions options,
    IDistributedCache cache,
    TimeProvider timeProvider) : IGoongGeocodingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool Enabled => !string.IsNullOrWhiteSpace(options.ApiKey);
    public bool MapTilesConfigured => !string.IsNullOrWhiteSpace(options.MapTilesKey);

    public async Task<GeocodeResponse> ForwardAsync(string address, CancellationToken cancellationToken)
    {
        var normalized = ValidateAddress(address);
        if (!Enabled)
        {
            throw new ChargingIntegrationException(
                StatusCodes.Status503ServiceUnavailable,
                "GOONG_NOT_CONFIGURED",
                "Optional Goong geocoding is not configured; cached charging locations remain available.");
        }
        var cacheKey = $"map:goong:geocode:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()}";
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            var cachedResults = JsonSerializer.Deserialize<GeocodeResult[]>(cached, JsonOptions) ?? [];
            return new GeocodeResponse(cachedResults, "Goong", true, timeProvider.GetUtcNow());
        }

        HttpResponseMessage response;
        try
        {
            var path = $"Geocode?address={Uri.EscapeDataString(normalized)}&api_key={Uri.EscapeDataString(options.ApiKey)}";
            response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderUnavailable("Goong geocoding timed out; cached charging locations remain available.");
        }
        catch (HttpRequestException)
        {
            throw ProviderUnavailable("Goong geocoding is unavailable; cached charging locations remain available.");
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? "Goong geocoding rate limit was reached; retry later."
                    : "Goong geocoding rejected or could not complete the request.";
                throw ProviderUnavailable(message);
            }
            GoongResponse? payload;
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                payload = await JsonSerializer.DeserializeAsync<GoongResponse>(body, JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                throw ProviderUnavailable("Goong geocoding returned an invalid response.");
            }
            if (payload is null || !string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                throw ProviderUnavailable("Goong geocoding returned no usable response.");
            }
            var results = payload.Results
                .Where(value => value.Geometry?.Location is not null
                    && value.Geometry.Location.Lat is >= -90 and <= 90
                    && value.Geometry.Location.Lng is >= -180 and <= 180
                    && !string.IsNullOrWhiteSpace(value.FormattedAddress))
                .Take(5)
                .Select(value => new GeocodeResult(
                    value.FormattedAddress!,
                    decimal.Parse(value.Geometry!.Location!.Lat.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                    decimal.Parse(value.Geometry.Location.Lng.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                    value.PlaceId))
                .ToArray();
            await cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(results, JsonOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.CacheSeconds),
                },
                cancellationToken);
            return new GeocodeResponse(results, "Goong", false, timeProvider.GetUtcNow());
        }
    }

    public static string ValidateAddress(string address)
    {
        var normalized = string.Join(" ", address?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? []);
        if (normalized.Length is < 3 or > 200 || normalized.Any(char.IsControl))
        {
            throw new ChargingIntegrationException(StatusCodes.Status400BadRequest, "GEOCODE_ADDRESS_INVALID", "Address must contain 3 to 200 printable characters.");
        }
        return normalized;
    }

    private static ChargingIntegrationException ProviderUnavailable(string message) => new(
        StatusCodes.Status503ServiceUnavailable,
        "GOONG_UNAVAILABLE",
        message);

    private sealed class GoongResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("results")]
        public GoongResult[] Results { get; init; } = [];
    }

    private sealed class GoongResult
    {
        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; init; }

        [JsonPropertyName("place_id")]
        public string? PlaceId { get; init; }

        [JsonPropertyName("geometry")]
        public GoongGeometry? Geometry { get; init; }
    }

    private sealed class GoongGeometry
    {
        [JsonPropertyName("location")]
        public GoongLocation? Location { get; init; }
    }

    private sealed class GoongLocation
    {
        [JsonPropertyName("lat")]
        public double Lat { get; init; }

        [JsonPropertyName("lng")]
        public double Lng { get; init; }
    }
}
