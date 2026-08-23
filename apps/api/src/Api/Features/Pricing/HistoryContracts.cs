namespace VietnamCarPlatform.Api.Features.Pricing;

public sealed class VehiclePriceHistoryQuery
{
    public string? RegionScope { get; init; }
    public int? Months { get; init; }
}

public sealed class DealerOfferHistoryQuery
{
    public string? ProvinceCode { get; init; }
    public int? Months { get; init; }
}

public sealed class EnergyPriceHistoryQuery
{
    public string? EnergyType { get; init; }
    public string? Provider { get; init; }
    public string? RegionCode { get; init; }
    public int? Months { get; init; }
}

public sealed record HistoryWindow(
    DateTimeOffset From,
    DateTimeOffset To,
    int Months,
    bool Truncated);

public sealed record HistorySourceReference(
    Guid SourceFactId,
    Guid SourceId,
    string Name,
    string Url,
    string Authority,
    DateTimeOffset FetchedAt,
    string ContentHash,
    string Confidence);

public sealed record VehicleHistoryIdentity(
    Guid TrimId,
    string BrandName,
    string ModelName,
    string TrimName,
    int ModelYear);

public sealed record PriceTimelineEvent(
    Guid Id,
    string Series,
    string ValueKind,
    decimal? Amount,
    string Currency,
    string Status,
    string Scope,
    string Label,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsCurrent,
    bool IsStale,
    string Provenance,
    HistorySourceReference? Source);

public sealed record CashPriceRangeInsight(
    bool Available,
    string Basis,
    string Policy,
    string ReasonCode,
    int ObservationCount,
    int DistinctObservationDates,
    int SpanDays,
    decimal? CurrentAmount,
    decimal? TwelveMonthMinimum,
    decimal? TwelveMonthMaximum,
    string Currency,
    string? Position);

public sealed record VehiclePriceHistoryResponse(
    VehicleHistoryIdentity Vehicle,
    IReadOnlyList<PriceTimelineEvent> Timeline,
    CashPriceRangeInsight CurrentVsTwelveMonthRange,
    HistoryWindow Window,
    DateTimeOffset GeneratedAt);

public sealed record DealerOfferBenefitHistory(
    string Type,
    decimal? CashValue,
    decimal? StatedValue,
    string Currency,
    bool IsCashEquivalent,
    string? ExclusivityGroup,
    string? Note);

public sealed record DealerOfferHistoryItem(
    Guid Id,
    string DealerName,
    string BranchName,
    string ProvinceCode,
    string Headline,
    string Status,
    string ConditionsJson,
    string? CombinabilityGroup,
    decimal? MaximumEligibleCashReduction,
    string Currency,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset LastVerifiedAt,
    bool IsCurrent,
    bool IsStale,
    IReadOnlyList<DealerOfferBenefitHistory> Benefits,
    string Provenance,
    HistorySourceReference? Source);

public sealed record DealerOfferHistoryResponse(
    VehicleHistoryIdentity Vehicle,
    IReadOnlyList<DealerOfferHistoryItem> Current,
    IReadOnlyList<DealerOfferHistoryItem> History,
    string CashSemantics,
    HistoryWindow Window,
    DateTimeOffset GeneratedAt);

public sealed record EnergyPriceObservation(
    Guid Id,
    decimal Amount,
    decimal TaxRate,
    bool TaxIncluded,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsCurrent,
    string Provenance,
    HistorySourceReference? Source);

public sealed record EnergyPriceSeries(
    string SeriesKey,
    string EnergyType,
    string Provider,
    string RegionCode,
    string Unit,
    string Currency,
    int TierFromInclusive,
    int? TierToInclusive,
    IReadOnlyList<EnergyPriceObservation> Observations);

public sealed record EnergyPriceHistoryResponse(
    IReadOnlyList<EnergyPriceSeries> Series,
    HistoryWindow Window,
    string Semantics,
    DateTimeOffset GeneratedAt);

public sealed class HistoryOperationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
