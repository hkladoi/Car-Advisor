namespace VietnamCarPlatform.Domain.Compare;

public sealed record ComparisonValue(
    string State,
    decimal? NumericValue = null,
    string? TextValue = null,
    bool? BooleanValue = null);

public static class ComparisonDifferenceEvaluator
{
    public static bool HasDifference(IEnumerable<ComparisonValue> values)
    {
        var materialized = values.ToArray();
        if (materialized.Length < 2)
        {
            return false;
        }
        var first = materialized[0];
        return materialized.Skip(1).Any(value => !Equivalent(first, value));
    }

    public static bool Equivalent(ComparisonValue left, ComparisonValue right) =>
        string.Equals(left.State, right.State, StringComparison.Ordinal)
        && left.NumericValue == right.NumericValue
        && string.Equals(left.TextValue, right.TextValue, StringComparison.Ordinal)
        && left.BooleanValue == right.BooleanValue;
}
