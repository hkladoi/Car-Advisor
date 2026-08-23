using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Energy;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Energy;

public interface IEnergyService
{
    Task<EnergyCalculationResponse> CalculateAsync(EnergyCalculationRequest request, CancellationToken cancellationToken);
}

public sealed class EnergyService(AppDbContext database, TimeProvider timeProvider) : IEnergyService
{
    public async Task<EnergyCalculationResponse> CalculateAsync(
        EnergyCalculationRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var instant = (request.CalculationDate ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var car = await database.CurrentSearchableTrims.AsNoTracking()
            .SingleOrDefaultAsync(value => value.TrimId == request.TrimId, cancellationToken)
            ?? throw new EnergyCalculationException(StatusCodes.Status404NotFound, "TRIM_NOT_FOUND", "The requested trim is not published in the Vietnam catalog.");
        var profile = await database.EnergyProfiles.AsNoTracking()
            .SingleOrDefaultAsync(value => value.TrimId == request.TrimId, cancellationToken)
            ?? throw new EnergyCalculationException(StatusCodes.Status422UnprocessableEntity, "ENERGY_PROFILE_UNKNOWN", "No reviewed official energy profile is available for this trim.");
        if (!Enum.TryParse<PowertrainType>(car.PowertrainType, true, out var powertrain))
        {
            throw new EnergyCalculationException(StatusCodes.Status422UnprocessableEntity, "POWERTRAIN_UNKNOWN", "The trim does not have a supported sourced powertrain type.");
        }
        ValidatePhevConditions(powertrain, profile.FuelConsumptionCondition, profile.ElectricConsumptionCondition);

        var needsFuel = powertrain != PowertrainType.Bev;
        var fuelType = ResolveFuelType(request.FuelType, profile.RecommendedFuel, needsFuel);
        var fuelPrice = needsFuel
            ? await database.EnergyPrices.AsNoTracking()
                .Where(value => value.EnergyType == fuelType
                    && value.RegionCode == "VN"
                    && value.EffectiveFrom <= instant
                    && (value.EffectiveTo == null || value.EffectiveTo > instant))
                .OrderByDescending(value => value.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        if (needsFuel && fuelPrice is null)
        {
            throw new EnergyCalculationException(StatusCodes.Status422UnprocessableEntity, "FUEL_PRICE_UNKNOWN", "No effective official fuel price exists for the selected fuel product and date.");
        }

        if (!Enum.TryParse<HomeChargingMode>(request.HomeMode, true, out var homeMode))
        {
            throw new EnergyCalculationException(StatusCodes.Status400BadRequest, "HOME_MODE_INVALID", "HomeMode must be EvnMarginalTiers or CustomFixedRate.");
        }
        var needsElectricity = powertrain is PowertrainType.Bev or PowertrainType.Phev or PowertrainType.Erev;
        var needsHomeTiers = needsElectricity && request.HomeChargingShare > 0 && homeMode == HomeChargingMode.EvnMarginalTiers;
        var homePrices = needsHomeTiers
            ? await database.EnergyPrices.AsNoTracking()
                .Where(value => value.EnergyType == EnergyType.Electricity
                    && value.RegionCode == "VN"
                    && value.EffectiveFrom <= instant
                    && (value.EffectiveTo == null || value.EffectiveTo > instant))
                .OrderBy(value => value.TierFromInclusive)
                .ToListAsync(cancellationToken)
            : [];
        if (needsHomeTiers && homePrices.Count != 6)
        {
            throw new EnergyCalculationException(StatusCodes.Status422UnprocessableEntity, "HOUSEHOLD_TARIFF_INCOMPLETE", "The effective EVN household tariff is not a complete six-tier schedule.");
        }

        var needsPublic = needsElectricity && request.HomeChargingShare < 1;
        ChargingProvider? provider = null;
        ChargingTariff? tariff = null;
        List<ChargingPromotion> promotions = [];
        if (needsPublic)
        {
            provider = await database.ChargingProviders.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Slug == request.ChargingProviderSlug, cancellationToken)
                ?? throw new EnergyCalculationException(StatusCodes.Status422UnprocessableEntity, "CHARGING_PROVIDER_UNKNOWN", "The requested charging provider is not in the reviewed registry.");
            var tariffs = await database.ChargingTariffs.AsNoTracking()
                .Where(value => value.ProviderId == provider.Id
                    && (value.RegionScope == "VN")
                    && value.EffectiveFrom <= instant
                    && (value.EffectiveTo == null || value.EffectiveTo > instant))
                .ToListAsync(cancellationToken);
            tariff = tariffs
                .Where(value => value.ConnectorType is null
                    || string.Equals(value.ConnectorType, request.ConnectorType, StringComparison.OrdinalIgnoreCase))
                .Where(value => request.ChargingPowerKw is null
                    || (value.MinimumPowerKw is null || request.ChargingPowerKw >= value.MinimumPowerKw)
                    && (value.MaximumPowerKw is null || request.ChargingPowerKw <= value.MaximumPowerKw))
                .OrderByDescending(value => value.ConnectorType is not null)
                .ThenByDescending(value => value.MinimumPowerKw)
                .ThenByDescending(value => value.EffectiveFrom)
                .FirstOrDefault()
                ?? throw new EnergyCalculationException(StatusCodes.Status422UnprocessableEntity, "CHARGING_TARIFF_UNKNOWN", "No effective tariff matches the provider, connector, power, region, and date.");
            promotions = await database.ChargingPromotions.AsNoTracking()
                .Where(value => (value.ProviderId == null || value.ProviderId == provider.Id)
                    && (value.BrandId == null || value.BrandId == car.BrandId)
                    && (value.ModelId == null || value.ModelId == car.ModelId)
                    && value.EffectiveFrom <= instant
                    && (value.EffectiveTo == null || value.EffectiveTo > instant))
                .OrderByDescending(value => value.EffectiveFrom)
                .ToListAsync(cancellationToken);
        }

        EnergyCostEvaluation evaluation;
        try
        {
            evaluation = EnergyCostEvaluator.Evaluate(
                new EnergyCostContext(
                    powertrain,
                    request.MonthlyKilometres,
                    profile.OfficialFuelLitresPer100Km,
                    profile.OfficialElectricKwhPer100Km,
                    request.EvShare,
                    request.HomeChargingShare,
                    request.ChargingEfficiency,
                    homeMode,
                    request.HouseholdBaseKwh,
                    request.CustomHomeAmountPerKwh,
                    request.PublicSessions,
                    request.SessionsUsedThisMonth,
                    request.PostChargeMinutesPerSession,
                    request.ConnectorType,
                    request.CustomerType,
                    request.PurchaseDate,
                    request.PromotionEligibilityConfirmed),
                fuelPrice is null ? null : ToRate(fuelPrice),
                homePrices.Select(ToRate).ToArray(),
                tariff is null ? null : new PublicChargingRate(
                    tariff.Id,
                    tariff.ProviderId,
                    tariff.AmountPerKwh ?? 0,
                    tariff.AmountPerSession ?? 0,
                    tariff.OverstayRulesJson,
                    tariff.OverstayCapPerSession,
                    tariff.TaxIncluded),
                promotions.Select(value => new PromotionRule(
                    value.Id,
                    value.Benefit,
                    value.BenefitValue,
                    value.EligibilityJson,
                    value.CapsJson)).ToArray());
        }
        catch (ArgumentException exception)
        {
            throw new EnergyCalculationException(StatusCodes.Status400BadRequest, "ENERGY_INPUT_INVALID", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new EnergyCalculationException(StatusCodes.Status422UnprocessableEntity, "ENERGY_DATA_INCOMPLETE", exception.Message);
        }

        var factIds = homePrices.Select(value => value.SourceFactId)
            .Append(profile.SourceFactId)
            .Append(fuelPrice?.SourceFactId)
            .Append(provider?.SourceFactId)
            .Append(tariff?.SourceFactId)
            .Concat(promotions.Select(value => value.SourceFactId));
        var sources = await LoadSourcesAsync(factIds, cancellationToken);
        var rates = new Dictionary<Guid, AppliedEnergyRate>();
        if (fuelPrice is not null)
        {
            rates[fuelPrice.Id] = PriceReference(fuelPrice, sources);
        }
        foreach (var price in homePrices)
        {
            rates[price.Id] = PriceReference(price, sources);
        }
        if (tariff is not null && provider is not null)
        {
            rates[tariff.Id] = TariffReference(tariff, provider, sources);
        }

        var warnings = evaluation.Warnings.ToList();
        var usedRateIds = evaluation.Breakdown.Where(value => value.RateId is not null).Select(value => value.RateId!.Value).ToHashSet();
        var usedSourceFacts = new HashSet<Guid?> { profile.SourceFactId };
        usedSourceFacts.UnionWith(usedRateIds.Select(id => rates.GetValueOrDefault(id)?.Source?.SourceFactId));
        usedSourceFacts.UnionWith(evaluation.AppliedPromotionIds.Select(id => promotions.Single(value => value.Id == id).SourceFactId));
        foreach (var source in usedSourceFacts.Where(value => value is not null).Select(value => sources[value!.Value]))
        {
            if (source.IsStale)
            {
                warnings.Add($"STALE_SOURCE: {source.Name} was not refreshed before {source.FreshUntil:O}; the last reviewed effective value remains in use.");
            }
        }
        if (profile.SourceFactId is null)
        {
            warnings.Add("ENERGY_PROFILE_MANUAL_OVERRIDE: the vehicle energy profile has no source fact.");
        }
        if (request.PostChargeMinutesPerSession > 0 && string.Equals(request.ConnectorType, "AC11", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("OVERSTAY_EXEMPT_CONNECTOR: V-Green states that the post-charge service fee does not apply to AC 11 kW car chargers; no post-charge fee was charged.");
        }

        var appliedPromotionIds = evaluation.AppliedPromotionIds.ToHashSet();
        return new EnergyCalculationResponse(
            new EnergyCalculationResult(
                evaluation.CurrentCost,
                evaluation.NormalizedCost,
                evaluation.PromotionSavings,
                evaluation.FuelLitres,
                evaluation.BatteryEnergyKwh,
                evaluation.GridEnergyKwh,
                "VND"),
            new EnergyVehicleIdentity(
                car.TrimId,
                car.BrandId,
                car.ModelId,
                car.BrandName,
                car.ModelName,
                car.TrimName,
                car.ModelYear,
                car.PowertrainType),
            new EnergyProfileReference(
                profile.Id,
                profile.OfficialFuelLitresPer100Km,
                profile.OfficialElectricKwhPer100Km,
                profile.FuelConsumptionCondition,
                profile.ElectricConsumptionCondition,
                profile.TestCycle,
                profile.ConsumptionNotes,
                Source(profile.SourceFactId, sources)),
            instant,
            evaluation.Breakdown.Select(value => new EnergyBreakdownItem(
                value.Component,
                value.Quantity,
                value.Unit,
                value.NormalizedAmount,
                value.CurrentAmount,
                value.Detail,
                value.RateId is not null ? rates.GetValueOrDefault(value.RateId.Value) : null)).ToArray(),
            evaluation.Assumptions.Concat(
            [
                $"Fuel test condition: {profile.FuelConsumptionCondition ?? "not applicable"}.",
                $"Electric test condition: {profile.ElectricConsumptionCondition ?? "not applicable"}.",
                $"Home tariff mode: {homeMode}.",
            ]).ToArray(),
            rates.Values.Where(value => usedRateIds.Contains(value.RateId)).ToArray(),
            promotions.Where(value => appliedPromotionIds.Contains(value.Id)).Select(value => new AppliedChargingPromotion(
                value.Id,
                value.Benefit.ToString(),
                value.BenefitValue,
                value.EffectiveFrom,
                value.EffectiveTo,
                Source(value.SourceFactId, sources))).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            timeProvider.GetUtcNow());
    }

    private async Task<Dictionary<Guid, EnergySourceReference>> LoadSourcesAsync(
        IEnumerable<Guid?> factIds,
        CancellationToken cancellationToken)
    {
        var ids = factIds.Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }
        var now = timeProvider.GetUtcNow();
        return await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where ids.Contains(fact.Id)
                let lastFetched = source.LastFetchedAt ?? snapshot.FetchedAt
                let freshUntil = lastFetched + source.RefreshInterval
                select new EnergySourceReference(
                    fact.Id,
                    source.Id,
                    source.Name,
                    source.Url,
                    source.AuthorityLevel.ToString(),
                    source.ContentType.ToString(),
                    snapshot.FetchedAt,
                    snapshot.ContentHash,
                    fact.Status.ToString(),
                    fact.Confidence.ToString(),
                    freshUntil,
                    now > freshUntil))
            .ToDictionaryAsync(value => value.SourceFactId, cancellationToken);
    }

    private static EnergyRate ToRate(EnergyPrice value) => new(
        value.Id,
        value.EnergyType,
        value.Provider,
        value.Amount,
        value.TaxRate,
        value.TaxIncluded,
        value.TierFromInclusive,
        value.TierToInclusive);

    private static AppliedEnergyRate PriceReference(
        EnergyPrice value,
        IReadOnlyDictionary<Guid, EnergySourceReference> sources) => new(
        value.Id,
        value.EnergyType.ToString(),
        value.Provider,
        value.Amount,
        value.Unit,
        value.Currency,
        value.TaxRate,
        value.TaxIncluded,
        value.EffectiveFrom,
        value.EffectiveTo,
        Source(value.SourceFactId, sources));

    private static AppliedEnergyRate TariffReference(
        ChargingTariff value,
        ChargingProvider provider,
        IReadOnlyDictionary<Guid, EnergySourceReference> sources) => new(
        value.Id,
        "PublicCharging",
        provider.Name,
        value.AmountPerKwh,
        "VND/kWh",
        value.Currency,
        null,
        value.TaxIncluded,
        value.EffectiveFrom,
        value.EffectiveTo,
        Source(value.SourceFactId, sources));

    private static EnergySourceReference? Source(Guid? id, IReadOnlyDictionary<Guid, EnergySourceReference> sources) =>
        id is not null && sources.TryGetValue(id.Value, out var source) ? source : null;

    private static EnergyType ResolveFuelType(string? requested, string? recommended, bool required)
    {
        if (!required)
        {
            return EnergyType.E10Ron95III;
        }
        var value = string.IsNullOrWhiteSpace(requested) ? recommended : requested;
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse<EnergyType>(value, true, out var result)
            || result == EnergyType.Electricity)
        {
            throw new EnergyCalculationException(StatusCodes.Status400BadRequest, "FUEL_TYPE_REQUIRED", "FuelType must select Ron92E5, E10Ron95III, or Diesel when the vehicle has a fuel distance share.");
        }
        return result;
    }

