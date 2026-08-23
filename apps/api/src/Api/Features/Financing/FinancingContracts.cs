using VietnamCarPlatform.Api.Features.Affordability;
using VietnamCarPlatform.Api.Features.Registration;
using VietnamCarPlatform.Domain.Affordability;

namespace VietnamCarPlatform.Api.Features.Financing;

public sealed class PurchaseFundingRequest
{
    public string FundingSource { get; init; } = "SelfFunded";
    public string PurchaseMethod { get; init; } = "Loan";
    public decimal AvailableCash { get; init; }
    public decimal FamilyContribution { get; init; }
    public decimal TradeInNetValue { get; init; }
    public decimal? DownPaymentAmount { get; init; }
    public decimal? DownPaymentPercent { get; init; } = 0.2m;
    public decimal? AnnualInterestRate { get; init; } = 0.1m;
    public Guid? InterestRateSourceFactId { get; init; }
    public int TermMonths { get; init; } = 60;
    public string RepaymentMethod { get; init; } = "Annuity";
    public decimal BankFees { get; init; }
    public decimal LoanInsuranceUpfront { get; init; }
    public IReadOnlyList<Guid> SelectedDealerOfferIds { get; init; } = [];
}

public sealed class FinancingCalculationRequest
{
    public Guid TrimId { get; init; }
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
    public PurchaseFundingRequest Purchase { get; init; } = new();
}

public sealed record InterestRateReference(
    decimal AnnualInterestRate,
    string Origin,
    string? FieldPath,
    string? RawValue,
    RuleSourceReference? Source);

public sealed record AppliedDealerFinancingCreditResponse(
    Guid OfferId,
    Guid BenefitId,
    string OfferHeadline,
    string Type,
    decimal Amount,
    string Currency,
    string? Note,
    RuleSourceReference? Source);

public sealed record FinancingProfileSummary(
    decimal NetMonthlyIncome,
    decimal RentHousing,
    decimal EssentialExpenses,
    decimal OtherFixedDebt,
    decimal SavingsTarget,
    decimal DisposableIncomeBeforeVehicle,
    string Currency);

public sealed record FinancingCalculationResponse(
    string Policy,
    string PurchaseRating,
    FinancingProfileSummary Profile,
    OwnershipCalculationResponse Ownership,
    AffordabilityEvaluationResult OwnershipAffordability,
    OnRoadCalculationResponse OnRoad,
    FinancingCalculationResult Financing,
    PurchaseCashflowThresholds PurchaseThresholds,
    PurchaseCashflowResult PurchaseCashflow,
    InterestRateReference InterestRate,
    IReadOnlyList<AppliedDealerFinancingCreditResponse> AppliedDealerCredits,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CalculatedAt);

public sealed class FinancingCalculationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
