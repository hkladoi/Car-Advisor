using VietnamCarPlatform.Domain.Affordability;

namespace VietnamCarPlatform.Api.Features.Affordability;

public sealed class AffordabilityOptions
{
    public AffordabilityPolicyOptions Conservative { get; set; } = new(0.15m, 0.4m, 0.8m);
    public AffordabilityPolicyOptions Balanced { get; set; } = new(0.2m, 0.5m, 0.8m);
    public AffordabilityPolicyOptions Aggressive { get; set; } = new(0.3m, 0.7m, 0.85m);
    public WorstReasonableOptions WorstReasonable { get; set; } = new();
    public PurchaseCashflowOptions PurchaseCashflow { get; set; } = new();

    public AffordabilityPolicyThresholds Thresholds(AffordabilityPolicy policy)
    {
        var value = policy switch
        {
            AffordabilityPolicy.Conservative => Conservative,
            AffordabilityPolicy.Balanced => Balanced,
            AffordabilityPolicy.Aggressive => Aggressive,
            _ => throw new InvalidOperationException("Custom affordability thresholds are not exposed until every threshold is supplied explicitly."),
        };
        return new AffordabilityPolicyThresholds(
            value.MaximumIncomeRatio,
            value.MaximumDisposableRatio,
            value.WarningUtilization);
    }

    public PurchaseCashflowThresholds PurchaseThresholds(AffordabilityPolicy policy) =>
        PurchaseCashflow.Thresholds(policy);
}

public sealed class PurchaseCashflowOptions
{
    public PurchaseCashflowPolicyOptions Conservative { get; set; } = new(0.25m, 0.4m, 0.8m);
    public PurchaseCashflowPolicyOptions Balanced { get; set; } = new(0.35m, 0.5m, 0.8m);
    public PurchaseCashflowPolicyOptions Aggressive { get; set; } = new(0.45m, 0.65m, 0.85m);

    public PurchaseCashflowThresholds Thresholds(AffordabilityPolicy policy)
    {
        var value = policy switch
        {
            AffordabilityPolicy.Conservative => Conservative,
            AffordabilityPolicy.Balanced => Balanced,
            AffordabilityPolicy.Aggressive => Aggressive,
            _ => throw new InvalidOperationException("Custom purchase cash-flow thresholds are not exposed until every threshold is supplied explicitly."),
        };
        return new PurchaseCashflowThresholds(
            value.MaximumVehicleDebtRatio,
            value.MaximumTotalCommitmentRatio,
            value.WarningUtilization);
    }
}

public sealed class PurchaseCashflowPolicyOptions
{
    public PurchaseCashflowPolicyOptions()
    {
    }

    public PurchaseCashflowPolicyOptions(
        decimal maximumVehicleDebtRatio,
        decimal maximumTotalCommitmentRatio,
        decimal warningUtilization)
    {
        MaximumVehicleDebtRatio = maximumVehicleDebtRatio;
        MaximumTotalCommitmentRatio = maximumTotalCommitmentRatio;
        WarningUtilization = warningUtilization;
    }

    public decimal MaximumVehicleDebtRatio { get; set; }
    public decimal MaximumTotalCommitmentRatio { get; set; }
    public decimal WarningUtilization { get; set; }
}

public sealed class AffordabilityPolicyOptions
{
    public AffordabilityPolicyOptions()
    {
    }

    public AffordabilityPolicyOptions(decimal maximumIncomeRatio, decimal maximumDisposableRatio, decimal warningUtilization)
    {
        MaximumIncomeRatio = maximumIncomeRatio;
        MaximumDisposableRatio = maximumDisposableRatio;
        WarningUtilization = warningUtilization;
    }

    public decimal MaximumIncomeRatio { get; set; }
    public decimal MaximumDisposableRatio { get; set; }
    public decimal WarningUtilization { get; set; }
}

public sealed class WorstReasonableOptions
{
    public decimal EnergyFactor { get; set; } = 1.15m;
    public decimal ParkingFactor { get; set; } = 1.1m;
    public decimal MaintenanceFactor { get; set; } = 1.25m;
    public decimal TyreFactor { get; set; } = 1.15m;
}
