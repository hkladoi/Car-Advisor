using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Charging;

public interface IChargingService
{
    Task<ChargingStationListResponse> SearchAsync(ChargingStationQuery request, CancellationToken cancellationToken);
}

public sealed class ChargingService(AppDbContext database, TimeProvider timeProvider) : IChargingService
{
    private static readonly TimeSpan FreshnessPolicy = TimeSpan.FromDays(7);

    public async Task<ChargingStationListResponse> SearchAsync(
        ChargingStationQuery request,
        CancellationToken cancellationToken)
    {
        var bounds = Validate(request);
        var limit = request.Limit ?? 200;
        var query = database.ChargingStations.AsNoTracking()
            .Where(value => value.Active
                && value.CountryCode == "VN"
                && value.Latitude >= bounds.MinLatitude
                && value.Latitude <= bounds.MaxLatitude
                && value.Longitude >= bounds.MinLongitude
                && value.Longitude <= bounds.MaxLongitude);
        if (request.Operational is not null)
        {
            query = query.Where(value => value.IsOperational == request.Operational);
        }
        if (!string.IsNullOrWhiteSpace(request.ConnectorType) || request.MinimumPowerKw is not null)
        {
            var connectorType = request.ConnectorType?.Trim();
            query = query.Where(station => database.ChargingStationConnectors.Any(connector =>
                connector.ChargingStationId == station.Id
                && (connectorType == null || (connector.ConnectorType != null
                    && EF.Functions.ILike(connector.ConnectorType, $"%{connectorType}%")))
                && (request.MinimumPowerKw == null || connector.PowerKw >= request.MinimumPowerKw)));
        }

        var stations = await query
            .OrderByDescending(value => value.IsOperational == true)
            .ThenByDescending(value => value.Confidence)
            .ThenBy(value => value.Name)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        var stationIds = stations.Select(value => value.Id).ToArray();
        var connectors = await database.ChargingStationConnectors.AsNoTracking()
            .Where(value => stationIds.Contains(value.ChargingStationId))
            .OrderByDescending(value => value.PowerKw)
            .ToArrayAsync(cancellationToken);
        var connectorsByStation = connectors.ToLookup(value => value.ChargingStationId);
        var snapshotIds = stations.Select(value => value.SourceSnapshotId).Distinct().ToArray();
        var sources = await (
                from snapshot in database.SourceSnapshots.AsNoTracking()
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where snapshotIds.Contains(snapshot.Id)
                select new
                {
                    SnapshotId = snapshot.Id,
                    source.Name,
                    source.Url,
                    snapshot.FetchedAt,
                    snapshot.ContentHash,
                })
            .ToDictionaryAsync(value => value.SnapshotId, cancellationToken);

        var mappedProviderIds = stations
            .Where(value => value.ChargingProviderId is not null && value.ProviderMappingReviewedAt is not null)
            .Select(value => value.ChargingProviderId!.Value)
            .Distinct()
            .ToArray();
        var now = timeProvider.GetUtcNow();
        var tariffRows = await (
                from tariff in database.ChargingTariffs.AsNoTracking()
                join provider in database.ChargingProviders.AsNoTracking() on tariff.ProviderId equals provider.Id
                join fact in database.SourceFacts.AsNoTracking() on tariff.SourceFactId equals fact.Id
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where mappedProviderIds.Contains(tariff.ProviderId)
                    && tariff.EffectiveFrom <= now
                    && (tariff.EffectiveTo == null || tariff.EffectiveTo > now)
                orderby tariff.EffectiveFrom descending
                select new
                {
                    Tariff = tariff,
                    Provider = provider,
                    SourceUrl = source.Url,
                })
            .ToArrayAsync(cancellationToken);
        var tariffByProvider = tariffRows
            .GroupBy(value => value.Provider.Id)
            .ToDictionary(value => value.Key, value => value.First());

        var data = stations.Select(station =>
        {
            if (!sources.TryGetValue(station.SourceSnapshotId, out var source))
            {
                throw new InvalidOperationException($"Charging station {station.Id} has no source snapshot.");
            }
            var tariff = station.ChargingProviderId is { } providerId
                && station.ProviderMappingReviewedAt is not null
                && tariffByProvider.TryGetValue(providerId, out var match)
                    ? match
                    : null;
            return new ChargingStationReference(
                station.Id,
                station.ExternalId,
                station.Name,
                station.AddressLine1,
                station.AddressLine2,
                station.Town,
                station.StateOrProvince,
                station.Postcode,
                station.Latitude,
                station.Longitude,
                station.OperatorName,
                station.UsageType,
                station.OperationalStatus,
                station.IsOperational,
                station.NumberOfPoints,
                station.Coverage.ToString(),
                station.Confidence.ToString(),
                ConfidenceBasis(station.ExternalDataQualityLevel),
                station.ExternalUpdatedAt,
                station.LastSeenAt,
                connectorsByStation[station.Id].Select(connector => new ChargingConnectorReference(
                    connector.ConnectorType,
                    connector.ChargingLevel,
                    connector.CurrentType,
                    connector.OperationalStatus,
                    connector.PowerKw,
                    connector.Quantity)).ToArray(),
                tariff is null ? null : new AuthoritativeChargingTariffReference(
                    tariff.Provider.Id,
                    tariff.Provider.Name,
                    tariff.Provider.OfficialUrl,
                    tariff.Tariff.AmountPerKwh,
                    tariff.Tariff.AmountPerSession,
                    tariff.Tariff.OverstayAmountPerMinute,
                    tariff.Tariff.Currency,
                    tariff.Tariff.TaxIncluded,
                    tariff.Tariff.EffectiveFrom,
                    tariff.Tariff.EffectiveTo,
                    tariff.SourceUrl),
                tariff is null ? "UnavailableUntilReviewedProviderMapping" : "ProviderOfficialSource",
                new ChargingLocationSourceReference(
                    source.Name,
                    source.Url,
                    source.FetchedAt,
                    source.ContentHash,
                    "Open Charge Map contributors and data providers",
                    "https://creativecommons.org/licenses/by/4.0/"));
        }).ToArray();

        var lastSyncedAt = stations.Length == 0
            ? await database.ChargingStations.AsNoTracking()
                .Where(value => value.ExternalSource == "OpenChargeMap")
                .MaxAsync(value => (DateTimeOffset?)value.ImportedAt, cancellationToken)
            : stations.Max(value => value.ImportedAt);
        return new ChargingStationListResponse(
            data,
            data.Length,
            new ChargingDatasetReference(
                "OpenChargeMap",
                "ReferenceOnly",
                "Community coverage is not guaranteed complete for Vietnam.",
                "Open Charge Map contributors and data providers",
                "https://creativecommons.org/licenses/by/4.0/",
                lastSyncedAt,
                lastSyncedAt is null || now - lastSyncedAt > FreshnessPolicy,
                "Tariffs are returned only from an effective provider source after a reviewed provider mapping; OCM usage-cost text is ignored."),
            now);
    }

