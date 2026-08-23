using VietnamCarPlatform.Domain.Affordability;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class FinancingCalculatorTests
{
    [Fact]
    public void AnnuityMatchesKnownFormula()
    {
        var result = FinancingCalculator.Calculate(Loan(LoanRepaymentMethod.Annuity));

        Assert.Equal(13_346_669, result.AveragePayment);
        Assert.Equal(13_346_669, result.MonthlyPaymentForCommitment);
        Assert.Equal(200_800_117, result.TotalInterest);
        Assert.Equal(800_800_117, result.TotalLoanRepayment);
    }

    [Fact]
    public void ReducingBalanceMatchesKnownFormula()
    {
        var result = FinancingCalculator.Calculate(Loan(LoanRepaymentMethod.ReducingBalance));

        Assert.Equal(16_000_000, result.FirstPayment);
        Assert.Equal(13_050_000, result.AveragePayment);
        Assert.Equal(10_100_000, result.LastPayment);
        Assert.Equal(183_000_000, result.TotalInterest);
        Assert.Equal(783_000_000, result.TotalLoanRepayment);
        Assert.Equal(result.FirstPayment, result.MonthlyPaymentForCommitment);
    }

    [Fact]
    public void FamilyFundedOutrightSkipsUserCashAndLoanGate()
    {
        var result = FinancingCalculator.Calculate(new FinancingCalculationInput(
            700_000_000, 0, 700_000_000, 0, 0, null, null, 0, 0, 0,
            PurchaseFundingSource.FamilyFunded, PurchaseMethod.Cash, LoanRepaymentMethod.Annuity));

        Assert.Equal("ExternallyFunded", result.PurchaseStatus);
        Assert.Equal("NotApplicable", result.FinancingStatus);
        Assert.Equal(0, result.UpfrontCashRequired);
        Assert.Equal(0, result.MonthlyPaymentForCommitment);
    }

    [Fact]
    public void CashPurchaseRequiresEnoughUserCash()
    {
        var result = FinancingCalculator.Calculate(new FinancingCalculationInput(
            700_000_000, 650_000_000, 0, 0, 0, null, null, 0, 0, 0,
            PurchaseFundingSource.SelfFunded, PurchaseMethod.Cash, LoanRepaymentMethod.Annuity));

        Assert.Equal("Fail", result.PurchaseStatus);
        Assert.Equal(50_000_000, result.CashShortfall);
        Assert.Equal(0, result.MonthlyPaymentForCommitment);
    }

    private static FinancingCalculationInput Loan(LoanRepaymentMethod repaymentMethod) => new(
        AcquisitionCost: 750_000_000,
        AvailableCash: 150_000_000,
        FamilyContribution: 0,
        TradeInNetValue: 0,
        OtherUpfrontCredits: 0,
        DownPaymentAmount: 150_000_000,
        DownPaymentPercent: null,
        AnnualInterestRate: 0.12m,
        TermMonths: 60,
        UpfrontFees: 0,
        FundingSource: PurchaseFundingSource.SelfFunded,
        PurchaseMethod: PurchaseMethod.Loan,
        RepaymentMethod: repaymentMethod);
}
