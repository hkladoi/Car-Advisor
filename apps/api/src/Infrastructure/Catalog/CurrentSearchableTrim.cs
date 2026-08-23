namespace VietnamCarPlatform.Infrastructure.Catalog;

/// <summary>
/// Denormalized, immutable projection of a currently searchable Vietnam-market trim.
/// It is backed by a PostgreSQL materialized view and refreshed by reviewed publish flows.
/// </summary>
public sealed class CurrentSearchableTrim
{
    public Guid TrimId { get; set; }
    public Guid BrandId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string BrandSlug { get; set; } = string.Empty;
    public Guid ModelId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string ModelSlug { get; set; } = string.Empty;
    public string GenerationCode { get; set; } = string.Empty;
    public int ModelYear { get; set; }
    public string TrimName { get; set; } = string.Empty;
    public string TrimSlug { get; set; } = string.Empty;
    public string MarketStatus { get; set; } = string.Empty;
    public string BodyType { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    public string PowertrainType { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public decimal? MsrpAmount { get; set; }
    public string? MsrpCurrency { get; set; }
    public decimal? CurrentPriceAmount { get; set; }
    public string? CurrentPriceType { get; set; }
    public string? CurrentPriceCurrency { get; set; }
    public decimal? OnRoadMinAmount { get; set; }
    public decimal? OnRoadMaxAmount { get; set; }
    public decimal? Seats { get; set; }
    public decimal? LengthMm { get; set; }
    public decimal? WidthMm { get; set; }
    public decimal? HeightMm { get; set; }
    public decimal? WheelbaseMm { get; set; }
    public decimal? OfficialRangeKm { get; set; }
    public decimal? UsableBatteryKwh { get; set; }
    public decimal? FuelLitresPer100Km { get; set; }
    public decimal? ElectricKwhPer100Km { get; set; }
    public string[] FeatureCodes { get; set; } = [];
    public string[] ColorCodes { get; set; } = [];
    public string? PrimaryImageUrl { get; set; }
    public DateTimeOffset DataUpdatedAt { get; set; }
}
