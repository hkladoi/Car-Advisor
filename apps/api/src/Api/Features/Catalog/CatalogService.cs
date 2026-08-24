using Microsoft.EntityFrameworkCore;
using System.Globalization;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Infrastructure.Catalog;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Catalog;

public interface ICatalogService
{
    Task<BrandsResponse> GetBrandsAsync(CancellationToken cancellationToken);
    Task<CarsResponse> GetCarsAsync(CatalogFilter filter, CancellationToken cancellationToken);
    Task<CarDetailResponse?> GetCarAsync(Guid trimId, CancellationToken cancellationToken);
}

public sealed class CatalogService(
    AppDbContext database,
    CatalogCache cache,
    TimeProvider timeProvider) : ICatalogService
{
    public async Task<BrandsResponse> GetBrandsAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "catalog:v1:brands";
        var cached = await cache.GetAsync<BrandsResponse>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var currentCars = await database.CurrentSearchableTrims
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var brands = currentCars
            .GroupBy(car => new { car.BrandId, car.BrandName, car.BrandSlug })
            .Select(group => new BrandItem(group.Key.BrandId, group.Key.BrandName, group.Key.BrandSlug, group.Count()))
            .OrderBy(brand => brand.Name)
            .ToList();

        var response = new BrandsResponse(brands, timeProvider.GetUtcNow());
        await cache.SetAsync(cacheKey, response, cancellationToken);
        return response;
    }

    public async Task<CarsResponse> GetCarsAsync(CatalogFilter filter, CancellationToken cancellationToken)
    {
        var cacheKey = CatalogCache.RequestKey(filter);
        var cached = await cache.GetAsync<CarsResponse>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var query = SearchQuery(filter.Search);
        var candidates = await query.ToListAsync(cancellationToken);
        var filtered = candidates.Where(filter.Matches).ToList();
        var facets = CreateFacets(filtered);
        var sorted = Sort(filtered, filter);
        var totalItems = filtered.Count;
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)filter.PageSize);
        var offset = (long)(filter.Page - 1) * filter.PageSize;
        var page = offset > int.MaxValue
            ? []
            : sorted.Skip((int)offset).Take(filter.PageSize).Select(ToContract).ToList();

        var response = new CarsResponse(
            page,
            facets,
            new Pagination(filter.Page, filter.PageSize, totalItems, totalPages),
            filter.FeatureMode == FeatureFilterMode.And
                ? "AND: every requested canonical feature must be officially present on the same trim."
                : "OR: at least one requested canonical feature must be officially present on the trim.",
            timeProvider.GetUtcNow());
        await cache.SetAsync(cacheKey, response, cancellationToken);
        return response;
    }

    public async Task<CarDetailResponse?> GetCarAsync(Guid trimId, CancellationToken cancellationToken)
    {
        var cacheKey = $"catalog:v1:detail:{trimId:N}";
        var cached = await cache.GetAsync<CarDetailResponse>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var car = await database.CurrentSearchableTrims
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TrimId == trimId, cancellationToken);
        if (car is null)
        {
            return null;
        }

        var trimRows = await database.CurrentSearchableTrims
            .AsNoTracking()
            .Where(value => value.ModelId == car.ModelId)
            .OrderByDescending(value => value.ModelYear)
            .ThenBy(value => value.TrimName)
            .ToListAsync(cancellationToken);
        var priceRows = await database.Prices
            .AsNoTracking()
            .Where(value => value.TrimId == trimId)
            .OrderByDescending(value => value.EffectiveFrom)
            .ThenBy(value => value.Priority)
            .ToListAsync(cancellationToken);
        var specificationRows = await (
                from value in database.TrimSpecs.AsNoTracking()
                join definition in database.SpecDefinitions.AsNoTracking()
                    on value.SpecDefinitionId equals definition.Id
                where value.TrimId == trimId
                orderby definition.Group, definition.Label
                select new { Value = value, Definition = definition })
            .ToListAsync(cancellationToken);
        var featureRows = await (
                from value in database.TrimFeatures.AsNoTracking()
                join definition in database.FeatureDefinitions.AsNoTracking()
                    on value.FeatureDefinitionId equals definition.Id
                where value.TrimId == trimId
                orderby definition.Group, definition.Label
                select new { Value = value, Definition = definition })
            .ToListAsync(cancellationToken);
        var colorRows = await (
                from value in database.TrimColors.AsNoTracking()
                join color in database.Colors.AsNoTracking()
                    on value.ColorId equals color.Id
                where value.TrimId == trimId
                orderby color.Name
                select new { Value = value, Color = color })
            .ToListAsync(cancellationToken);
        var imageRows = await database.VehicleImages
            .AsNoTracking()
            .Where(value => (value.TrimId == trimId || value.ModelId == car.ModelId)
                && value.StorageUrl != null
                && (value.RightsStatus == RightsStatus.Owned
                    || value.RightsStatus == RightsStatus.Licensed
                    || value.RightsStatus == RightsStatus.OfficialPressKit
                    || value.RightsStatus == RightsStatus.Permitted))
            .OrderBy(value => value.TrimId == trimId ? 0 : 1)
            .ThenBy(value => value.Type)
            .ToListAsync(cancellationToken);
        var warrantyRow = await database.WarrantyProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TrimId == trimId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var offerRows = await (
                from offer in database.DealerOffers.AsNoTracking()
                join branch in database.DealerBranches.AsNoTracking() on offer.BranchId equals branch.Id
                join dealer in database.Dealers.AsNoTracking() on branch.DealerId equals dealer.Id
                where offer.TrimId == trimId
                    && offer.Status == OfferStatus.Published
                    && offer.EffectiveFrom <= now
                    && (offer.EffectiveTo == null || offer.EffectiveTo > now)
                orderby offer.EffectiveTo, dealer.Name, branch.Name
                select new { Offer = offer, Branch = branch, Dealer = dealer })
            .ToListAsync(cancellationToken);
        var offerIds = offerRows.Select(value => value.Offer.Id).ToArray();
        var benefitRows = offerIds.Length == 0
            ? []
            : await database.DealerOfferBenefits
                .AsNoTracking()
                .Where(value => offerIds.Contains(value.OfferId))
                .OrderBy(value => value.Type)
                .ToListAsync(cancellationToken);
        var realWorldCandidates = await database.RealWorldConsumptionAggregates
            .AsNoTracking()
            .Where(value => value.BrandId == car.BrandId)
            .OrderByDescending(value => value.VehicleRegistrationYear)
            .ThenBy(value => value.FuelType)
            .ThenByDescending(value => value.SampleSize)
            .ToListAsync(cancellationToken);
        var powertrainFuelType = await database.PowertrainProfiles
            .AsNoTracking()
            .Where(value => value.TrimId == trimId)
            .Select(value => value.FuelType)
            .SingleOrDefaultAsync(cancellationToken);
        var recommendedFuel = await database.EnergyProfiles
            .AsNoTracking()
            .Where(value => value.TrimId == trimId)
            .Select(value => value.RecommendedFuel)
            .SingleOrDefaultAsync(cancellationToken);
        var realWorldRows = RealWorldConsumptionSelectionPolicy.LatestCohorts(
            realWorldCandidates,
            RealWorldConsumptionSelectionPolicy.ResolveFuel(
                car.PowertrainType,
                powertrainFuelType,
                recommendedFuel));

        var sourceFactIds = priceRows.Select(value => value.SourceFactId)
            .Concat(specificationRows.Select(value => value.Value.SourceFactId))
            .Concat(featureRows.Select(value => value.Value.SourceFactId))
            .Concat(colorRows.Select(value => value.Value.SourceFactId))
            .Concat(offerRows.Select(value => value.Offer.SourceFactId))
            .Concat(realWorldRows.Select(value => value.SourceFactId))
            .Append(warrantyRow?.SourceFactId)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        var sources = await LoadSourcesAsync(sourceFactIds, cancellationToken);
        SourceBadge? SourceFor(Guid? sourceFactId) => sourceFactId.HasValue
            && sources.TryGetValue(sourceFactId.Value, out var source)
                ? source
                : null;

        var prices = priceRows.Select(value => new PriceDetail(
            value.Id,
            value.PriceType.ToString(),
            value.Status.ToString(),
            value.Amount,
            value.Currency,
            value.RegionScope,
            value.EffectiveFrom,
            value.EffectiveTo,
            SourceFor(value.SourceFactId))).ToList();
        var specifications = specificationRows.Select(value => new SpecificationDetail(
            value.Definition.Code,
            value.Definition.Label,
            value.Definition.Group,
            value.Value.Status.ToString(),
            value.Value.NumericValue,
            value.Value.TextValue,
            value.Value.EnumValue,
            value.Definition.CanonicalUnit,
            SourceFor(value.Value.SourceFactId))).ToList();
        var features = featureRows.Select(value => new FeatureDetail(
            value.Definition.Code,
            value.Definition.Label,
            value.Definition.Group,
            value.Value.Status.ToString(),
            value.Value.BooleanValue,
            value.Value.NumericValue,
            value.Value.TextValue,
            value.Value.EnumValue,
            SourceFor(value.Value.SourceFactId))).ToList();
        var colors = colorRows.Select(value => new ColorDetail(
            value.Color.Code,
            value.Color.Name,
            value.Color.HexHint,
            value.Color.Type,
            value.Value.Availability.ToString(),
            value.Value.ExtraPrice,
            value.Value.Currency,
            SourceFor(value.Value.SourceFactId))).ToList();
        var gallery = imageRows.Select(value => new GalleryImage(
            value.Id,
            value.Type,
            value.StorageUrl!,
            value.RightsStatus.ToString(),
            value.RightsNote)).ToList();
        var warranty = warrantyRow is null ? null : new WarrantyDetail(
            warrantyRow.VehicleMonths,
            warrantyRow.VehicleKilometres,
            warrantyRow.BatteryMonths,
            warrantyRow.BatteryKilometres,
            warrantyRow.Conditions,
            SourceFor(warrantyRow.SourceFactId));
        var offers = offerRows.Select(value => new DealerOfferDetail(
            value.Offer.Id,
            value.Dealer.Name,
            value.Branch.Name,
            value.Branch.ProvinceCode,
            value.Offer.Headline,
            value.Offer.Status.ToString(),
            value.Offer.ConditionsJson,
            value.Offer.EffectiveFrom,
            value.Offer.EffectiveTo,
            benefitRows.Where(benefit => benefit.OfferId == value.Offer.Id)
                .Select(benefit => new DealerOfferBenefitDetail(
                    benefit.Type.ToString(),
                    benefit.CashValue,
                    benefit.StatedValue,
                    benefit.Currency,
                    benefit.IsCashEquivalent,
                    benefit.ExclusivityGroup,
                    benefit.Note))
                .ToList(),
            SourceFor(value.Offer.SourceFactId))).ToList();
        var realWorldConsumption = realWorldRows
            .Select(value => new { Value = value, Source = SourceFor(value.SourceFactId) })
            .Where(value => value.Source is not null)
            .Select(value => new RealWorldConsumptionReference(
                value.Value.Id,
                value.Value.VehicleRegistrationYear,
                value.Value.Manufacturer,
                value.Value.FuelType,
                value.Value.SampleSize,
                value.Value.RealWorldFuelWeightedLitresPer100Km,
                value.Value.OfficialWltpFuelWeightedLitresPer100Km,
                value.Value.FuelWeightedAbsoluteGapLitresPer100Km,
                value.Value.FuelWeightedPercentageGap,
                value.Value.RealWorldCo2WeightedGramsPerKm,
                value.Value.OfficialWltpCo2WeightedGramsPerKm,
                value.Value.Geography,
                value.Value.AggregationScope,
                false,
                value.Value.MethodologyUrl,
                value.Value.Attribution,
                value.Source!))
            .ToList();
        var trims = trimRows.Select(value => new TrimSwitchItem(
            value.TrimId,
            value.TrimName,
            value.TrimSlug,
            value.ModelYear,
            value.CurrentPriceAmount.HasValue && value.CurrentPriceCurrency is not null
                ? new MoneyValue(value.CurrentPriceAmount.Value, value.CurrentPriceCurrency, value.CurrentPriceType)
                : null,
            value.TrimId == trimId)).ToList();
        var primarySource = prices.Select(value => value.Source)
            .Concat(specifications.Select(value => value.Source))
            .Concat(features.Select(value => value.Source))
            .FirstOrDefault(value => value is not null);

        var response = new CarDetailResponse(
            ToContract(car),
            trims,
            prices,
            gallery,
            specifications,
            features,
            colors,
            warranty,
            offers,
            realWorldConsumption,
            primarySource,
            now);
        await cache.SetAsync(cacheKey, response, cancellationToken);
        return response;
    }

    private async Task<Dictionary<Guid, SourceBadge>> LoadSourcesAsync(
        Guid[] sourceFactIds,
        CancellationToken cancellationToken)
    {
        if (sourceFactIds.Length == 0)
        {
            return [];
        }

        var rows = await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where sourceFactIds.Contains(fact.Id)
                select new { Fact = fact, Snapshot = snapshot, Source = source })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            value => value.Fact.Id,
            value => new SourceBadge(
                value.Source.Id,
                value.Source.Name,
                value.Source.Url,
                value.Source.AuthorityLevel.ToString(),
                value.Source.ContentType.ToString(),
                value.Snapshot.FetchedAt,
                value.Snapshot.ContentHash,
                value.Fact.Status.ToString(),
                value.Fact.Confidence.ToString()));
    }

    private IQueryable<CurrentSearchableTrim> SearchQuery(string normalizedSearch)
    {
        if (normalizedSearch.Length == 0)
        {
            return database.CurrentSearchableTrims.AsNoTracking();
        }

        var tokens = SearchNormalizer.Tokens(normalizedSearch);
        return database.CurrentSearchableTrims
            .FromSqlInterpolated($"""
                SELECT searchable.*
                FROM current_searchable_trims AS searchable
                WHERE searchable.search_text LIKE '%' || {normalizedSearch} || '%'
                   OR searchable.search_text % {normalizedSearch}
                   OR EXISTS (
                       SELECT 1
                       FROM unnest({tokens}) AS token(value)
                       WHERE searchable.search_text LIKE '%' || token.value || '%'
                   )
                """)
            .AsNoTracking();
    }

    private static IEnumerable<CurrentSearchableTrim> Sort(
        IEnumerable<CurrentSearchableTrim> cars,
        CatalogFilter filter) => filter.Sort switch
        {
            "price_asc" => cars.OrderBy(car => car.CurrentPriceAmount is null).ThenBy(car => car.CurrentPriceAmount).ThenBy(car => car.BrandName).ThenBy(car => car.ModelName),
            "price_desc" => cars.OrderBy(car => car.CurrentPriceAmount is null).ThenByDescending(car => car.CurrentPriceAmount).ThenBy(car => car.BrandName).ThenBy(car => car.ModelName),
            "name_asc" => cars.OrderBy(car => car.BrandName).ThenBy(car => car.ModelName).ThenBy(car => car.TrimName),
            "newest" => cars.OrderByDescending(car => car.ModelYear).ThenByDescending(car => car.DataUpdatedAt).ThenBy(car => car.BrandName),
            _ => cars.OrderByDescending(filter.SearchScore).ThenBy(car => car.BrandName).ThenBy(car => car.ModelName).ThenBy(car => car.TrimName),
        };

    private static CatalogCar ToContract(CurrentSearchableTrim car) => new(
        car.TrimId,
        car.BrandName,
        car.BrandSlug,
        car.ModelName,
        car.ModelSlug,
        car.GenerationCode,
        car.ModelYear,
        car.TrimName,
        car.TrimSlug,
        car.MarketStatus,
        car.BodyType,
        car.Segment,
        car.PowertrainType,
        car.MsrpAmount.HasValue && car.MsrpCurrency is not null ? new MoneyValue(car.MsrpAmount.Value, car.MsrpCurrency, "Msrp") : null,
        car.CurrentPriceAmount.HasValue && car.CurrentPriceCurrency is not null ? new MoneyValue(car.CurrentPriceAmount.Value, car.CurrentPriceCurrency, car.CurrentPriceType) : null,
        car.OnRoadMinAmount.HasValue && car.OnRoadMaxAmount.HasValue ? new MoneyRange(car.OnRoadMinAmount.Value, car.OnRoadMaxAmount.Value, "VND") : null,
        new CatalogSpecifications(
            car.Seats,
            car.LengthMm,
            car.WidthMm,
            car.HeightMm,
            car.WheelbaseMm,
            car.OfficialRangeKm,
            car.UsableBatteryKwh,
            car.FuelLitresPer100Km,
            car.ElectricKwhPer100Km),
        car.FeatureCodes,
        car.ColorCodes,
        car.PrimaryImageUrl,
        car.DataUpdatedAt);

    private static CatalogFacets CreateFacets(IReadOnlyCollection<CurrentSearchableTrim> cars) => new(
        Facet(cars, car => car.BrandSlug),
        Facet(cars, car => car.ModelSlug),
        Facet(cars, car => car.BodyType),
        Facet(cars, car => car.Segment),
        Facet(cars, car => car.PowertrainType),
        Facet(cars.Where(car => car.Seats.HasValue), car => car.Seats!.Value.ToString("0", CultureInfo.InvariantCulture)),
        Facet(cars.SelectMany(car => car.FeatureCodes)),
        Facet(cars.SelectMany(car => car.ColorCodes)),
        Range(cars.Select(car => car.MsrpAmount)),
        Range(cars.Select(car => car.CurrentPriceAmount)),
        Range(cars.SelectMany(car => new[] { car.OnRoadMinAmount, car.OnRoadMaxAmount })),
        Range(cars.Select(car => car.OfficialRangeKm)),
        Range(cars.Select(car => car.UsableBatteryKwh)));

    private static List<FacetValue> Facet<T>(IEnumerable<T> source, Func<T, string> selector) =>
        source.Select(selector)
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new FacetValue(group.Key, group.Count()))
            .OrderByDescending(value => value.Count)
            .ThenBy(value => value.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<FacetValue> Facet(IEnumerable<string> values) => Facet(values, value => value);

    private static NumericRange? Range(IEnumerable<decimal?> source)
    {
        var values = source.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return values.Count == 0 ? null : new NumericRange(values.Min(), values.Max());
    }
}
