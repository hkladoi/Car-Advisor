namespace VietnamCarPlatform.Domain.Affordability;

public sealed record OperatingOwnershipCostInput(
    decimal EnergyCurrentMonthly,
    decimal EnergyNormalizedMonthly,
    decimal ParkingMonthly,
    decimal MaintenanceReserveMonthly,
    decimal CompulsoryInsuranceMonthly,
    decimal BodyInsuranceMonthly,
    decimal RoadUsageMonthly,
    decimal InspectionMonthly,
    decimal TyreReserveMonthly,
    decimal BatteryRentalMonthly,
    decimal WorstEnergyFactor,
    decimal WorstParkingFactor,
    decimal WorstMaintenanceFactor,
    decimal WorstTyreFactor);

public sealed record OwnershipCostComponent(
    string Component,
    decimal CurrentAmount,
    decimal NormalizedAmount,
    decimal WorstReasonableAmount,
    string Origin,
    string Note);

public sealed record OperatingOwnershipCostResult(
    decimal CurrentMonthlyCost,
    decimal NormalizedMonthlyCost,
    decimal WorstReasonableMonthlyCost,
    IReadOnlyList<OwnershipCostComponent> Breakdown);

public static class OperatingOwnershipCostEvaluator
{
    public static OperatingOwnershipCostResult Evaluate(OperatingOwnershipCostInput input)
    {
        var amounts = new[]
        {
            input.EnergyCurrentMonthly,
            input.EnergyNormalizedMonthly,
            input.ParkingMonthly,
            input.MaintenanceReserveMonthly,
            input.CompulsoryInsuranceMonthly,
            input.BodyInsuranceMonthly,
            input.RoadUsageMonthly,
            input.InspectionMonthly,
            input.TyreReserveMonthly,
            input.BatteryRentalMonthly,
        };
        if (amounts.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Monthly ownership inputs cannot be negative.");
        }
        if (new[] { input.WorstEnergyFactor, input.WorstParkingFactor, input.WorstMaintenanceFactor, input.WorstTyreFactor }
            .Any(value => value < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Worst-reasonable factors must be at least 1.0.");
        }

        var components = new[]
        {
            Component("Energy", input.EnergyCurrentMonthly, input.EnergyNormalizedMonthly, input.EnergyNormalizedMonthly * input.WorstEnergyFactor, "SourcedCalculation", "Energy Engine; current may include an effective promotion while normalized excludes temporary promotions."),
            Component("Parking", input.ParkingMonthly, input.ParkingMonthly, input.ParkingMonthly * input.WorstParkingFactor, "UserAssumption", "Editable monthly parking assumption."),
            Component("MaintenanceReserve", input.MaintenanceReserveMonthly, input.MaintenanceReserveMonthly, input.MaintenanceReserveMonthly * input.WorstMaintenanceFactor, "UserEstimate", "Editable reserve; not presented as a manufacturer maintenance quote."),
            Component("CompulsoryInsurance", input.CompulsoryInsuranceMonthly, input.CompulsoryInsuranceMonthly, input.CompulsoryInsuranceMonthly, "SourcedRuleOrOverride", "Effective legal annual amount divided by 12 unless explicitly overridden."),
            Component("BodyInsurance", input.BodyInsuranceMonthly, input.BodyInsuranceMonthly, input.BodyInsuranceMonthly, "UserAssumption", "Optional annual quote or budget divided by 12."),
            Component("RoadUsage", input.RoadUsageMonthly, input.RoadUsageMonthly, input.RoadUsageMonthly, "SourcedRuleOrOverride", "Effective 12-month road-usage rule divided by 12 unless explicitly overridden."),
            Component("Inspection", input.InspectionMonthly, input.InspectionMonthly, input.InspectionMonthly, "SourcedRuleOrOverride", "Effective inspection rule annualized; a sourced exemption remains zero for its applicable period."),
            Component("TyreReserve", input.TyreReserveMonthly, input.TyreReserveMonthly, input.TyreReserveMonthly * input.WorstTyreFactor, "UserEstimate", "Editable reserve; not presented as a tyre-life forecast."),
            Component("BatteryRental", input.BatteryRentalMonthly, input.BatteryRentalMonthly, input.BatteryRentalMonthly, "UserAssumption", "Only non-zero when a documented model policy or explicit user amount applies."),
        };

        return new OperatingOwnershipCostResult(
            components.Sum(value => value.CurrentAmount),
            components.Sum(value => value.NormalizedAmount),
            components.Sum(value => value.WorstReasonableAmount),
            components);
    }

    private static OwnershipCostComponent Component(
        string name,
        decimal current,
        decimal normalized,
        decimal worst,
        string origin,
        string note) => new(
            name,
            Round(current),
            Round(normalized),
            Round(worst),
            origin,
            note);

    private static decimal Round(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
