using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Affordability;

public enum AffordabilityPolicy
{
    Conservative,
    Balanced,
    Aggressive,
    Custom,
}

public enum PurchaseFundingSource
{
    SelfFunded,
    FamilyFunded,
    Mixed,
}

public enum PurchaseMethod
{
    Cash,
    Loan,
}

public enum LoanRepaymentMethod
{
    Annuity,
    ReducingBalance,
}

public sealed class AffordabilityProfile : Entity
{
    public string? OwnerSubjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal NetMonthlyIncome { get; set; }
    public decimal RentHousing { get; set; }
    public decimal EssentialExpenses { get; set; }
    public decimal OtherFixedDebt { get; set; }
    public decimal SavingsTarget { get; set; }
    public decimal MonthlyKilometres { get; set; }
    public decimal ParkingMonthly { get; set; }
    public decimal HouseholdBaseKwh { get; set; }
    public string RegionCode { get; set; } = string.Empty;
    public AffordabilityPolicy Policy { get; set; } = AffordabilityPolicy.Balanced;
    public string AssumptionsJson { get; set; } = "{}";
}

public sealed class FinancingScenario : Entity
{
    public Guid? AffordabilityProfileId { get; set; }
    public Guid TrimId { get; set; }
    public PurchaseFundingSource FundingSource { get; set; } = PurchaseFundingSource.SelfFunded;
    public PurchaseMethod PurchaseMethod { get; set; } = PurchaseMethod.Loan;
    public LoanRepaymentMethod RepaymentMethod { get; set; } = LoanRepaymentMethod.Annuity;
    public decimal AvailableCash { get; set; }
    public decimal DownPayment { get; set; }
    public decimal Principal { get; set; }
    public decimal AnnualInterestRate { get; set; }
    public int TermMonths { get; set; }
    public decimal OriginationFees { get; set; }
    public string? DealerFinancingConditionsJson { get; set; }
}
