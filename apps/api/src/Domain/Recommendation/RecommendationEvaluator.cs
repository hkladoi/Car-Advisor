namespace VietnamCarPlatform.Domain.Recommendation;

public enum RecommendationDirection
{
    HigherIsBetter,
    LowerIsBetter,
}

public static class RecommendationComponentCodes
{
    public const string Value = "value";
    public const string RunningCost = "running_cost";
    public const string Space = "space";
    public const string SafetyAdas = "safety_adas";
    public const string Comfort = "comfort";
    public const string Performance = "performance";
    public const string Technology = "technology";

    public static readonly IReadOnlyList<string> All =
    [
        Value,
        RunningCost,
        Space,
        SafetyAdas,
        Comfort,
        Performance,
        Technology,
    ];
}

public sealed record RecommendationMetricInput(
    string Code,
    string Label,
    decimal Value,
    string Unit,
    RecommendationDirection Direction);

public sealed record RecommendationComponentInput(
    string Code,
    string Label,
    IReadOnlyList<RecommendationMetricInput> Metrics,
    IReadOnlyList<Guid> SourceFactIds,
    bool Trusted,
    string? MissingReason);

public sealed record RecommendationCandidateInput(
    Guid TrimId,
    bool HardFilterMatched,
    IReadOnlyList<string> HardFilterReasons,
    IReadOnlyList<RecommendationComponentInput> Components);

public sealed record RecommendationComponentScore(
    string Code,
    string Label,
    decimal Weight,
    IReadOnlyList<RecommendationMetricInput> RawMetrics,
    decimal? Score,
    bool IncludedInOverall,
    bool Trusted,
    IReadOnlyList<Guid> SourceFactIds,
    string Explanation);

public sealed record RecommendationCandidateScore(
    Guid TrimId,
    int? Rank,
    decimal Completeness,
    bool CompletenessPassed,
    bool TrustPassed,
    decimal? OverallScore,
    decimal? PricePerformanceScore,
    IReadOnlyList<RecommendationComponentScore> Components,
    IReadOnlyList<string> Reasons);

public sealed record RecommendationEvaluation(
    IReadOnlyList<RecommendationCandidateScore> Ranked,
    IReadOnlyList<RecommendationCandidateScore> DataWithheld,
    IReadOnlyList<RecommendationCandidateScore> HardFilterExcluded);

/// <summary>
/// Deterministic recommendation engine. It intentionally has no AI/LLM dependency:
/// hard filters run first, completeness and provenance gates run second, and only
/// then are peer-normalised component scores and the weighted total calculated.
/// </summary>
public static class RecommendationEvaluator
{
    public const decimal PublicCompletenessThreshold = 0.80m;

    public static RecommendationEvaluation Evaluate(
        IReadOnlyList<RecommendationCandidateInput> candidates,
        IReadOnlyDictionary<string, decimal> requestedWeights,
        decimal completenessThreshold = PublicCompletenessThreshold)
    {
        if (completenessThreshold is < PublicCompletenessThreshold or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(completenessThreshold));
        }

        var weights = NormalizeWeights(requestedWeights);
        var hardExcluded = candidates
            .Where(candidate => !candidate.HardFilterMatched)
            .Select(candidate => Withheld(candidate, weights, completenessThreshold, candidate.HardFilterReasons))
            .ToArray();
        var hardMatched = candidates.Where(candidate => candidate.HardFilterMatched).ToArray();
        var preliminary = hardMatched.Select(candidate => Preliminary(candidate, weights, completenessThreshold)).ToArray();
        var scoreableIds = preliminary
            .Where(candidate => candidate.CompletenessPassed && candidate.TrustPassed)
            .Select(candidate => candidate.TrimId)
            .ToHashSet();

        var scoreableInputs = hardMatched.Where(candidate => scoreableIds.Contains(candidate.TrimId)).ToArray();
        var scored = scoreableInputs
            .Select(candidate => Score(candidate, scoreableInputs, weights, completenessThreshold))
            .OrderByDescending(candidate => candidate.OverallScore)
            .ThenBy(candidate => candidate.TrimId)
            .Select((candidate, index) => candidate with { Rank = index + 1 })
            .ToArray();
        var withheld = preliminary.Where(candidate => !scoreableIds.Contains(candidate.TrimId)).ToArray();

