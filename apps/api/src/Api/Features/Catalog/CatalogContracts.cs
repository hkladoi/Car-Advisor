using Microsoft.AspNetCore.Mvc;

namespace VietnamCarPlatform.Api.Features.Catalog;

public sealed class CatalogRequest
{
    [FromQuery(Name = "q")]
    public string? Search { get; init; }

    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? Sort { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? Body { get; init; }
    public string? Segment { get; init; }
    public string? Powertrain { get; init; }
    public int? Seats { get; init; }
    public decimal? MsrpMin { get; init; }
    public decimal? MsrpMax { get; init; }
    public decimal? CurrentPriceMin { get; init; }
    public decimal? CurrentPriceMax { get; init; }
    public decimal? OnRoadMin { get; init; }
    public decimal? OnRoadMax { get; init; }
    public decimal? LengthMin { get; init; }
    public decimal? LengthMax { get; init; }
    public decimal? WidthMin { get; init; }
    public decimal? WidthMax { get; init; }
    public decimal? HeightMin { get; init; }
    public decimal? HeightMax { get; init; }
    public decimal? RangeMin { get; init; }
    public decimal? RangeMax { get; init; }
    public decimal? BatteryMin { get; init; }
    public decimal? BatteryMax { get; init; }
    public decimal? ConsumptionMin { get; init; }
    public decimal? ConsumptionMax { get; init; }
    public string? Features { get; init; }
    public string? FeatureMode { get; init; }
    public string? Colors { get; init; }
}

public sealed record BrandItem(
    Guid Id,
    string Name,
    string Slug,
    int CurrentTrimCount);

public sealed record BrandsResponse(
    IReadOnlyList<BrandItem> Data,
    DateTimeOffset GeneratedAt);

public sealed record CatalogCar(
    Guid TrimId,
    string BrandName,
    string BrandSlug,
    string ModelName,
    string ModelSlug,
    string GenerationCode,
    int ModelYear,
    string TrimName,
    string TrimSlug,
    string MarketStatus,
    string BodyType,
    string Segment,
    string PowertrainType,
    MoneyValue? Msrp,
    MoneyValue? CurrentPrice,
    MoneyRange? OnRoadRange,
    CatalogSpecifications Specifications,
    IReadOnlyList<string> FeatureCodes,
    IReadOnlyList<string> ColorCodes,
    string? PrimaryImageUrl,
    DateTimeOffset DataUpdatedAt);

public sealed record MoneyValue(decimal Amount, string Currency, string? Type = null);

public sealed record MoneyRange(decimal Minimum, decimal Maximum, string Currency);

public sealed record CatalogSpecifications(
    decimal? Seats,
    decimal? LengthMm,
    decimal? WidthMm,
    decimal? HeightMm,
    decimal? WheelbaseMm,
    decimal? OfficialRangeKm,
    decimal? UsableBatteryKwh,
    decimal? FuelLitresPer100Km,
    decimal? ElectricKwhPer100Km);

public sealed record FacetValue(string Value, int Count);

public sealed record CatalogFacets(
    IReadOnlyList<FacetValue> Brands,
    IReadOnlyList<FacetValue> Models,
    IReadOnlyList<FacetValue> BodyTypes,
    IReadOnlyList<FacetValue> Segments,
    IReadOnlyList<FacetValue> Powertrains,
    IReadOnlyList<FacetValue> Seats,
    IReadOnlyList<FacetValue> Features,
    IReadOnlyList<FacetValue> Colors,
    NumericRange? Msrp,
    NumericRange? CurrentPrice,
    NumericRange? OnRoad,
    NumericRange? RangeKm,
    NumericRange? BatteryKwh);

public sealed record NumericRange(decimal Minimum, decimal Maximum);

public sealed record Pagination(int Page, int PageSize, int TotalItems, int TotalPages);

public sealed record CarsResponse(
    IReadOnlyList<CatalogCar> Data,
    CatalogFacets Facets,
    Pagination Pagination,
    string FeatureFilterSemantics,
    DateTimeOffset GeneratedAt);

public sealed record SourceBadge(
    Guid SourceId,
    string Name,
    string Url,
    string Authority,
    string ContentType,
    DateTimeOffset FetchedAt,
    string ContentHash,
    string FactStatus,
    string Confidence);

public sealed record TrimSwitchItem(
    Guid TrimId,
    string Name,
    string Slug,
    int ModelYear,
    MoneyValue? CurrentPrice,
    bool Selected);

public sealed record PriceDetail(
    Guid Id,
    string Type,
    string Status,
    decimal? Amount,
    string Currency,
    string RegionScope,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    SourceBadge? Source);

public sealed record SpecificationDetail(
    string Code,
    string Label,
    string Group,
    string Status,
    decimal? NumericValue,
    string? TextValue,
    string? EnumValue,
    string? Unit,
    SourceBadge? Source);

public sealed record FeatureDetail(
    string Code,
    string Label,
    string Group,
    string Status,
    bool? BooleanValue,
    decimal? NumericValue,
    string? TextValue,
    string? EnumValue,
    SourceBadge? Source);

public sealed record ColorDetail(
    string Code,
    string Name,
    string? HexHint,
    string Type,
    string Availability,
    decimal? ExtraPrice,
    string Currency,
    SourceBadge? Source);

public sealed record GalleryImage(
    Guid Id,
    string Type,
    string Url,
    string RightsStatus,
    string? RightsNote);

public sealed record WarrantyDetail(
    int? VehicleMonths,
    int? VehicleKilometres,
    int? BatteryMonths,
    int? BatteryKilometres,
    string? Conditions,
    SourceBadge? Source);

public sealed record DealerOfferBenefitDetail(
    string Type,
    decimal? CashValue,
    decimal? StatedValue,
    string Currency,
    bool IsCashEquivalent,
    string? ExclusivityGroup,
    string? Note);

public sealed record DealerOfferDetail(
    Guid Id,
    string DealerName,
    string BranchName,
    string ProvinceCode,
    string Headline,
    string Status,
    string ConditionsJson,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    IReadOnlyList<DealerOfferBenefitDetail> Benefits,
    SourceBadge? Source);

public sealed record CarDetailResponse(
    CatalogCar Car,
    IReadOnlyList<TrimSwitchItem> Trims,
    IReadOnlyList<PriceDetail> Prices,
    IReadOnlyList<GalleryImage> Gallery,
    IReadOnlyList<SpecificationDetail> Specifications,
    IReadOnlyList<FeatureDetail> Features,
    IReadOnlyList<ColorDetail> Colors,
    WarrantyDetail? Warranty,
    IReadOnlyList<DealerOfferDetail> DealerOffers,
    SourceBadge? PrimarySource,
    DateTimeOffset GeneratedAt);
