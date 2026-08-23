using VietnamCarPlatform.Domain.Affordability;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class PurchaseCashflowEvaluatorTests
{
    [Fact]
    public void CombinesNormalizedOwnershipAndFinancingWithoutDoubleCountingExistingDebt()
    {
        var result = PurchaseCashflowEvaluator.Evaluate(new PurchaseCashflowInput(
            NetMonthlyIncome: 50_000_000,
            RentHousing: 8_000_000,
            EssentialExpenses: 10_000_000,
            OtherFixedDebt: 5_000_000,
            SavingsTarget: 3_000_000,
            NormalizedOperatingOwnershipCost: 4_000_000,
            FinancingPayment: 12_000_000,
            Thresholds: new PurchaseCashflowThresholds(0.35m, 0.5m, 0.8m)));

        Assert.Equal(0.24m, result.VehicleDebtRatio);
        Assert.Equal(0.34m, result.TotalDebtRatio);
        Assert.Equal(16_000_000, result.TotalMonthlyVehicleCommitment);
        Assert.Equal(0.32m, result.TotalCommitmentRatio);
        Assert.Equal(8_000_000, result.PostPaymentDisposable);
        Assert.Equal("Pass", result.Rating);
    }

    [Fact]
    public void FailsWhenFirstReducingBalancePaymentBreachesDebtPolicy()
    {
        var result = PurchaseCashflowEvaluator.Evaluate(new PurchaseCashflowInput(
            40_000_000,
            5_000_000,
            8_000_000,
            0,
            2_000_000,
            3_000_000,
            16_000_000,
            new PurchaseCashflowThresholds(0.35m, 0.5m, 0.8m)));

        Assert.Equal("Fail", result.Rating);
        Assert.Contains("VEHICLE_DEBT_RATIO_EXCEEDED", result.Reasons);
    }
}