    public static string ConfidenceBasis(int? level) => level switch
    {
        null => "Open Charge Map did not provide a data-quality level; not provider verified.",
        <= 2 => $"Open Charge Map community data quality {level}/5; low reference confidence and not provider verified.",
        3 => "Open Charge Map community data quality 3/5; medium reference confidence and not provider verified.",
        _ => $"Open Charge Map community data quality {level}/5; high location-detail confidence but not provider verified.",
    };

    private static Bounds Validate(ChargingStationQuery request)
    {
        if (request.Limit is < 1 or > 500)
        {
            throw new ChargingIntegrationException(StatusCodes.Status400BadRequest, "CHARGING_LIMIT_INVALID", "Limit must be between 1 and 500.");
        }
        if (request.MinimumPowerKw is < 0 or > 1000)
        {
            throw new ChargingIntegrationException(StatusCodes.Status400BadRequest, "CHARGING_POWER_INVALID", "MinimumPowerKw must be between 0 and 1000.");
        }
        var supplied = new[] { request.MinLatitude, request.MinLongitude, request.MaxLatitude, request.MaxLongitude };
        if (supplied.Any(value => value is not null) && supplied.Any(value => value is null))
        {
            throw new ChargingIntegrationException(StatusCodes.Status400BadRequest, "CHARGING_BBOX_INCOMPLETE", "All four bounding-box coordinates are required together.");
        }
        var bounds = supplied[0] is null
            ? new Bounds(7.5m, 101.5m, 24m, 110.5m)
            : new Bounds(supplied[0]!.Value, supplied[1]!.Value, supplied[2]!.Value, supplied[3]!.Value);
        if (bounds.MinLatitude < 7.5m || bounds.MaxLatitude > 24m
            || bounds.MinLongitude < 101.5m || bounds.MaxLongitude > 110.5m
            || bounds.MinLatitude >= bounds.MaxLatitude
            || bounds.MinLongitude >= bounds.MaxLongitude)
        {
            throw new ChargingIntegrationException(StatusCodes.Status400BadRequest, "CHARGING_BBOX_INVALID", "Bounding box must be ordered and remain within the supported Vietnam extent.");
        }
        return bounds;
    }

    private sealed record Bounds(decimal MinLatitude, decimal MinLongitude, decimal MaxLatitude, decimal MaxLongitude);
}