        return new RecommendationEvaluation(scored, withheld, hardExcluded);
    }

    public static IReadOnlyDictionary<string, decimal> NormalizeWeights(IReadOnlyDictionary<string, decimal> requested)
    {
        var unexpected = requested.Keys.Except(RecommendationComponentCodes.All, StringComparer.Ordinal).ToArray();
        if (unexpected.Length > 0 || RecommendationComponentCodes.All.Any(code => !requested.ContainsKey(code)))
        {
            throw new ArgumentException("Weights must contain exactly the seven canonical recommendation components.", nameof(requested));
        }
        if (requested.Values.Any(weight => weight < 0) || requested.Values.Sum() <= 0)
        {
            throw new ArgumentException("Weights must be non-negative and at least one weight must be positive.", nameof(requested));
        }

        var total = requested.Values.Sum();
        return RecommendationComponentCodes.All.ToDictionary(
            code => code,
            code => requested[code] / total,
            StringComparer.Ordinal);
    }

    private static RecommendationCandidateScore Preliminary(
        RecommendationCandidateInput candidate,
        IReadOnlyDictionary<string, decimal> weights,
        decimal threshold)
    {
        var components = Components(candidate, weights, score: null);
        var available = components.Count(component => component.RawMetrics.Count > 0);
        var completeness = available / (decimal)RecommendationComponentCodes.All.Count;
        var trustPassed = components.Where(component => component.RawMetrics.Count > 0).All(component => component.Trusted);
        var missing = components.Where(component => component.RawMetrics.Count == 0).Select(component => $"MISSING_{component.Code.ToUpperInvariant()}");
        var weak = components.Where(component => component.RawMetrics.Count > 0 && !component.Trusted).Select(component => $"WEAK_SOURCE_{component.Code.ToUpperInvariant()}");
        var reasons = missing.Concat(weak).ToList();
        if (completeness < threshold)
        {
            reasons.Insert(0, $"COMPLETENESS_BELOW_{decimal.Round(threshold * 100, 0):0}_PERCENT");
        }

        return new RecommendationCandidateScore(
            candidate.TrimId,
            null,
            decimal.Round(completeness, 4),
            completeness >= threshold,
            trustPassed,
            null,
            null,
            components,
            reasons);
    }

    private static RecommendationCandidateScore Withheld(
        RecommendationCandidateInput candidate,
        IReadOnlyDictionary<string, decimal> weights,
        decimal threshold,
        IReadOnlyList<string> reasons)
    {
        var preliminary = Preliminary(candidate, weights, threshold);
        return preliminary with { Reasons = reasons };
    }

    private static RecommendationCandidateScore Score(
        RecommendationCandidateInput candidate,
        IReadOnlyList<RecommendationCandidateInput> peerSet,
        IReadOnlyDictionary<string, decimal> weights,
        decimal threshold)
    {
        var componentScores = candidate.Components.Select(component =>
        {
            if (component.Metrics.Count == 0)
            {
                return ToScore(component, weights[component.Code], null);
            }
            var metricScores = component.Metrics.Select(metric => NormalizeMetric(metric, component.Code, peerSet)).ToArray();
            return ToScore(component, weights[component.Code], decimal.Round(metricScores.Average(), 2));
        }).OrderBy(component => ComponentOrder(component.Code)).ToArray();
        var appliedWeight = componentScores.Where(component => component.Score is not null).Sum(component => component.Weight);
        decimal? overall = appliedWeight == 0
            ? null
            : decimal.Round(componentScores.Where(component => component.Score is not null)
                .Sum(component => component.Score!.Value * component.Weight) / appliedWeight, 2);
        var value = componentScores.Single(component => component.Code == RecommendationComponentCodes.Value).Score;
        var performance = componentScores.Single(component => component.Code == RecommendationComponentCodes.Performance).Score;
        decimal? pricePerformance = value is not null && performance is not null
            ? decimal.Round((value.Value * 0.40m) + (performance.Value * 0.60m), 2)
            : null;
        var preliminary = Preliminary(candidate, weights, threshold);
        return preliminary with
        {
            OverallScore = overall,
            PricePerformanceScore = pricePerformance,
            Components = componentScores,
            Reasons = [],
        };
    }

    private static decimal NormalizeMetric(
        RecommendationMetricInput metric,
        string componentCode,
        IReadOnlyList<RecommendationCandidateInput> peers)
    {
        var values = peers
            .SelectMany(candidate => candidate.Components)
            .Where(component => component.Code == componentCode)
            .SelectMany(component => component.Metrics)
            .Where(value => value.Code == metric.Code)
            .Select(value => value.Value)
            .ToArray();
        if (values.Length == 0 || values.Max() == values.Min())
        {
            return 50m;
        }
        var ascending = (metric.Value - values.Min()) / (values.Max() - values.Min()) * 100m;
        return metric.Direction == RecommendationDirection.HigherIsBetter ? ascending : 100m - ascending;
    }

    private static RecommendationComponentScore[] Components(
        RecommendationCandidateInput candidate,
        IReadOnlyDictionary<string, decimal> weights,
        decimal? score) => candidate.Components
        .OrderBy(component => ComponentOrder(component.Code))
        .Select(component => ToScore(component, weights[component.Code], score))
        .ToArray();

    private static RecommendationComponentScore ToScore(
        RecommendationComponentInput component,
        decimal weight,
        decimal? score) => new(
            component.Code,
            component.Label,
            decimal.Round(weight, 6),
            component.Metrics,
            score,
            score is not null,
            component.Trusted,
            component.SourceFactIds,
            component.MissingReason ?? (score is null ? "Component passed the data gate and will be normalised only inside the scoreable peer set." : "Min-max normalised against the same hard-filtered, gate-passing peer set."));

    private static int ComponentOrder(string code)
    {
        for (var index = 0; index < RecommendationComponentCodes.All.Count; index++)
        {
            if (RecommendationComponentCodes.All[index] == code) return index;
        }
        return int.MaxValue;
    }
}
