using VietnamCarPlatform.Api.Features.Affordability;
using VietnamCarPlatform.Api.Features.Financing;

namespace VietnamCarPlatform.Api.Features.Compare;

public sealed class CompareCalculationRequest
{
    public IReadOnlyList<Guid> TrimIds { get; init; } = [];
    public string ProvinceCode { get; init; } = "VN-01";
    public DateTimeOffset? CalculationDate { get; init; }
    public string ProfilePreset { get; init; } = "city-balanced";
    public string FinancingPreset { get; init; } = "standard-loan";
    public string Policy { get; init; } = "Balanced";
    public decimal NetMonthlyIncome { get; init; }
    public decimal RentHousing { get; init; }
    public decimal EssentialExpenses { get; init; }
    public decimal OtherFixedDebt { get; init; }
    public decimal SavingsTarget { get; init; }
    public decimal? MaximumMonthlyVehicleSpend { get; init; }
    public OwnershipExpenseAssumptionsRequest Expenses { get; init; } = new();
    public OwnershipEnergyScenarioRequest Energy { get; init; } = new();
    public PurchaseFundingRequest Purchase { get; init; } = new();
}

public sealed record CompareVehicleHeader(
    Guid TrimId,
    string BrandName,
    string ModelName,
    string TrimName,
    int ModelYear,
    string BodyType,
    string Segment,
    string Powertrain,
    DateTimeOffset DataUpdatedAt);

public sealed record CompareSourceReference(
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

public sealed record CompareCell(
    Guid TrimId,
    string State,
    decimal? NumericValue,
    string? TextValue,
    bool? BooleanValue,
    IReadOnlyList<CompareSourceReference> Sources,
    string? Note);

public sealed record CompareRow(
    string Code,
    string Label,
    string Section,
    string? CanonicalUnit,
    string Format,
    bool Different,
    IReadOnlyList<CompareCell> Cells);

public sealed record CompareScenarioSummary(
    string ProvinceCode,
    DateTimeOffset CalculationDate,
    string ProfilePreset,
    string FinancingPreset,
    string Policy,
    decimal MonthlyKilometres,
    decimal ParkingMonthly,
    string FundingSource,
    string PurchaseMethod,
    string RepaymentMethod,
    decimal? AnnualInterestRate,
    int TermMonths,
    decimal? DownPaymentPercent,
    string Currency);

public sealed record CompareCalculationResponse(
    IReadOnlyList<CompareVehicleHeader> Vehicles,
    CompareScenarioSummary Scenario,
    IReadOnlyList<CompareRow> Rows,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CalculatedAt);

public sealed class CompareCalculationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
