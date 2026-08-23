using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Rules;

public enum RegistrationComponent
{
    FirstRegistrationTax,
    PlateAndRegistrationFee,
    CompulsoryInsurance,
    InspectionFee,
    RoadUsageFee,
    Other,
}

public enum CalculationType
{
    Fixed,
    Percentage,
    Tiered,
    Formula,
}

public enum EnergyType
{
    Ron95,
    E10Ron95III,
    Ron92E5,
    Diesel,
    Electricity,
}

public enum ChargingNetworkType
{
    Public,
    Private,
    BrandOwned,
    Roaming,
}

public enum ChargingPromotionBenefit
{
    Free,
    PercentageDiscount,
    FixedDiscount,
    KwhCredit,
    SessionCredit,
}

public enum ChargingLocationCoverage
{
    ReferenceOnly,
}

public enum ChargingLocationConfidence
{
    Unknown,
    Low,
    Medium,
    High,
}

public sealed class Region : SourcedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? AreaClass { get; set; }
    public string? ParentCode { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class RegistrationRule : EffectiveSourcedEntity
{
    public RegistrationComponent Component { get; set; }
    public string ScopeJson { get; set; } = "{}";
    public CalculationType CalculationType { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public int Priority { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class EnergyPrice : EffectiveSourcedEntity
{
    public EnergyType EnergyType { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string RegionCode { get; set; } = "VN";
    public decimal Amount { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Currency { get; set; } = "VND";
    public int TierFromInclusive { get; set; }
    public int? TierToInclusive { get; set; }
    public decimal TaxRate { get; set; }
    public bool TaxIncluded { get; set; } = true;
}

public sealed class ChargingProvider : SourcedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public ChargingNetworkType NetworkType { get; set; }
    public string OfficialUrl { get; set; } = string.Empty;
}

public sealed class ChargingTariff : EffectiveSourcedEntity
{
    public Guid ProviderId { get; set; }
    public string? ConnectorType { get; set; }
    public decimal? MinimumPowerKw { get; set; }
    public decimal? MaximumPowerKw { get; set; }
    public decimal? AmountPerKwh { get; set; }
    public decimal? AmountPerSession { get; set; }
    public decimal? OverstayAmountPerMinute { get; set; }
    public string OverstayRulesJson { get; set; } = "{}";
    public decimal? OverstayCapPerSession { get; set; }
    public bool TaxIncluded { get; set; } = true;
    public string Currency { get; set; } = "VND";
    public string RegionScope { get; set; } = "VN";
}

public sealed class ChargingPromotion : EffectiveSourcedEntity
{
    public Guid? ProviderId { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? ModelId { get; set; }
    public ChargingPromotionBenefit Benefit { get; set; }
    public string EligibilityJson { get; set; } = "{}";
    public string CapsJson { get; set; } = "{}";
    public decimal? BenefitValue { get; set; }
    public string? Currency { get; set; }
}

/// <summary>
/// Cached charging-location reference data. Open Charge Map records are never
/// treated as charging-tariff facts; a tariff can only be exposed after an
/// explicit reviewed mapping to an authoritative ChargingProvider.
/// </summary>
public sealed class ChargingStation : Entity
{
    public string ExternalSource { get; set; } = "OpenChargeMap";
    public int ExternalId { get; set; }
    public string? ExternalUuid { get; set; }
    public Guid SourceSnapshotId { get; set; }
    public Guid? ChargingProviderId { get; set; }
    public DateTimeOffset? ProviderMappingReviewedAt { get; set; }
    public string? ProviderMappingReviewedBy { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Town { get; set; }
    public string? StateOrProvince { get; set; }
    public string? Postcode { get; set; }
    public string CountryCode { get; set; } = "VN";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string? OperatorName { get; set; }
    public string? UsageType { get; set; }
    public string? OperationalStatus { get; set; }
    public bool? IsOperational { get; set; }
    public int? NumberOfPoints { get; set; }
    public int? ExternalDataQualityLevel { get; set; }
    public ChargingLocationCoverage Coverage { get; set; } = ChargingLocationCoverage.ReferenceOnly;
    public ChargingLocationConfidence Confidence { get; set; } = ChargingLocationConfidence.Unknown;
    public string? RelatedUrl { get; set; }
    public DateTimeOffset? ExternalUpdatedAt { get; set; }
    public DateTimeOffset? LastConfirmedAt { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class ChargingStationConnector : Entity
{
    public Guid ChargingStationId { get; set; }
    public int ExternalId { get; set; }
    public string? ConnectorType { get; set; }
    public string? ChargingLevel { get; set; }
    public string? CurrentType { get; set; }
    public string? OperationalStatus { get; set; }
    public decimal? PowerKw { get; set; }
    public int? Amps { get; set; }
    public int? Voltage { get; set; }
    public int? Quantity { get; set; }
}
