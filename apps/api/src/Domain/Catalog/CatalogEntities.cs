using VietnamCarPlatform.Domain.Common;

namespace VietnamCarPlatform.Domain.Catalog;

public sealed class Brand : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? OfficialUrl { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class BrandScope : EffectiveDatedEntity
{
    public Guid BrandId { get; set; }
    public string Market { get; set; } = "VN";
    public bool Included { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public Guid? EvidenceSnapshotId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

public sealed class VehicleModel : Entity
{
    public Guid BrandId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public BodyType BodyType { get; set; } = BodyType.Unknown;
    public VehicleSegment Segment { get; set; } = VehicleSegment.Unknown;
    public string SearchText { get; set; } = string.Empty;
}

public sealed class ModelAlias : Entity
{
    public Guid ModelId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
}

public sealed class Generation : Entity
{
    public Guid ModelId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int StartYear { get; set; }
    public int? EndYear { get; set; }
}

public sealed class ModelYear : Entity
{
    public Guid GenerationId { get; set; }
    public int Year { get; set; }
    public string Market { get; set; } = "VN";
}

public sealed class Trim : Entity
{
    public Guid ModelYearId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string NormalizedKey { get; set; } = string.Empty;
    public MarketStatus MarketStatus { get; set; } = MarketStatus.Unknown;
    public DateOnly? LaunchedAt { get; set; }
    public DateOnly? DiscontinuedAt { get; set; }
    public string SearchText { get; set; } = string.Empty;
}

public sealed class TrimAlias : Entity
{
    public Guid TrimId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
}

public sealed class SpecDefinition : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ValueDataType DataType { get; set; }
    public string? CanonicalUnit { get; set; }
    public string Group { get; set; } = string.Empty;
    public decimal? MinimumNumericValue { get; set; }
    public decimal? MaximumNumericValue { get; set; }
}

public sealed class TrimSpec : SourcedEntity
{
    public Guid TrimId { get; set; }
    public Guid SpecDefinitionId { get; set; }
    public FactStatus Status { get; set; } = FactStatus.Unknown;
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public string? EnumValue { get; set; }
    public string? OriginalValue { get; set; }
    public string? OriginalUnit { get; set; }
}

public sealed class FeatureDefinition : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public ValueDataType DataType { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal? MinimumNumericValue { get; set; }
    public decimal? MaximumNumericValue { get; set; }
}

public sealed class TrimFeature : SourcedEntity
{
    public Guid TrimId { get; set; }
    public Guid FeatureDefinitionId { get; set; }
    public FactStatus Status { get; set; } = FactStatus.Unknown;
    public bool? BooleanValue { get; set; }
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public string? EnumValue { get; set; }
    public string? MarketingName { get; set; }
}

public sealed class VehicleColor : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? HexHint { get; set; }
    public string Type { get; set; } = string.Empty;
}

public sealed class TrimColor : SourcedEntity
{
    public Guid TrimId { get; set; }
    public Guid ColorId { get; set; }
    public AvailabilityStatus Availability { get; set; } = AvailabilityStatus.Unknown;
    public decimal? ExtraPrice { get; set; }
    public string Currency { get; set; } = "VND";
}

public sealed class VehicleImage : Entity
{
    public Guid? TrimId { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? ColorId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? StorageUrl { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public RightsStatus RightsStatus { get; set; } = RightsStatus.Unknown;
    public string ContentHash { get; set; } = string.Empty;
    public string? RightsNote { get; set; }
}

public sealed class PowertrainProfile : SourcedEntity
{
    public Guid TrimId { get; set; }
    public PowertrainType Type { get; set; } = PowertrainType.Unknown;
    public string? FuelType { get; set; }
    public decimal? EngineDisplacementCc { get; set; }
    public decimal? EnginePowerKw { get; set; }
    public decimal? MotorPowerKw { get; set; }
    public decimal? CombinedPowerKw { get; set; }
    public decimal? TorqueNm { get; set; }
    public string? Gearbox { get; set; }
    public string? Drivetrain { get; set; }
}

public sealed class EnergyProfile : SourcedEntity
{
    public Guid TrimId { get; set; }
    public string? RecommendedFuel { get; set; }
    public decimal? OfficialFuelLitresPer100Km { get; set; }
    public decimal? OfficialElectricKwhPer100Km { get; set; }
    public string? FuelConsumptionCondition { get; set; }
    public string? ElectricConsumptionCondition { get; set; }
    public decimal? UsableBatteryKwh { get; set; }
    public decimal? OfficialRangeKm { get; set; }
    public string? TestCycle { get; set; }
    public decimal? AcMaxKw { get; set; }
    public decimal? DcMaxKw { get; set; }
    public string? PortType { get; set; }
    public string? ConsumptionNotes { get; set; }
}

public sealed class WarrantyProfile : SourcedEntity
{
    public Guid TrimId { get; set; }
    public int? VehicleMonths { get; set; }
    public int? VehicleKilometres { get; set; }
    public int? BatteryMonths { get; set; }
    public int? BatteryKilometres { get; set; }
    public string? Conditions { get; set; }
}
