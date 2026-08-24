namespace VietnamCarPlatform.Domain.Catalog;

public sealed record RealWorldFuelSelection(bool HasLiquidFuel, string? CohortFuelType);

public static class RealWorldConsumptionSelectionPolicy
{
    public static IReadOnlyList<RealWorldConsumptionAggregate> LatestCohorts(
        IEnumerable<RealWorldConsumptionAggregate> candidates,
        RealWorldFuelSelection? fuelSelection = null)
    {
        if (fuelSelection is { HasLiquidFuel: false })
        {
            return [];
        }

        var rows = candidates.ToList();
        if (!string.IsNullOrWhiteSpace(fuelSelection?.CohortFuelType))
        {
            rows = rows
                .Where(value => value.FuelType.Equals(
                    fuelSelection.CohortFuelType,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (rows.Count == 0)
        {
            return [];
        }

        var latestRegistrationYear = rows.Max(value => value.VehicleRegistrationYear);
        return rows
            .Where(value => value.VehicleRegistrationYear == latestRegistrationYear)
            .GroupBy(value => value.FuelType, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(value => value.SampleSize)
                .ThenBy(value => value.Manufacturer, StringComparer.Ordinal)
                .First())
            .OrderBy(value => value.FuelType, StringComparer.Ordinal)
            .ToList();
    }

    public static RealWorldFuelSelection ResolveFuel(
        string powertrainType,
        string? powertrainFuelType,
        string? recommendedFuel)
    {
        var powertrain = powertrainType.Trim().ToUpperInvariant();
        if (powertrain == "BEV")
        {
            return new RealWorldFuelSelection(false, null);
        }

        var declaredFuel = new[] { powertrainFuelType, recommendedFuel }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (declaredFuel is null)
        {
            return new RealWorldFuelSelection(true, null);
        }

        var normalized = new string(declaredFuel
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        var baseFuel = normalized.Contains("DIESEL", StringComparison.Ordinal)
                       || normalized.StartsWith("DO", StringComparison.Ordinal)
            ? "DIESEL"
            : normalized.Contains("PETROL", StringComparison.Ordinal)
              || normalized.Contains("GASOLINE", StringComparison.Ordinal)
              || normalized.Contains("RON", StringComparison.Ordinal)
              || normalized.StartsWith("E5", StringComparison.Ordinal)
              || normalized.StartsWith("E10", StringComparison.Ordinal)
                ? "PETROL"
                : null;
        if (baseFuel is null)
        {
            return new RealWorldFuelSelection(true, null);
        }

        return new RealWorldFuelSelection(
            true,
            powertrain is "HEV" or "PHEV" or "EREV" ? $"{baseFuel}/ELECTRIC" : baseFuel);
    }
}
