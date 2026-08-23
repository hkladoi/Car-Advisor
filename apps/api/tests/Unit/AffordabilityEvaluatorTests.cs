using VietnamCarPlatform.Domain.Affordability;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class AffordabilityEvaluatorTests
{
    [Fact]
    public void OwnershipExcludesLoanAndKeepsCurrentNormalizedWorstBands()
    {
        var result = OperatingOwnershipCostEvaluator.Evaluate(new OperatingOwnershipCostInput(
            EnergyCurrentMonthly: 0,
            EnergyNormalizedMonthly: 557_781,
            ParkingMonthly: 1_200_000,
            MaintenanceReserveMonthly: 1_000_000,
            CompulsoryInsuranceMonthly: 40_058,
            BodyInsuranceMonthly: 0,
            RoadUsageMonthly: 130_000,
            InspectionMonthly: 0,
            TyreReserveMonthly: 300_000,
            BatteryRentalMonthly: 0,
            WorstEnergyFactor: 1.15m,
            WorstParkingFactor: 1.1m,
            WorstMaintenanceFactor: 1.25m,
            WorstTyreFactor: 1.15m));

        Assert.Equal(2_670_058, result.CurrentMonthlyCost);
        Assert.Equal(3_227_839, result.NormalizedMonthlyCost);
        Assert.Equal(3_726_506, result.WorstReasonableMonthlyCost);
        Assert.DoesNotContain(result.Breakdown, component => component.Component == "LoanPayment");
    }

    [Fact]
    public void RentAndParkingCanChangeNormalizedEligibilityForTheSameIncome()
    {
        var ownership = OperatingOwnershipCostEvaluator.Evaluate(new OperatingOwnershipCostInput(
            513_409, 513_409, 500_000, 800_000, 40_058, 0, 130_000, 0, 250_000, 0, 1.15m, 1.1m, 1.25m, 1.15m));
        var thresholds = new AffordabilityPolicyThresholds(0.2m, 0.5m, 0.8m);
        var lowFixedCosts = AffordabilityEvaluator.Evaluate(new AffordabilityEvaluationInput(
            20_000_000, 2_000_000, 6_000_000, 0, 2_000_000, null, thresholds, ownership));
        var highFixedCosts = AffordabilityEvaluator.Evaluate(new AffordabilityEvaluationInput(
            20_000_000, 8_000_000, 6_000_000, 0, 2_000_000, null, thresholds, ownership));

        Assert.True(lowFixedCosts.Eligible);
        Assert.False(highFixedCosts.Eligible);
        Assert.Contains("DISPOSABLE_RATIO_EXCEEDED", highFixedCosts.Reasons);
    }

    [Fact]
    public void TemporaryEnergyPromotionNeverReplacesNormalizedCost()
    {
        var ownership = OperatingOwnershipCostEvaluator.Evaluate(new OperatingOwnershipCostInput(
            0, 557_781, 0, 0, 0, 0, 0, 0, 0, 0, 1.15m, 1.1m, 1.25m, 1.15m));
        var result = AffordabilityEvaluator.Evaluate(new AffordabilityEvaluationInput(
            10_000_000,
            0,
            9_000_000,
            0,
            0,
            null,
            new AffordabilityPolicyThresholds(0.2m, 0.5m, 0.8m),
            ownership));

        Assert.True(result.Current.Eligible);
        Assert.False(result.Normalized.Eligible);
        Assert.False(result.Eligible);
        Assert.Contains("NORMALIZED_COST_FAILS", result.Reasons);
        Assert.Contains("CURRENT_ENERGY_PROMOTION_APPLIED", result.Reasons);
    }
}
