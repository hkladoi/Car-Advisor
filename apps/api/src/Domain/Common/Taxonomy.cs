namespace VietnamCarPlatform.Domain.Common;

public enum FactStatus
{
    Unknown,
    NotAvailable,
    NotApplicable,
    Expected,
    Official,
}

public enum ConfidenceLevel
{
    Unknown,
    Estimated,
    TrustedSingleSource,
    VerifiedMultiSource,
    VerifiedOfficial,
}

public enum ValueDataType
{
    Boolean,
    Number,
    Text,
    Enum,
}

public enum PowertrainType
{
    Unknown,
    Ice,
    Hev,
    Phev,
    Erev,
    Bev,
}

public enum BodyType
{
    Unknown,
    Sedan,
    Hatchback,
    Suv,
    Crossover,
    Mpv,
    Pickup,
    Coupe,
    Convertible,
    Wagon,
    Van,
    Other,
}

public enum VehicleSegment
{
    Unknown,
    A,
    B,
    C,
    D,
    E,
    F,
    Luxury,
    Sports,
    Utility,
}

public enum MarketStatus
{
    Unknown,
    Upcoming,
    Announced,
    Active,
    Discontinued,
}

public enum AvailabilityStatus
{
    Unknown,
    Available,
    NotAvailable,
    NotApplicable,
    Expected,
}

public enum RightsStatus
{
    Unknown,
    Owned,
    Licensed,
    OfficialPressKit,
    Permitted,
    Restricted,
}

public static class FactSemantics
{
    public static bool AllowsValue(FactStatus status, bool hasValue) => status switch
    {
        FactStatus.Unknown or FactStatus.NotAvailable or FactStatus.NotApplicable => !hasValue,
        FactStatus.Expected or FactStatus.Official => hasValue,
        _ => false,
    };
}

public static class CanonicalFeatureCodes
{
    public static readonly IReadOnlySet<string> Adas = new HashSet<string>(StringComparer.Ordinal)
    {
        "ACC", "AEB", "FCW", "LKA", "LCC_LFA", "BSD", "RCTA", "TSR",
    };

    public static readonly IReadOnlySet<string> Convenience = new HashSet<string>(StringComparer.Ordinal)
    {
        "REMOTE_START", "REMOTE_CLIMATE", "APP_CONTROL", "HUD", "CAMERA_360",
        "VENTILATED_FRONT", "HEATED_REAR", "SEAT_MEMORY", "PANORAMIC_ROOF",
    };
}