    private static void ValidatePhevConditions(
        PowertrainType powertrain,
        string? fuelCondition,
        string? electricCondition)
    {
        if (powertrain is not (PowertrainType.Phev or PowertrainType.Erev))
        {
            return;
        }
        if (fuelCondition is null || !fuelCondition.Contains("charge-sustaining", StringComparison.OrdinalIgnoreCase)
            || electricCondition is null || !electricCondition.Contains("charge-depleting", StringComparison.OrdinalIgnoreCase))
        {
            throw new EnergyCalculationException(
                StatusCodes.Status422UnprocessableEntity,
                "PHEV_TEST_CONDITION_INCOMPATIBLE",
                "PHEV split calculation requires separately labelled charge-sustaining fuel and charge-depleting electric consumption facts; a weighted combined figure cannot be substituted.");
        }
    }

    private static void Validate(EnergyCalculationRequest request)
    {
        if (request.TrimId == Guid.Empty)
        {
            throw new EnergyCalculationException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "TrimId is required.");
        }
        if (request.MonthlyKilometres is < 0 or > 100_000
            || request.HouseholdBaseKwh is < 0 or > 100_000
            || request.CustomHomeAmountPerKwh is < 0 or > 1_000_000)
        {
            throw new EnergyCalculationException(StatusCodes.Status400BadRequest, "ENERGY_INPUT_OUT_OF_RANGE", "Distance, household consumption, and custom rate must be non-negative and within supported bounds.");
        }
        if (request.EvShare is < 0 or > 1 || request.HomeChargingShare is < 0 or > 1
            || request.ChargingEfficiency is <= 0 or > 1)
        {
            throw new EnergyCalculationException(StatusCodes.Status400BadRequest, "ENERGY_SHARE_INVALID", "EV share, home share, and charging efficiency must be fractions between zero and one; efficiency must be greater than zero.");
        }
        if (request.PublicSessions is < 0 or > 1_000
            || request.SessionsUsedThisMonth is < 0 or > 1_000
            || request.PostChargeMinutesPerSession is < 0 or > 10_000)
        {
            throw new EnergyCalculationException(StatusCodes.Status400BadRequest, "SESSION_INPUT_INVALID", "Session counts and post-charge minutes must be non-negative and within supported bounds.");
        }
        if (request.CalculationDate is { } date && (date.Year < 2020 || date.Year > 2100))
        {
            throw new EnergyCalculationException(StatusCodes.Status400BadRequest, "INVALID_DATE", "CalculationDate must be between 2020 and 2100.");
        }
    }
}
