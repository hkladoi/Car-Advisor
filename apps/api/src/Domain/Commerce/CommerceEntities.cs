using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Commerce;

public enum PriceType
{
    Msrp,
    PromotionPrice,
    ExpectedPrice,
    Unannounced,
    DealerCashPrice,
    DealerQuote,
}

public enum PriceStatus
{
    Unknown,
    Expected,
    Official,
    Superseded,
    Withdrawn,
}

public enum BenefitType
{
    CashDiscount,
    RegistrationFeeSupport,
    FirstRegistrationTaxSupport,
    InsuranceGift,
    AccessoryPackage,
    TradeInBonus,
    FinancingBonus,
    ServicePackage,
    ChargingCredit,
    OtherNonCash,
}

public enum OfferStatus
{
    Draft,
    PendingReview,
    Published,
    Expired,
    Rejected,
}

public sealed class Price : EffectiveSourcedEntity
{
    public Guid TrimId { get; set; }
    public PriceType PriceType { get; set; }
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string RegionScope { get; set; } = "VN";
    public PriceStatus Status { get; set; } = PriceStatus.Unknown;
    public int Priority { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class PriceHistory : Entity
{
    public Guid PriceId { get; set; }
    public Guid TrimId { get; set; }
    public PriceType PriceType { get; set; }
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string RegionScope { get; set; } = "VN";
    public PriceStatus Status { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public Guid? SourceFactId { get; set; }
    public string? ManualOverrideReason { get; set; }
    public DateTimeOffset ArchivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Promotion : EffectiveSourcedEntity
{
    public Guid? TrimId { get; set; }
    public Guid? BrandId { get; set; }
    public BenefitType BenefitType { get; set; }
    public decimal? Value { get; set; }
    public string Currency { get; set; } = "VND";
    public string ConditionsJson { get; set; } = "{}";
    public OfferStatus Status { get; set; } = OfferStatus.Draft;
}

public sealed class Dealer : Entity
{
    public Guid BrandId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool OfficialStatus { get; set; }
    public string? OfficialUrl { get; set; }
}

public sealed class DealerBranch : Entity
{
    public Guid DealerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProvinceCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

public sealed class DealerOffer : EffectiveSourcedEntity
{
    public Guid BranchId { get; set; }
    public Guid TrimId { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string? CombinabilityGroup { get; set; }
    public string ConditionsJson { get; set; } = "{}";
    public OfferStatus Status { get; set; } = OfferStatus.Draft;
}

public sealed class DealerOfferBenefit : Entity
{
    public Guid OfferId { get; set; }
    public BenefitType Type { get; set; }
    public decimal? CashValue { get; set; }
    public decimal? StatedValue { get; set; }
    public string Currency { get; set; } = "VND";
    public bool IsCashEquivalent { get; set; }
    public string? ExclusivityGroup { get; set; }
    public string? Note { get; set; }
}
