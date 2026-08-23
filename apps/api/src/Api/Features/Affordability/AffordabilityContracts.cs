using VietnamCarPlatform.Api.Features.Energy;
using VietnamCarPlatform.Api.Features.Registration;
using VietnamCarPlatform.Domain.Affordability;

namespace VietnamCarPlatform.Api.Features.Affordability;

public sealed class OwnershipExpenseAssumptionsRequest
{
    public decimal MonthlyKilometres { get; init; } = 1_000;
    public decimal ParkingMonthly { get; init; } = 1_200_000;
    public decimal MaintenanceReserveMonthly { get; init; } = 1_000_000;
    public decimal BodyInsuranceAnnual { get; init; }
    public decimal TyreReserveMonthly { get; init; } = 300_000;
    public decimal BatteryRentalMonthly { get; init; }
    public decimal? CompulsoryInsuranceMonthlyOverride { get; init; }
    public decimal? RoadUsageMonthlyOverride { get; init; }
    public decimal? InspectionMonthlyOverride { get; init; }
    public bool FirstInspectionExempt { get; init; } = true;
}

public sealed class OwnershipEnergyScenarioRequest
{
    public string? FuelType { get; init; } = "E10Ron95III";
    public decimal EvShare { get; init; } = 0.5m;
    public decimal HomeChargingShare { get; init; } = 1m;
    public decimal ChargingEfficiency { get; init; } = 0.9m;
    public string HomeMode { get; init; } = "EvnMarginalTiers";
    public decimal HouseholdBaseKwh { get; init; } = 250;
    public decimal? CustomHomeAmountPerKwh { get; init; }
    public string ChargingProviderSlug { get; init; } = "v-green";
    public string? ConnectorType { get; init; } = "DC";
    public decimal? ChargingPowerKw { get; init; } = 60;
    public int PublicSessions { get; init; } = 6;
    public int SessionsUsedThisMonth { get; init; }
    public int PostChargeMinutesPerSession { get; init; }
    public string CustomerType { get; init; } = "Personal";
    public DateOnly? PurchaseDate { get; init; }
    public bool PromotionEligibilityConfirmed { get; init; }
}

public sealed class OwnershipCalculationRequest
{
    public Guid TrimId { get; init; }
    public string ProvinceCode { get; init; } = "VN-01";
    public DateTimeOffset? CalculationDate { get; init; }
    public OwnershipExpenseAssumptionsRequest Expenses { get; init; } = new();
    public OwnershipEnergyScenarioRequest Energy { get; init; } = new();
}

public sealed class AffordabilityEvaluationRequest
{
    public IReadOnlyList<Guid> TrimIds { get; init; } = [];
    public string ProvinceCode { get; init; } = "VN-01";
    public DateTimeOffset? CalculationDate { get; init; }
    public string Policy { get; init; } = "Balanced";
    public decimal NetMonthlyIncome { get; init; }
    public decimal RentHousing { get; init; }
    public decimal EssentialExpenses { get; init; } = 6_000_000;
    public decimal OtherFixedDebt { get; init; }
    public decimal SavingsTarget { get; init; } = 2_000_000;
    public decimal? MaximumMonthlyVehicleSpend { get; init; }
    public OwnershipExpenseAssumptionsRequest Expenses { get; init; } = new();
    public OwnershipEnergyScenarioRequest Energy { get; init; } = new();
}

public sealed record AffordabilityVehicleIdentity(
    Guid TrimId,
    string BrandName,
    string ModelName,
    string TrimName,
    int ModelYear,
    string Powertrain);

public sealed record OwnershipCalculationResponse(
    OperatingOwnershipCostResult Result,
    AffordabilityVehicleIdentity Vehicle,
    EnergyCalculationResponse Energy,
    IReadOnlyList<AppliedRuleReference> AppliedRecurringRules,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CalculatedAt);

public sealed record AffordabilityCandidate(
    AffordabilityVehicleIdentity Vehicle,
    AffordabilityEvaluationResult Evaluation,
    OwnershipCalculationResponse Ownership);

public sealed record ExcludedAffordabilityCandidate(
    AffordabilityVehicleIdentity Vehicle,
    IReadOnlyList<string> Reasons,
    string Explanation);

public sealed record AffordabilityProfileSummary(
    decimal NetMonthlyIncome,
    decimal RentHousing,
    decimal EssentialExpenses,
    decimal OtherFixedDebt,
    decimal SavingsTarget,
    decimal? MaximumMonthlyVehicleSpend,
    decimal DisposableIncomeBeforeVehicle,
    string Currency);

public sealed record AffordabilityEvaluationResponse(
    string Policy,
    AffordabilityPolicyThresholds Thresholds,
    AffordabilityProfileSummary Profile,
    IReadOnlyList<AffordabilityCandidate> EligibleCars,
    IReadOnlyList<AffordabilityCandidate> OverBudgetCars,
    IReadOnlyList<ExcludedAffordabilityCandidate> DataExcludedCars,
    IReadOnlyList<string> Assumptions,
    DateTimeOffset EvaluatedAt);

public sealed class OwnershipCalculationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
