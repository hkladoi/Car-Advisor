namespace VietnamCarPlatform.Api.Features.Recommendation;

public sealed class RecommendationHardFiltersRequest
{
    public decimal? MaximumPrice { get; init; }
    public IReadOnlyList<string> BodyTypes { get; init; } = [];
    public IReadOnlyList<string> Segments { get; init; } = [];
    public IReadOnlyList<string> Powertrains { get; init; } = [];
    public decimal? MinimumSeats { get; init; }
    public IReadOnlyList<string> RequiredFeatureCodes { get; init; } = [];
}

public sealed class RecommendationWeightsRequest
{
    public decimal PriceValue { get; init; } = 20;
    public decimal RunningCost { get; init; } = 15;
    public decimal Space { get; init; } = 15;
    public decimal SafetyAdas { get; init; } = 20;
    public decimal Comfort { get; init; } = 10;
    public decimal Performance { get; init; } = 10;
    public decimal Technology { get; init; } = 10;
}

public sealed class RecommendationRequest
{
    public RecommendationHardFiltersRequest HardFilters { get; init; } = new();
    public RecommendationWeightsRequest Weights { get; init; } = new();
    public string RegionCode { get; init; } = "VN-01";
    public DateTimeOffset? AsOfDate { get; init; }
    public int MaximumResults { get; init; } = 10;
}

public sealed record RecommendationVehicle(
    Guid TrimId,
    string BrandName,
    string ModelName,
    string TrimName,
    int ModelYear,
    string BodyType,
    string Segment,
    string Powertrain,
    decimal? CurrentPrice,
    string Currency);

public sealed record RecommendationSource(
    Guid SourceFactId,
    Guid SourceId,
    string Name,
    string Url,
    string Authority,
    string ContentType,
    DateTimeOffset FetchedAt,
    string ContentHash,
    string FactStatus,
    string Confidence,
    bool Stale);

public sealed record RecommendationMetric(
    string Code,
    string Label,
    decimal Value,
    string Unit,
    string Direction);

public sealed record RecommendationComponent(
    string Code,
    string Label,
    decimal Weight,
    IReadOnlyList<RecommendationMetric> RawMetrics,
    decimal? Score,
    bool IncludedInOverall,
    bool Trusted,
    IReadOnlyList<RecommendationSource> Sources,
    string Explanation);

public sealed record RecommendationCandidate(
    RecommendationVehicle Vehicle,
    int? Rank,
    decimal Completeness,
    bool CompletenessPassed,
    bool TrustPassed,
    decimal? OverallScore,
    decimal? PricePerformanceScore,
    IReadOnlyList<RecommendationComponent> Components,
    IReadOnlyList<string> Reasons);

public sealed record RecommendationMethodology(
    string Version,
    IReadOnlyList<string> EvaluationOrder,
    decimal CompletenessThreshold,
    IReadOnlyDictionary<string, decimal> NormalizedWeights,
    string OverallFormula,
    string PricePerformanceFormula,
    IReadOnlyList<string> Assumptions);

public sealed record RecommendationResponse(
    RecommendationMethodology Methodology,
    int Considered,
    int HardFilterMatched,
    IReadOnlyList<RecommendationCandidate> Ranked,
    IReadOnlyList<RecommendationCandidate> DataWithheld,
    IReadOnlyList<RecommendationCandidate> HardFilterExcluded,
    DateTimeOffset EvaluatedAt);

public sealed class RecommendationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
