using VietnamCarPlatform.Domain.Recommendation;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class RecommendationEvaluatorTests
{
    [Fact]
    public void HardFiltersRunBeforeScoringAndCannotBeOverriddenByPerformance()
    {
        var included = Candidate("10000000-0000-0000-0000-000000000001", price: 900, performance: 100);
        var filtered = Candidate("10000000-0000-0000-0000-000000000002", price: 500, performance: 1_000) with
        {
            HardFilterMatched = false,
            HardFilterReasons = ["HARD_FILTER_BODY_TYPE"],
        };

        var result = RecommendationEvaluator.Evaluate([included, filtered], Weights());

        Assert.Single(result.Ranked);
        Assert.Equal(included.TrimId, result.Ranked[0].TrimId);
        Assert.Single(result.HardFilterExcluded);
        Assert.Equal("HARD_FILTER_BODY_TYPE", result.HardFilterExcluded[0].Reasons.Single());
        Assert.Null(result.HardFilterExcluded[0].OverallScore);
    }

    [Fact]
    public void CompleteTrustedCandidatesExposeEveryComponentAndReproducibleScores()
    {
        var efficient = Candidate("20000000-0000-0000-0000-000000000001", price: 700, performance: 110, running: 90);
        var powerful = Candidate("20000000-0000-0000-0000-000000000002", price: 900, performance: 180, running: 130);
        var weights = Weights(value: 40, performance: 10, running: 25);

        var first = RecommendationEvaluator.Evaluate([efficient, powerful], weights);
        var second = RecommendationEvaluator.Evaluate([efficient, powerful], weights);

        Assert.Equal(first.Ranked.Select(value => value.TrimId), second.Ranked.Select(value => value.TrimId));
        Assert.Equal(efficient.TrimId, first.Ranked[0].TrimId);
        Assert.All(first.Ranked, candidate =>
        {
            Assert.Equal(7, candidate.Components.Count);
            Assert.Equal(1m, candidate.Completeness);
            Assert.True(candidate.TrustPassed);
            Assert.NotNull(candidate.OverallScore);
            Assert.NotNull(candidate.PricePerformanceScore);
            Assert.All(candidate.Components, component => Assert.NotEmpty(component.RawMetrics));
        });
    }

    [Fact]
    public void CompletenessAndSourceTrustGateWithholdScoresInsteadOfInventingZeros()
    {
        var incomplete = Candidate("30000000-0000-0000-0000-000000000001", price: 700, performance: 110) with
        {
            Components = Candidate("30000000-0000-0000-0000-000000000001", price: 700, performance: 110)
                .Components.Take(5).ToArray(),
        };
        var weak = Candidate("30000000-0000-0000-0000-000000000002", price: 800, performance: 120);
        weak = weak with
        {
            Components = weak.Components.Select((component, index) => index == 0 ? component with { Trusted = false } : component).ToArray(),
        };

        var result = RecommendationEvaluator.Evaluate([incomplete, weak], Weights());

        Assert.Empty(result.Ranked);
        Assert.Equal(2, result.DataWithheld.Count);
        Assert.All(result.DataWithheld, candidate => Assert.Null(candidate.OverallScore));
        Assert.Contains(result.DataWithheld.Single(value => value.TrimId == incomplete.TrimId).Reasons, reason => reason.StartsWith("COMPLETENESS_BELOW_", StringComparison.Ordinal));
        Assert.Contains("WEAK_SOURCE_VALUE", result.DataWithheld.Single(value => value.TrimId == weak.TrimId).Reasons);
    }

    [Fact]
    public void WeightNormalizationRejectsMissingNegativeAndAllZeroInputs()
    {
        Assert.Throws<ArgumentException>(() => RecommendationEvaluator.NormalizeWeights(new Dictionary<string, decimal>()));
        Assert.Throws<ArgumentException>(() => RecommendationEvaluator.NormalizeWeights(Weights(value: -1)));
        Assert.Throws<ArgumentException>(() => RecommendationEvaluator.NormalizeWeights(Weights(all: 0)));
        Assert.Equal(1m, RecommendationEvaluator.NormalizeWeights(Weights()).Values.Sum());
    }

    private static RecommendationCandidateInput Candidate(
        string id,
        decimal price,
        decimal performance,
        decimal running = 100)
    {
        var values = new Dictionary<string, (decimal value, RecommendationDirection direction)>
        {
            [RecommendationComponentCodes.Value] = (price, RecommendationDirection.LowerIsBetter),
            [RecommendationComponentCodes.RunningCost] = (running, RecommendationDirection.LowerIsBetter),
            [RecommendationComponentCodes.Space] = (10, RecommendationDirection.HigherIsBetter),
            [RecommendationComponentCodes.SafetyAdas] = (5, RecommendationDirection.HigherIsBetter),
            [RecommendationComponentCodes.Comfort] = (4, RecommendationDirection.HigherIsBetter),
            [RecommendationComponentCodes.Performance] = (performance, RecommendationDirection.HigherIsBetter),
            [RecommendationComponentCodes.Technology] = (3, RecommendationDirection.HigherIsBetter),
        };
        var trimId = Guid.Parse(id);
        return new RecommendationCandidateInput(
            trimId,
            true,
            [],
            RecommendationComponentCodes.All.Select(code => new RecommendationComponentInput(
                code,
                code,
                [new RecommendationMetricInput(code, code, values[code].value, "unit", values[code].direction)],
                [trimId],
                true,
                null)).ToArray());
    }

    private static Dictionary<string, decimal> Weights(
        decimal? value = null,
        decimal? running = null,
        decimal? performance = null,
        decimal? all = null) => new(StringComparer.Ordinal)
        {
            [RecommendationComponentCodes.Value] = all ?? value ?? 20,
            [RecommendationComponentCodes.RunningCost] = all ?? running ?? 15,
            [RecommendationComponentCodes.Space] = all ?? 15,
            [RecommendationComponentCodes.SafetyAdas] = all ?? 20,
            [RecommendationComponentCodes.Comfort] = all ?? 10,
            [RecommendationComponentCodes.Performance] = all ?? performance ?? 10,
            [RecommendationComponentCodes.Technology] = all ?? 10,
        };
}
