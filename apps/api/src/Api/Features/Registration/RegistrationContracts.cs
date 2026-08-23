using System.Text.Json.Serialization;

namespace VietnamCarPlatform.Api.Features.Registration;

public sealed class OnRoadCalculationRequest
{
    public Guid TrimId { get; init; }
    public string ProvinceCode { get; init; } = string.Empty;
    public DateTimeOffset? CalculationDate { get; init; }
    public string BuyerType { get; init; } = "Individual";
    public string VehicleType { get; init; } = "PassengerCar";
    public bool FirstInspectionExempt { get; init; } = true;
    public int RoadUsageMonths { get; init; } = 12;
    public IReadOnlyList<Guid> SelectedOfferIds { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyDictionary<string, object?>? ScenarioAttributes { get; init; }
}

public sealed record RegionItem(
    string Code,
    string Name,
    string AreaClass,
    string Type,
    RuleSourceReference? Source);

public sealed record RegionsResponse(IReadOnlyList<RegionItem> Data, DateTimeOffset GeneratedAt);

public sealed record RuleSourceReference(
    Guid SourceFactId,
    Guid SourceId,
    string Name,
    string Url,
    string Authority,
    string ContentType,
    DateTimeOffset FetchedAt,
    string ContentHash,
    string FactStatus,
    string Confidence);

public sealed record InputPriceReference(
    Guid PriceId,
    string PriceType,
    int Version,
    decimal Amount,
    string Currency,
    string RegionScope,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    RuleSourceReference? Source);

public sealed record AppliedRuleReference(
    Guid RuleId,
    string Component,
    int Version,
    int Priority,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    RuleSourceReference? Source);

public sealed record OnRoadBreakdownItem(
    string Component,
    decimal BeforeSupport,
    decimal EligibleSupport,
    decimal Amount,
    AppliedRuleReference AppliedRule);

public sealed record AppliedBenefit(
    string Type,
    decimal? CashValue,
    decimal? StatedValue,
    bool IsCashEquivalent,
    string Origin,
    Guid OriginId,
    string? Note);

public sealed record OnRoadResult(
    decimal OnRoadPrice,
    decimal EffectiveCashPurchasePrice,
    decimal InputPrice,
    decimal CashPurchaseReduction,
    decimal EligibleFeeSupportBenefits,
    string Currency);

public sealed record VehicleCalculationIdentity(
    Guid TrimId,
    string BrandName,
    string ModelName,
    string TrimName,
    int ModelYear,
    string Powertrain,
    decimal? Seats);

public sealed record OnRoadCalculationResponse(
    OnRoadResult Result,
    VehicleCalculationIdentity Vehicle,
    RegionItem Region,
    DateTimeOffset CalculationDate,
    InputPriceReference InputPrice,
    IReadOnlyList<OnRoadBreakdownItem> Breakdown,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<AppliedRuleReference> AppliedRules,
    IReadOnlyList<AppliedBenefit> AppliedBenefits,
    IReadOnlyList<AppliedBenefit> NonCashBenefits,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CalculatedAt);

public sealed class RegistrationCalculationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
