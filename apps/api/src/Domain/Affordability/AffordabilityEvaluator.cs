namespace VietnamCarPlatform.Domain.Affordability;

public sealed record AffordabilityPolicyThresholds(
    decimal MaximumIncomeRatio,
    decimal MaximumDisposableRatio,
    decimal WarningUtilization);

public sealed record AffordabilityEvaluationInput(
    decimal NetMonthlyIncome,
    decimal RentHousing,
    decimal EssentialExpenses,
    decimal OtherFixedDebt,
    decimal SavingsTarget,
    decimal? MaximumMonthlyVehicleSpend,
    AffordabilityPolicyThresholds Thresholds,
    OperatingOwnershipCostResult Ownership);

public sealed record AffordabilityCostBand(
    string Band,
    decimal MonthlyVehicleCashflow,
    decimal IncomeRatio,
    decimal DisposableRatio,
    bool Eligible,
    string Rating,
    IReadOnlyList<string> Reasons);

public sealed record AffordabilityEvaluationResult(
    bool Eligible,
    string Rating,
    decimal DisposableIncome,
    AffordabilityCostBand Current,
    AffordabilityCostBand Normalized,
    AffordabilityCostBand WorstReasonable,
    IReadOnlyList<string> Reasons);

public static class AffordabilityEvaluator
{
    public static AffordabilityEvaluationResult Evaluate(AffordabilityEvaluationInput input)
    {
        if (input.NetMonthlyIncome <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Net monthly income must be greater than zero.");
        }
        if (new decimal?[] { input.RentHousing, input.EssentialExpenses, input.OtherFixedDebt, input.SavingsTarget, input.MaximumMonthlyVehicleSpend }.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Household cash-flow inputs cannot be negative.");
        }
        if (input.Thresholds.MaximumIncomeRatio <= 0
            || input.Thresholds.MaximumDisposableRatio <= 0
            || input.Thresholds.WarningUtilization is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Affordability policy thresholds are invalid.");
        }

        var disposable = input.NetMonthlyIncome
            - input.EssentialExpenses
            - input.RentHousing
            - input.OtherFixedDebt
            - input.SavingsTarget;
        var current = Band("Current", input.Ownership.CurrentMonthlyCost, input, disposable);
        var normalized = Band("Normalized", input.Ownership.NormalizedMonthlyCost, input, disposable);
        var worst = Band("WorstReasonable", input.Ownership.WorstReasonableMonthlyCost, input, disposable);
        var reasons = normalized.Reasons.ToList();

        if (current.Eligible && !normalized.Eligible)
        {
            reasons.Add("NORMALIZED_COST_FAILS");
        }
        if (normalized.Eligible && !worst.Eligible)
        {
            reasons.Add("WORST_REASONABLE_COST_EXCEEDS_POLICY");
        }

        var normalizedCost = Math.Max(1, input.Ownership.NormalizedMonthlyCost);
        var energy = input.Ownership.Breakdown.Single(value => value.Component == "Energy").NormalizedAmount;
        var parking = input.Ownership.Breakdown.Single(value => value.Component == "Parking").NormalizedAmount;
        if (energy / normalizedCost >= 0.4m)
        {
            reasons.Add("ENERGY_COST_HIGH");
        }
        if (parking / normalizedCost >= 0.35m)
        {
            reasons.Add("PARKING_DOMINATES");
        }
        if (input.Ownership.CurrentMonthlyCost < input.Ownership.NormalizedMonthlyCost)
        {
            reasons.Add("CURRENT_ENERGY_PROMOTION_APPLIED");
        }

        return new AffordabilityEvaluationResult(
            normalized.Eligible,
            normalized.Rating,
            disposable,
            current,
            normalized,
            worst,
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static AffordabilityCostBand Band(
        string name,
        decimal monthlyCost,
        AffordabilityEvaluationInput input,
        decimal disposable)
    {
        var incomeRatio = monthlyCost / input.NetMonthlyIncome;
        var disposableRatio = monthlyCost / Math.Max(disposable, 1);
        var reasons = new List<string>();
        if (disposable <= 0)
        {
            reasons.Add("LOW_DISPOSABLE_INCOME");
        }
        if (incomeRatio > input.Thresholds.MaximumIncomeRatio)
        {
            reasons.Add("INCOME_RATIO_EXCEEDED");
        }
        if (disposableRatio > input.Thresholds.MaximumDisposableRatio)
        {
            reasons.Add("DISPOSABLE_RATIO_EXCEEDED");
        }
        if (input.MaximumMonthlyVehicleSpend is not null && monthlyCost > input.MaximumMonthlyVehicleSpend.Value)
        {
            reasons.Add("MAX_MONTHLY_VEHICLE_SPEND_EXCEEDED");
        }

        var eligible = reasons.Count == 0;
        var utilization = Math.Max(
            incomeRatio / input.Thresholds.MaximumIncomeRatio,
            disposableRatio / input.Thresholds.MaximumDisposableRatio);
        var rating = !eligible
            ? "OverBudget"
            : utilization >= input.Thresholds.WarningUtilization ? "Watch" : "Comfortable";
        return new AffordabilityCostBand(
            name,
            monthlyCost,
            decimal.Round(incomeRatio, 6),
            decimal.Round(disposableRatio, 6),
            eligible,
            rating,
            reasons);
    }
}
