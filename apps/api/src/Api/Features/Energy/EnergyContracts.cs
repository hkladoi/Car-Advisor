namespace VietnamCarPlatform.Api.Features.Energy;

public sealed class EnergyCalculationRequest
{
    public Guid TrimId { get; init; }
    public DateTimeOffset? CalculationDate { get; init; }
    public decimal MonthlyKilometres { get; init; } = 1_000;
    public string? FuelType { get; init; }
    public decimal EvShare { get; init; } = 0.5m;
    public decimal HomeChargingShare { get; init; } = 1m;
    public decimal ChargingEfficiency { get; init; } = 0.9m;
    public string HomeMode { get; init; } = "EvnMarginalTiers";
    public decimal HouseholdBaseKwh { get; init; }
    public decimal? CustomHomeAmountPerKwh { get; init; }
    public string ChargingProviderSlug { get; init; } = "v-green";
    public string? ConnectorType { get; init; }
    public decimal? ChargingPowerKw { get; init; }
    public int PublicSessions { get; init; }
    public int SessionsUsedThisMonth { get; init; }
    public int PostChargeMinutesPerSession { get; init; }
    public string CustomerType { get; init; } = "Personal";
    public DateOnly? PurchaseDate { get; init; }
    public bool PromotionEligibilityConfirmed { get; init; }
}

public sealed record EnergySourceReference(
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
    DateTimeOffset FreshUntil,
    bool IsStale);

public sealed record EnergyVehicleIdentity(
    Guid TrimId,
    Guid BrandId,
    Guid ModelId,
    string BrandName,
    string ModelName,
    string TrimName,
    int ModelYear,
    string Powertrain);

public sealed record EnergyProfileReference(
    Guid ProfileId,
    decimal? OfficialFuelLitresPer100Km,
    decimal? OfficialElectricKwhPer100Km,
    string? FuelConsumptionCondition,
    string? ElectricConsumptionCondition,
    string? TestCycle,
    string? ConsumptionNotes,
    EnergySourceReference? Source);

public sealed record AppliedEnergyRate(
    Guid RateId,
    string Kind,
    string Provider,
    decimal? Amount,
    string Unit,
    string Currency,
    decimal? TaxRate,
    bool TaxIncluded,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    EnergySourceReference? Source);

public sealed record AppliedChargingPromotion(
    Guid PromotionId,
    string Benefit,
    decimal? BenefitValue,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    EnergySourceReference? Source);

public sealed record EnergyBreakdownItem(
    string Component,
    decimal Quantity,
    string Unit,
    decimal NormalizedAmount,
    decimal CurrentAmount,
    string Detail,
    AppliedEnergyRate? AppliedRate);

public sealed record EnergyCalculationResult(
    decimal CurrentCost,
    decimal NormalizedCost,
    decimal PromotionSavings,
    decimal FuelLitres,
    decimal BatteryEnergyKwh,
    decimal GridEnergyKwh,
    string Currency);

public sealed record EnergyCalculationResponse(
    EnergyCalculationResult Result,
    EnergyVehicleIdentity Vehicle,
    EnergyProfileReference EnergyProfile,
    DateTimeOffset CalculationDate,
    IReadOnlyList<EnergyBreakdownItem> Breakdown,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<AppliedEnergyRate> AppliedRates,
    IReadOnlyList<AppliedChargingPromotion> AppliedPromotions,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CalculatedAt);

public sealed class EnergyCalculationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
