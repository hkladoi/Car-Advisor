namespace VietnamCarPlatform.Domain.Affordability;

public sealed record PurchaseCashflowThresholds(
    decimal MaximumVehicleDebtRatio,
    decimal MaximumTotalCommitmentRatio,
    decimal WarningUtilization);

public sealed record PurchaseCashflowInput(
    decimal NetMonthlyIncome,
    decimal RentHousing,
    decimal EssentialExpenses,
    decimal OtherFixedDebt,
    decimal SavingsTarget,
    decimal NormalizedOperatingOwnershipCost,
    decimal FinancingPayment,
    PurchaseCashflowThresholds Thresholds);

public sealed record PurchaseCashflowResult(
    decimal VehicleDebtRatio,
    decimal TotalDebtRatio,
    decimal TotalMonthlyVehicleCommitment,
    decimal TotalCommitmentRatio,
    decimal PostPaymentDisposable,
    string Rating,
    IReadOnlyList<string> Reasons);

public static class PurchaseCashflowEvaluator
{
    public static PurchaseCashflowResult Evaluate(PurchaseCashflowInput input)
    {
        if (input.NetMonthlyIncome <= 0
            || input.Thresholds.MaximumVehicleDebtRatio <= 0
            || input.Thresholds.MaximumTotalCommitmentRatio <= 0
            || input.Thresholds.WarningUtilization is <= 0 or >= 1
            || new[]
            {
                input.RentHousing,
                input.EssentialExpenses,
                input.OtherFixedDebt,
                input.SavingsTarget,
                input.NormalizedOperatingOwnershipCost,
                input.FinancingPayment,
            }.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Purchase cash-flow inputs are invalid.");
        }

        var vehicleDebtRatio = input.FinancingPayment / input.NetMonthlyIncome;
        var totalDebtRatio = (input.FinancingPayment + input.OtherFixedDebt) / input.NetMonthlyIncome;
        var vehicleCommitment = input.NormalizedOperatingOwnershipCost + input.FinancingPayment;
        var commitmentRatio = vehicleCommitment / input.NetMonthlyIncome;
        var postPaymentDisposable = input.NetMonthlyIncome
            - input.RentHousing
            - input.EssentialExpenses
            - input.OtherFixedDebt
            - input.SavingsTarget
            - vehicleCommitment;
        var reasons = new List<string>();
        if (vehicleDebtRatio > input.Thresholds.MaximumVehicleDebtRatio)
        {
            reasons.Add("VEHICLE_DEBT_RATIO_EXCEEDED");
        }
        if (commitmentRatio > input.Thresholds.MaximumTotalCommitmentRatio)
        {
            reasons.Add("TOTAL_COMMITMENT_RATIO_EXCEEDED");
        }
        if (postPaymentDisposable < 0)
        {
            reasons.Add("POST_PAYMENT_DISPOSABLE_NEGATIVE");
        }

        var utilization = Math.Max(
            vehicleDebtRatio / input.Thresholds.MaximumVehicleDebtRatio,
            commitmentRatio / input.Thresholds.MaximumTotalCommitmentRatio);
        var rating = reasons.Count > 0
            ? "Fail"
            : utilization >= input.Thresholds.WarningUtilization ? "Warn" : "Pass";
        return new PurchaseCashflowResult(
            decimal.Round(vehicleDebtRatio, 6),
            decimal.Round(totalDebtRatio, 6),
            decimal.Round(vehicleCommitment, 0, MidpointRounding.AwayFromZero),
            decimal.Round(commitmentRatio, 6),
            decimal.Round(postPaymentDisposable, 0, MidpointRounding.AwayFromZero),
            rating,
            reasons);
    }
}
