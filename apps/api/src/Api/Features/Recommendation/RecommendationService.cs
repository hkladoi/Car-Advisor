using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Energy;
using VietnamCarPlatform.Domain.Recommendation;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Infrastructure.Catalog;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Recommendation;

public interface IRecommendationService
{
    Task<RecommendationResponse> EvaluateAsync(RecommendationRequest request, CancellationToken cancellationToken);
}

public sealed class RecommendationService(AppDbContext database, TimeProvider timeProvider) : IRecommendationService
{
    private const int MinimumObservedFeaturesPerComponent = 3;
    private static readonly IReadOnlySet<string> ComfortCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "VENTILATED_FRONT", "HEATED_REAR", "SEAT_MEMORY", "PANORAMIC_ROOF", "REMOTE_CLIMATE",
    };
    private static readonly IReadOnlySet<string> TechnologyCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "APP_CONTROL", "HUD", "CAMERA_360", "REMOTE_START", "REMOTE_CLIMATE",
    };

    public async Task<RecommendationResponse> EvaluateAsync(
        RecommendationRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var instant = (request.AsOfDate ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var cars = await database.CurrentSearchableTrims.AsNoTracking()
            .OrderBy(value => value.BrandName)
            .ThenBy(value => value.ModelName)
            .ThenBy(value => value.TrimName)
            .ToListAsync(cancellationToken);
        var trimIds = cars.Select(value => value.TrimId).ToArray();

        var prices = await database.Prices.AsNoTracking()
            .Where(value => trimIds.Contains(value.TrimId)
                && value.Amount != null
                && value.Status == PriceStatus.Official
                && value.EffectiveFrom <= instant
                && (value.EffectiveTo == null || value.EffectiveTo > instant))
            .ToListAsync(cancellationToken);
        var currentPrices = prices.GroupBy(value => value.TrimId).ToDictionary(
            group => group.Key,
            group => group.OrderBy(value => PricePriority(value.PriceType))
                .ThenByDescending(value => value.RegionScope == request.RegionCode)
                .ThenBy(value => value.Priority)
                .ThenByDescending(value => value.Version)
                .First());
        var specRows = await (
                from value in database.TrimSpecs.AsNoTracking()
                join definition in database.SpecDefinitions.AsNoTracking() on value.SpecDefinitionId equals definition.Id
                where trimIds.Contains(value.TrimId)
                select new SpecRow(value, definition))
            .ToListAsync(cancellationToken);
        var featureRows = await (
                from value in database.TrimFeatures.AsNoTracking()
                join definition in database.FeatureDefinitions.AsNoTracking() on value.FeatureDefinitionId equals definition.Id
                where trimIds.Contains(value.TrimId)
                select new FeatureRow(value, definition))
            .ToListAsync(cancellationToken);
        var powertrains = await database.PowertrainProfiles.AsNoTracking()
            .Where(value => trimIds.Contains(value.TrimId))
            .ToDictionaryAsync(value => value.TrimId, cancellationToken);
        var energyProfiles = await database.EnergyProfiles.AsNoTracking()
            .Where(value => trimIds.Contains(value.TrimId))
            .ToDictionaryAsync(value => value.TrimId, cancellationToken);
        var energyPrices = await database.EnergyPrices.AsNoTracking()
            .Where(value => value.RegionCode == "VN"
                && value.EffectiveFrom <= instant
                && (value.EffectiveTo == null || value.EffectiveTo > instant))
            .OrderBy(value => value.TierFromInclusive)
            .ToListAsync(cancellationToken);

        var factIds = prices.Select(value => value.SourceFactId)
            .Concat(specRows.Select(value => value.Value.SourceFactId))
            .Concat(featureRows.Select(value => value.Value.SourceFactId))
            .Concat(powertrains.Values.Select(value => value.SourceFactId))
            .Concat(energyProfiles.Values.Select(value => value.SourceFactId))
            .Concat(energyPrices.Select(value => value.SourceFactId))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        var sources = await LoadSourcesAsync(factIds, instant, cancellationToken);

        var inputs = cars.Select(car => Candidate(
            car,
            request.HardFilters,
            currentPrices.GetValueOrDefault(car.TrimId),
            specRows.Where(value => value.Value.TrimId == car.TrimId).ToArray(),
            featureRows.Where(value => value.Value.TrimId == car.TrimId).ToArray(),
            powertrains.GetValueOrDefault(car.TrimId),
            energyProfiles.GetValueOrDefault(car.TrimId),
            energyPrices,
            sources)).ToArray();
        var weights = WeightMap(request.Weights);
        RecommendationEvaluation evaluation;
        try
        {
            evaluation = RecommendationEvaluator.Evaluate(inputs, weights);
        }
        catch (ArgumentException exception)
        {
            throw new RecommendationException(StatusCodes.Status400BadRequest, "RECOMMENDATION_WEIGHTS_INVALID", exception.Message);
        }

        var byId = cars.ToDictionary(value => value.TrimId);
        var response = new RecommendationResponse(
            new RecommendationMethodology(
                "v3.1-deterministic-1",
                ["hard_filters", "component_completeness", "source_trust", "peer_normalization", "weighted_ranking", "explanation"],
                RecommendationEvaluator.PublicCompletenessThreshold,
                RecommendationEvaluator.NormalizeWeights(weights),
                "overall = Σ(component_score × normalized_weight) / Σ(applied_weight); unavailable components never become zero",
                "price_performance = 0.40 × value_score + 0.60 × performance_score; emitted only after the public completeness and source-trust gates pass",
                [
                    "Scores are deterministic and use only published PostgreSQL data; no LLM contributes to ranking.",
                    "Component metrics are min-max normalized only against the same hard-filtered, gate-passing peer set; a tied metric scores 50.",
                    "Running cost uses 100 km, official consumption, current reviewed energy rates, 90% home-charging efficiency and a 250 kWh household base; PHEV/EREV require a user energy-share scenario and are withheld here.",
                    $"Safety, comfort and technology require at least {MinimumObservedFeaturesPerComponent} explicitly reviewed canonical observations; an absent row remains UNKNOWN, never false.",
                ]),
            cars.Count,
            inputs.Count(value => value.HardFilterMatched),
            evaluation.Ranked.Take(request.MaximumResults).Select(value => Contract(value, byId[value.TrimId], currentPrices, sources)).ToArray(),
            evaluation.DataWithheld.Select(value => Contract(value, byId[value.TrimId], currentPrices, sources)).ToArray(),
            evaluation.HardFilterExcluded.Select(value => Contract(value, byId[value.TrimId], currentPrices, sources)).ToArray(),
            timeProvider.GetUtcNow());
        return response;
    }

    private static RecommendationCandidateInput Candidate(
        CurrentSearchableTrim car,
        RecommendationHardFiltersRequest filters,
        Price? price,
        IReadOnlyList<SpecRow> specs,
        IReadOnlyList<FeatureRow> features,
        PowertrainProfile? powertrain,
        EnergyProfile? energyProfile,
        IReadOnlyList<EnergyPrice> energyPrices,
        IReadOnlyDictionary<Guid, SourceInfo> sources)
    {
        var hardReasons = HardFilterReasons(car, filters, price, features);
        return new RecommendationCandidateInput(
            car.TrimId,
            hardReasons.Count == 0,
            hardReasons,
            [
                ValueComponent(price, sources),
                RunningCostComponent(car, energyProfile, energyPrices, sources),
                SpaceComponent(specs, sources),
                FeatureComponent(RecommendationComponentCodes.SafetyAdas, "An toàn / ADAS", features, CanonicalFeatureCodes.Adas, sources),
                FeatureComponent(RecommendationComponentCodes.Comfort, "Tiện nghi", features, ComfortCodes, sources),
                PerformanceComponent(powertrain, sources),
                FeatureComponent(RecommendationComponentCodes.Technology, "Công nghệ", features, TechnologyCodes, sources),
            ]);
    }

    private static List<string> HardFilterReasons(
        CurrentSearchableTrim car,
        RecommendationHardFiltersRequest filters,
        Price? price,
        IReadOnlyList<FeatureRow> features)
    {
        var reasons = new List<string>();
        if (filters.MaximumPrice is { } ceiling)
        {
            if (price?.Amount is null) reasons.Add("HARD_FILTER_PRICE_UNKNOWN");
            else if (price.Amount > ceiling) reasons.Add("HARD_FILTER_PRICE_ABOVE_MAXIMUM");
        }
        if (filters.BodyTypes.Count > 0 && !filters.BodyTypes.Contains(car.BodyType, StringComparer.OrdinalIgnoreCase)) reasons.Add("HARD_FILTER_BODY_TYPE");
        if (filters.Segments.Count > 0 && !filters.Segments.Contains(car.Segment, StringComparer.OrdinalIgnoreCase)) reasons.Add("HARD_FILTER_SEGMENT");
        if (filters.Powertrains.Count > 0 && !filters.Powertrains.Contains(car.PowertrainType, StringComparer.OrdinalIgnoreCase)) reasons.Add("HARD_FILTER_POWERTRAIN");
        if (filters.MinimumSeats is { } seats)
        {
            if (car.Seats is null) reasons.Add("HARD_FILTER_SEATS_UNKNOWN");
            else if (car.Seats < seats) reasons.Add("HARD_FILTER_SEATS_BELOW_MINIMUM");
        }
        foreach (var code in filters.RequiredFeatureCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var feature = features.SingleOrDefault(value => string.Equals(value.Definition.Code, code, StringComparison.OrdinalIgnoreCase));
            if (feature?.Value.Status != FactStatus.Official || feature.Value.BooleanValue != true)
            {
                reasons.Add($"HARD_FILTER_FEATURE_UNVERIFIED:{code.ToUpperInvariant()}");
            }
        }
        return reasons;
    }

    private static RecommendationComponentInput ValueComponent(Price? price, IReadOnlyDictionary<Guid, SourceInfo> sources)
    {
        if (price?.Amount is null)
        {
            return Missing(RecommendationComponentCodes.Value, "Giá / giá trị", "No effective official purchase price is published.");
        }
        return Component(
            RecommendationComponentCodes.Value,
            "Giá / giá trị",
            [new("purchase_price", "Giá mua hiện hành", price.Amount.Value, price.Currency, RecommendationDirection.LowerIsBetter)],
            [price.SourceFactId],
            sources,
            $"Uses {price.PriceType} effective price; lower is better inside the eligible peer set.");
    }

    private static RecommendationComponentInput SpaceComponent(IReadOnlyList<SpecRow> specs, IReadOnlyDictionary<Guid, SourceInfo> sources)
    {
        var required = new[]
        {
            ("SEATS", "Số chỗ", "seat"),
            ("LENGTH_MM", "Chiều dài", "mm"),
            ("WIDTH_MM", "Chiều rộng", "mm"),
            ("WHEELBASE_MM", "Chiều dài cơ sở", "mm"),
        };
        var rows = required.Select(item => new { item, row = specs.SingleOrDefault(value => value.Definition.Code == item.Item1) }).ToArray();
        if (rows.Any(value => value.row?.Value.Status != FactStatus.Official || value.row.Value.NumericValue is null))
        {
            return Missing(RecommendationComponentCodes.Space, "Không gian", "Seats, length, width and wheelbase are not all officially reviewed.");
        }
        return Component(
            RecommendationComponentCodes.Space,
            "Không gian",
            rows.Select(value => new RecommendationMetricInput(value.item.Item1.ToLowerInvariant(), value.item.Item2, value.row!.Value.NumericValue!.Value, value.item.Item3, RecommendationDirection.HigherIsBetter)).ToArray(),
            rows.Select(value => value.row!.Value.SourceFactId),
            sources,
            "Averages independently normalized seats, length, width and wheelbase; no single dimension can dominate by unit scale.");
    }

    private static RecommendationComponentInput PerformanceComponent(PowertrainProfile? profile, IReadOnlyDictionary<Guid, SourceInfo> sources)
    {
        var power = profile?.CombinedPowerKw ?? profile?.MotorPowerKw ?? profile?.EnginePowerKw;
        if (power is null)
        {
            return Missing(RecommendationComponentCodes.Performance, "Hiệu năng", "No officially reviewed engine, motor or combined power is published.");
        }
        return Component(
            RecommendationComponentCodes.Performance,
            "Hiệu năng",
            [new("power_kw", "Công suất", power.Value, "kW", RecommendationDirection.HigherIsBetter)],
            [profile!.SourceFactId],
            sources,
            "Uses combined power when available, then motor power, then engine power; higher is better within peers.");
    }

    private static RecommendationComponentInput FeatureComponent(
        string code,
        string label,
        IReadOnlyList<FeatureRow> rows,
        IReadOnlySet<string> canonicalCodes,
        IReadOnlyDictionary<Guid, SourceInfo> sources)
    {
        var observed = rows.Where(value => canonicalCodes.Contains(value.Definition.Code)
            && value.Value.Status is FactStatus.Official or FactStatus.NotAvailable or FactStatus.NotApplicable).ToArray();
        if (observed.Length < MinimumObservedFeaturesPerComponent)
        {
            return Missing(code, label, $"Only {observed.Length}/{MinimumObservedFeaturesPerComponent} required explicit canonical observations are published.");
        }
        var present = observed.Count(value => value.Value.Status == FactStatus.Official && value.Value.BooleanValue == true);
        return Component(
            code,
            label,
            [new("verified_present_count", "Trang bị xác minh là có", present, "feature", RecommendationDirection.HigherIsBetter)],
            observed.Select(value => value.Value.SourceFactId),
            sources,
            $"Counts officially present features across {observed.Length} explicit observations; UNKNOWN rows are never treated as absent.");
    }

    private static RecommendationComponentInput RunningCostComponent(
        CurrentSearchableTrim car,
        EnergyProfile? profile,
        IReadOnlyList<EnergyPrice> prices,
        IReadOnlyDictionary<Guid, SourceInfo> sources)
    {
        if (profile is null || !Enum.TryParse<PowertrainType>(car.PowertrainType, true, out var powertrain))
        {
            return Missing(RecommendationComponentCodes.RunningCost, "Chi phí vận hành", "Official consumption or powertrain data is unavailable.");
        }
        if (powertrain is PowertrainType.Phev or PowertrainType.Erev)
        {
            return Missing(RecommendationComponentCodes.RunningCost, "Chi phí vận hành", "PHEV/EREV requires an explicit electric-distance share; the recommendation form does not invent one.");
        }

        EnergyPrice? fuel = null;
        var home = Array.Empty<EnergyPrice>();
        if (powertrain != PowertrainType.Bev)
        {
            if (!Enum.TryParse<EnergyType>(profile.RecommendedFuel, true, out var fuelType) || fuelType == EnergyType.Electricity)
            {
                return Missing(RecommendationComponentCodes.RunningCost, "Chi phí vận hành", "Recommended fuel product is not explicitly mapped.");
            }
            fuel = prices.Where(value => value.EnergyType == fuelType).OrderByDescending(value => value.EffectiveFrom).FirstOrDefault();
            if (profile.OfficialFuelLitresPer100Km is null || fuel is null)
            {
                return Missing(RecommendationComponentCodes.RunningCost, "Chi phí vận hành", "Official fuel consumption or current reviewed fuel price is unavailable.");
            }
        }
        else
        {
            home = prices.Where(value => value.EnergyType == EnergyType.Electricity).OrderBy(value => value.TierFromInclusive).ToArray();
            if (profile.OfficialElectricKwhPer100Km is null || home.Length != 6)
            {
                return Missing(RecommendationComponentCodes.RunningCost, "Chi phí vận hành", "Official electric consumption or the six current EVN tiers are unavailable.");
            }
        }

        try
        {
            var result = EnergyCostEvaluator.Evaluate(
                new EnergyCostContext(
                    powertrain,
                    100,
                    profile.OfficialFuelLitresPer100Km,
                    profile.OfficialElectricKwhPer100Km,
                    powertrain == PowertrainType.Bev ? 1 : 0,
                    1,
                    0.9m,
                    HomeChargingMode.EvnMarginalTiers,
                    250,
                    null,
                    0,
                    0,
                    0,
                    null,
                    "Personal",
                    null,
                    false),
                fuel is null ? null : Rate(fuel),
                home.Select(Rate).ToArray(),
                null,
                []);
            return Component(
                RecommendationComponentCodes.RunningCost,
                "Chi phí vận hành",
                [new("energy_cost_100km", "Chi phí năng lượng / 100 km", result.NormalizedCost, "VND/100km", RecommendationDirection.LowerIsBetter)],
                new Guid?[] { profile.SourceFactId, fuel?.SourceFactId }.Concat(home.Select(value => value.SourceFactId)),
                sources,
                "Calculated by the authoritative energy engine from official consumption and effective reviewed rates; lower is better.");
        }
        catch (InvalidOperationException)
        {
            return Missing(RecommendationComponentCodes.RunningCost, "Chi phí vận hành", "The authoritative energy engine rejected incomplete or incompatible inputs.");
        }
    }

    private static RecommendationComponentInput Component(
        string code,
        string label,
        IReadOnlyList<RecommendationMetricInput> metrics,
        IEnumerable<Guid?> factIds,
        IReadOnlyDictionary<Guid, SourceInfo> sources,
        string explanation)
    {
        var ids = factIds.Where(value => value is not null).Select(value => value!.Value).Distinct().ToArray();
        var trusted = ids.Length > 0 && ids.All(id => sources.TryGetValue(id, out var source) && source.Trusted);
        return new RecommendationComponentInput(code, label, metrics, ids, trusted, explanation);
    }

    private static RecommendationComponentInput Missing(string code, string label, string reason) =>
        new(code, label, [], [], false, reason);

    private async Task<Dictionary<Guid, SourceInfo>> LoadSourcesAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return [];
        var rows = await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where ids.Contains(fact.Id)
                select new { Fact = fact, Snapshot = snapshot, Source = source })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(value => value.Fact.Id, value =>
        {
            var fetched = value.Source.LastFetchedAt ?? value.Snapshot.FetchedAt;
            var stale = fetched + value.Source.RefreshInterval < instant;
            var trusted = value.Fact.Status == FactStatus.Official
                && value.Fact.Confidence is ConfidenceLevel.TrustedSingleSource or ConfidenceLevel.VerifiedMultiSource or ConfidenceLevel.VerifiedOfficial
                && !stale;
            return new SourceInfo(
                value.Fact.Id,
                value.Source.Id,
                value.Source.Name,
                value.Source.Url,
                value.Source.AuthorityLevel.ToString(),
                value.Source.ContentType.ToString(),
                value.Snapshot.FetchedAt,
                value.Snapshot.ContentHash,
                value.Fact.Status.ToString(),
                value.Fact.Confidence.ToString(),
                stale,
                trusted);
        });
    }

    private static RecommendationCandidate Contract(
        RecommendationCandidateScore score,
        CurrentSearchableTrim car,
        IReadOnlyDictionary<Guid, Price> prices,
        IReadOnlyDictionary<Guid, SourceInfo> sources)
    {
        var price = prices.GetValueOrDefault(car.TrimId);
        return new RecommendationCandidate(
            new RecommendationVehicle(
                car.TrimId,
                car.BrandName,
                car.ModelName,
                car.TrimName,
                car.ModelYear,
                car.BodyType,
                car.Segment,
                car.PowertrainType,
                price?.Amount,
                price?.Currency ?? "VND"),
            score.Rank,
            score.Completeness,
            score.CompletenessPassed,
            score.TrustPassed,
            score.OverallScore,
            score.PricePerformanceScore,
            score.Components.Select(component => new RecommendationComponent(
                component.Code,
                component.Label,
                component.Weight,
                component.RawMetrics.Select(metric => new RecommendationMetric(metric.Code, metric.Label, metric.Value, metric.Unit, metric.Direction.ToString())).ToArray(),
                component.Score,
                component.IncludedInOverall,
                component.Trusted,
                component.SourceFactIds.Where(sources.ContainsKey).Select(id => Source(sources[id])).ToArray(),
                component.Explanation)).ToArray(),
            score.Reasons);
    }

    private static RecommendationSource Source(SourceInfo value) => new(
        value.SourceFactId,
        value.SourceId,
        value.Name,
        value.Url,
        value.Authority,
        value.ContentType,
        value.FetchedAt,
        value.ContentHash,
        value.FactStatus,
        value.Confidence,
        value.Stale);

    private static Dictionary<string, decimal> WeightMap(RecommendationWeightsRequest weights) =>
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [RecommendationComponentCodes.Value] = weights.PriceValue,
            [RecommendationComponentCodes.RunningCost] = weights.RunningCost,
            [RecommendationComponentCodes.Space] = weights.Space,
            [RecommendationComponentCodes.SafetyAdas] = weights.SafetyAdas,
            [RecommendationComponentCodes.Comfort] = weights.Comfort,
            [RecommendationComponentCodes.Performance] = weights.Performance,
            [RecommendationComponentCodes.Technology] = weights.Technology,
        };

    private static EnergyRate Rate(EnergyPrice value) => new(
        value.Id,
        value.EnergyType,
        value.Provider,
        value.Amount,
        value.TaxRate,
        value.TaxIncluded,
        value.TierFromInclusive,
        value.TierToInclusive);

    private static int PricePriority(PriceType type) => type switch
    {
        PriceType.DealerCashPrice => 0,
        PriceType.PromotionPrice => 1,
        PriceType.Msrp => 2,
        _ => 3,
    };

    private static void Validate(RecommendationRequest request)
    {
        if (request.MaximumResults is < 1 or > 20
            || string.IsNullOrWhiteSpace(request.RegionCode)
            || request.HardFilters.MaximumPrice is < 0
            || request.HardFilters.MinimumSeats is < 1 or > 100
            || request.HardFilters.RequiredFeatureCodes.Any(string.IsNullOrWhiteSpace))
        {
            throw new RecommendationException(StatusCodes.Status400BadRequest, "RECOMMENDATION_INPUT_INVALID", "MaximumResults must be 1-20; region and filter values must be valid and non-negative.");
        }
        if (request.AsOfDate is { } date && (date.Year < 2020 || date.Year > 2100))
        {
            throw new RecommendationException(StatusCodes.Status400BadRequest, "RECOMMENDATION_DATE_INVALID", "AsOfDate must be between 2020 and 2100.");
        }
    }

    private sealed record SpecRow(TrimSpec Value, SpecDefinition Definition);
    private sealed record FeatureRow(TrimFeature Value, FeatureDefinition Definition);
    private sealed record SourceInfo(
        Guid SourceFactId,
        Guid SourceId,
        string Name,
        string Url,
        string Authority,
        string ContentType,
        DateTimeOffset FetchedAt,
        string ContentHash,
        string FactStatus,
        string Confidence,
        bool Stale,
        bool Trusted);
}
