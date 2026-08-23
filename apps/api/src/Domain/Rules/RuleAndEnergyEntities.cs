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
