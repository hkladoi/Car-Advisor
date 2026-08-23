using VietnamCarPlatform.Infrastructure.Catalog;

namespace VietnamCarPlatform.Api.Features.Catalog;

public enum FeatureFilterMode
{
    And,
    Or,
}

public sealed record CatalogFilter(
    string Search,
    IReadOnlySet<string> Brands,
    IReadOnlySet<string> Models,
    IReadOnlySet<string> BodyTypes,
    IReadOnlySet<string> Segments,
    IReadOnlySet<string> Powertrains,
    int? Seats,
    decimal? MsrpMin,
    decimal? MsrpMax,
    decimal? CurrentPriceMin,
    decimal? CurrentPriceMax,
    decimal? OnRoadMin,
    decimal? OnRoadMax,
    decimal? LengthMin,
    decimal? LengthMax,
    decimal? WidthMin,
    decimal? WidthMax,
    decimal? HeightMin,
    decimal? HeightMax,
    decimal? RangeMin,
    decimal? RangeMax,
    decimal? BatteryMin,
    decimal? BatteryMax,
    decimal? ConsumptionMin,
    decimal? ConsumptionMax,
    IReadOnlySet<string> Features,
    FeatureFilterMode FeatureMode,
    IReadOnlySet<string> Colors,
    int Page,
    int PageSize,
    string Sort)
{
    private static readonly HashSet<string> AllowedSorts = new(
        ["relevance", "price_asc", "price_desc", "name_asc", "newest"],
        StringComparer.Ordinal);

    public static bool TryCreate(CatalogRequest request, out CatalogFilter? filter, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? 24;
        if (page < 1)
        {
            errors[nameof(request.Page)] = ["Page must be at least 1."];
        }

        if (pageSize is < 1 or > 100)
        {
            errors[nameof(request.PageSize)] = ["PageSize must be between 1 and 100."];
        }

        var sort = (request.Sort ?? "relevance").Trim().ToLowerInvariant();
        if (!AllowedSorts.Contains(sort))
        {
            errors[nameof(request.Sort)] = [$"Sort must be one of: {string.Join(", ", AllowedSorts)}."];
        }

        var mode = (request.FeatureMode ?? "and").Trim().ToLowerInvariant() switch
        {
            "and" => FeatureFilterMode.And,
            "or" => FeatureFilterMode.Or,
            _ => (FeatureFilterMode?)null,
        };
        if (mode is null)
        {
            errors[nameof(request.FeatureMode)] = ["FeatureMode must be 'and' or 'or'."];
        }

        ValidateRange(request.MsrpMin, request.MsrpMax, nameof(request.MsrpMin), nameof(request.MsrpMax), errors);
        ValidateRange(request.CurrentPriceMin, request.CurrentPriceMax, nameof(request.CurrentPriceMin), nameof(request.CurrentPriceMax), errors);
        ValidateRange(request.OnRoadMin, request.OnRoadMax, nameof(request.OnRoadMin), nameof(request.OnRoadMax), errors);
        ValidateRange(request.LengthMin, request.LengthMax, nameof(request.LengthMin), nameof(request.LengthMax), errors);
        ValidateRange(request.WidthMin, request.WidthMax, nameof(request.WidthMin), nameof(request.WidthMax), errors);
        ValidateRange(request.HeightMin, request.HeightMax, nameof(request.HeightMin), nameof(request.HeightMax), errors);
        ValidateRange(request.RangeMin, request.RangeMax, nameof(request.RangeMin), nameof(request.RangeMax), errors);
        ValidateRange(request.BatteryMin, request.BatteryMax, nameof(request.BatteryMin), nameof(request.BatteryMax), errors);
        ValidateRange(request.ConsumptionMin, request.ConsumptionMax, nameof(request.ConsumptionMin), nameof(request.ConsumptionMax), errors);

        if (errors.Count > 0 || mode is null)
        {
            filter = null;
            return false;
        }

        filter = new CatalogFilter(
            SearchNormalizer.Normalize(request.Search),
            Values(request.Brand),
            Values(request.Model),
            Values(request.Body),
            Values(request.Segment),
            Values(request.Powertrain),
            request.Seats,
            request.MsrpMin,
            request.MsrpMax,
            request.CurrentPriceMin,
            request.CurrentPriceMax,
            request.OnRoadMin,
            request.OnRoadMax,
            request.LengthMin,
            request.LengthMax,
            request.WidthMin,
            request.WidthMax,
            request.HeightMin,
            request.HeightMax,
            request.RangeMin,
            request.RangeMax,
            request.BatteryMin,
            request.BatteryMax,
            request.ConsumptionMin,
            request.ConsumptionMax,
            Values(request.Features, uppercase: true),
            mode.Value,
            Values(request.Colors, uppercase: true),
            page,
            pageSize,
            sort);
        return true;
    }

    public bool Matches(CurrentSearchableTrim car)
    {
        if (!MatchesSearch(car.SearchText, Search)
            || !Matches(Brands, car.BrandSlug)
            || !Matches(Models, car.ModelSlug)
            || !Matches(BodyTypes, car.BodyType)
            || !Matches(Segments, car.Segment)
            || !Matches(Powertrains, car.PowertrainType)
            || (Seats.HasValue && car.Seats != Seats.Value)
            || !InRange(car.MsrpAmount, MsrpMin, MsrpMax)
            || !InRange(car.CurrentPriceAmount, CurrentPriceMin, CurrentPriceMax)
            || !Overlaps(car.OnRoadMinAmount, car.OnRoadMaxAmount, OnRoadMin, OnRoadMax)
            || !InRange(car.LengthMm, LengthMin, LengthMax)
            || !InRange(car.WidthMm, WidthMin, WidthMax)
            || !InRange(car.HeightMm, HeightMin, HeightMax)
            || !InRange(car.OfficialRangeKm, RangeMin, RangeMax)
            || !InRange(car.UsableBatteryKwh, BatteryMin, BatteryMax)
            || !MatchesConsumption(car)
            || !MatchesFeatures(car.FeatureCodes)
            || !MatchesAny(Colors, car.ColorCodes))
        {
            return false;
        }

        return true;
    }

    public int SearchScore(CurrentSearchableTrim car)
    {
        if (Search.Length == 0)
        {
            return 0;
        }

        var score = car.SearchText.Contains(Search, StringComparison.Ordinal) ? 1000 : 0;
        foreach (var token in SearchNormalizer.Tokens(Search))
        {
            if (car.ModelNameNormalized().Equals(token, StringComparison.Ordinal))
            {
                score += 300;
            }
            else if (car.SearchText.Contains(token, StringComparison.Ordinal))
            {
                score += 100;
            }
        }

        return score;
    }

    private bool MatchesFeatures(IReadOnlyCollection<string> actual)
    {
        if (Features.Count == 0)
        {
            return true;
        }

        var values = actual.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return FeatureMode == FeatureFilterMode.And
            ? Features.All(values.Contains)
            : Features.Any(values.Contains);
    }

    private bool MatchesConsumption(CurrentSearchableTrim car)
    {
        if (!ConsumptionMin.HasValue && !ConsumptionMax.HasValue)
        {
            return true;
        }

        return InRange(car.FuelLitresPer100Km, ConsumptionMin, ConsumptionMax)
            || InRange(car.ElectricKwhPer100Km, ConsumptionMin, ConsumptionMax);
    }

    private static bool MatchesSearch(string searchText, string search)
    {
        if (search.Length == 0)
        {
            return true;
        }

        if (searchText.Contains(search, StringComparison.Ordinal))
        {
            return true;
        }

        return SearchNormalizer.Tokens(search).Any(token => searchText.Contains(token, StringComparison.Ordinal));
    }

    private static bool Matches(IReadOnlySet<string> expected, string actual) =>
        expected.Count == 0 || expected.Contains(actual);

    private static bool MatchesAny(IReadOnlySet<string> expected, IReadOnlyCollection<string> actual) =>
        expected.Count == 0 || actual.Any(expected.Contains);

    private static bool InRange(decimal? value, decimal? minimum, decimal? maximum)
    {
        if (!minimum.HasValue && !maximum.HasValue)
        {
            return true;
        }

        return value.HasValue
            && (!minimum.HasValue || value >= minimum)
            && (!maximum.HasValue || value <= maximum);
    }

    private static bool Overlaps(decimal? actualMinimum, decimal? actualMaximum, decimal? requestedMinimum, decimal? requestedMaximum)
    {
        if (!requestedMinimum.HasValue && !requestedMaximum.HasValue)
        {
            return true;
        }

        return actualMinimum.HasValue
            && actualMaximum.HasValue
            && (!requestedMinimum.HasValue || actualMaximum >= requestedMinimum)
            && (!requestedMaximum.HasValue || actualMinimum <= requestedMaximum);
    }

    private static HashSet<string> Values(string? raw, bool uppercase = false)
    {
        var values = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => uppercase ? value.ToUpperInvariant() : value.ToLowerInvariant());
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateRange(
        decimal? minimum,
        decimal? maximum,
        string minimumName,
        string maximumName,
        IDictionary<string, string[]> errors)
    {
        if (minimum < 0)
        {
            errors[minimumName] = ["Minimum cannot be negative."];
        }

        if (maximum < 0)
        {
            errors[maximumName] = ["Maximum cannot be negative."];
        }

        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
        {
            errors[maximumName] = ["Maximum must be greater than or equal to minimum."];
        }
    }
}

internal static class CurrentSearchableTrimExtensions
{
    public static string ModelNameNormalized(this CurrentSearchableTrim car) => SearchNormalizer.Normalize(car.ModelName);
}
