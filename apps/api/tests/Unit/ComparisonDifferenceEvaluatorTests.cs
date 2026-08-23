using VietnamCarPlatform.Domain.Compare;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class ComparisonDifferenceEvaluatorTests
{
    [Fact]
    public void UnknownAndNotAvailableRemainDifferentStates()
    {
        Assert.True(ComparisonDifferenceEvaluator.HasDifference(
        [
            new("Unknown"),
            new("NotAvailable"),
        ]));
    }

    [Fact]
    public void EqualCanonicalValuesAreNotAVisualDifference()
    {
        Assert.False(ComparisonDifferenceEvaluator.HasDifference(
        [
            new("Official", NumericValue: 4_615),
            new("Official", NumericValue: 4_615),
        ]));
    }

    [Fact]
    public void SameStateButDifferentBooleanIsDifferent()
    {
        Assert.True(ComparisonDifferenceEvaluator.HasDifference(
        [
            new("Official", BooleanValue: true),
            new("Official", BooleanValue: false),
        ]));
    }
}
