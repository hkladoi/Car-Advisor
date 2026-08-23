namespace VietnamCarPlatform.Api.Features.Charging;

public sealed class ChargingStationQuery
{
    public decimal? MinLatitude { get; init; }
    public decimal? MinLongitude { get; init; }
    public decimal? MaxLatitude { get; init; }
    public decimal? MaxLongitude { get; init; }
    public int? Limit { get; init; }
    public bool? Operational { get; init; }
    public string? ConnectorType { get; init; }
    public decimal? MinimumPowerKw { get; init; }
}

public sealed record ChargingConnectorReference(
    string? ConnectorType,
    string? ChargingLevel,
    string? CurrentType,
    string? OperationalStatus,
    decimal? PowerKw,
    int? Quantity);

public sealed record AuthoritativeChargingTariffReference(
    Guid ProviderId,
    string ProviderName,
    string ProviderOfficialUrl,
    decimal? AmountPerKwh,
    decimal? AmountPerSession,
    decimal? OverstayAmountPerMinute,
    string Currency,
    bool TaxIncluded,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string SourceUrl);

public sealed record ChargingLocationSourceReference(
    string Name,
    string Url,
    DateTimeOffset FetchedAt,
    string ContentHash,
    string Attribution,
    string LicenseUrl);

public sealed record ChargingStationReference(
    Guid Id,
    int OpenChargeMapId,
    string Name,
    string? AddressLine1,
    string? AddressLine2,
    string? Town,
    string? StateOrProvince,
    string? Postcode,
    decimal Latitude,
    decimal Longitude,
    string? OperatorName,
    string? UsageType,
    string? OperationalStatus,
    bool? IsOperational,
    int? NumberOfPoints,
    string Coverage,
    string Confidence,
    string ConfidenceBasis,
    DateTimeOffset? ExternalUpdatedAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<ChargingConnectorReference> Connectors,
    AuthoritativeChargingTariffReference? Tariff,
    string TariffAuthority,
    ChargingLocationSourceReference Source);

public sealed record ChargingDatasetReference(
    string Provider,
    string Coverage,
    string GeographicCompleteness,
    string Attribution,
    string LicenseUrl,
    DateTimeOffset? LastSyncedAt,
    bool IsStale,
    string TariffPolicy);

public sealed record ChargingStationListResponse(
    IReadOnlyList<ChargingStationReference> Data,
    int Count,
    ChargingDatasetReference Dataset,
    DateTimeOffset GeneratedAt);

public sealed record GeocodeResult(
    string FormattedAddress,
    decimal Latitude,
    decimal Longitude,
    string? PlaceId);

public sealed record GeocodeResponse(
    IReadOnlyList<GeocodeResult> Results,
    string Provider,
    bool Cached,
    DateTimeOffset GeneratedAt);

public sealed record MapCapabilitiesResponse(
    bool CachedChargingLocationsEnabled,
    bool GoongGeocodingEnabled,
    bool GoongMapTilesConfigured,
    bool MapTilesKeyExposed,
    string MapMode,
    string DegradedMode);

public sealed class ChargingIntegrationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
