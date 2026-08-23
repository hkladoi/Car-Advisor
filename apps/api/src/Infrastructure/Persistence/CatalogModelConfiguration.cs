using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Sources;

namespace VietnamCarPlatform.Infrastructure.Persistence;

internal static class CatalogModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(entity =>
        {
            entity.ToTable("brands");
            entity.Property(value => value.Name).HasMaxLength(160);
            entity.Property(value => value.Slug).HasMaxLength(180);
            entity.Property(value => value.CountryCode).HasMaxLength(2);
            entity.Property(value => value.OfficialUrl).HasMaxLength(2048);
            entity.HasIndex(value => value.Slug).IsUnique();
        });

        modelBuilder.Entity<BrandScope>(entity =>
        {
            entity.ToTable("brand_scopes", table => table.HasCheckConstraint(
                "ck_brand_scopes_effective_period",
                "effective_to IS NULL OR effective_from < effective_to"));
            entity.Property(value => value.Reason).HasMaxLength(500);
            entity.Property(value => value.Market).HasMaxLength(8);
            entity.Property(value => value.ReviewedBy).HasMaxLength(320);
            entity.HasOne<Brand>().WithMany().HasForeignKey(value => value.BrandId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Source>().WithMany().HasForeignKey(value => value.SourceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SourceSnapshot>().WithMany().HasForeignKey(value => value.EvidenceSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.Market, value.BrandId, value.EffectiveFrom }).IsUnique();
        });

        modelBuilder.Entity<VehicleModel>(entity =>
        {
            entity.ToTable("models");
            entity.Property(value => value.Name).HasMaxLength(160);
            entity.Property(value => value.Slug).HasMaxLength(180);
            entity.Property(value => value.SearchText).HasMaxLength(1000);
            entity.HasOne<Brand>().WithMany().HasForeignKey(value => value.BrandId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.BrandId, value.Slug }).IsUnique();
            entity.HasIndex(value => value.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        modelBuilder.Entity<ModelAlias>(entity =>
        {
            entity.ToTable("model_aliases");
            entity.Property(value => value.Alias).HasMaxLength(240);
            entity.Property(value => value.NormalizedAlias).HasMaxLength(240);
            entity.HasOne<VehicleModel>().WithMany().HasForeignKey(value => value.ModelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Source>().WithMany().HasForeignKey(value => value.SourceId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(value => new { value.ModelId, value.NormalizedAlias }).IsUnique();
            entity.HasIndex(value => value.NormalizedAlias).HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        modelBuilder.Entity<Generation>(entity =>
        {
            entity.ToTable("generations", table => table.HasCheckConstraint(
                "ck_generations_year_range",
                "end_year IS NULL OR start_year <= end_year"));
            entity.Property(value => value.Code).HasMaxLength(80);
            entity.Property(value => value.Name).HasMaxLength(160);
            entity.HasOne<VehicleModel>().WithMany().HasForeignKey(value => value.ModelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.ModelId, value.Code }).IsUnique();
        });

        modelBuilder.Entity<ModelYear>(entity =>
        {
            entity.ToTable("model_years", table => table.HasCheckConstraint(
                "ck_model_years_year",
                "year BETWEEN 1900 AND 2200"));
            entity.Property(value => value.Market).HasMaxLength(8);
            entity.HasOne<Generation>().WithMany().HasForeignKey(value => value.GenerationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.GenerationId, value.Year, value.Market }).IsUnique();
        });

        modelBuilder.Entity<Trim>(entity =>
        {
            entity.ToTable("trims", table => table.HasCheckConstraint(
                "ck_trims_market_dates",
                "discontinued_at IS NULL OR launched_at IS NULL OR launched_at <= discontinued_at"));
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.Property(value => value.Slug).HasMaxLength(220);
            entity.Property(value => value.NormalizedKey).HasMaxLength(260);
            entity.Property(value => value.SearchText).HasMaxLength(1200);
            entity.HasOne<ModelYear>().WithMany().HasForeignKey(value => value.ModelYearId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.ModelYearId, value.NormalizedKey }).IsUnique();
            entity.HasIndex(value => new { value.ModelYearId, value.MarketStatus });
            entity.HasIndex(value => value.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        modelBuilder.Entity<TrimAlias>(entity =>
        {
            entity.ToTable("trim_aliases");
            entity.Property(value => value.Alias).HasMaxLength(260);
            entity.Property(value => value.NormalizedAlias).HasMaxLength(260);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Source>().WithMany().HasForeignKey(value => value.SourceId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(value => new { value.TrimId, value.NormalizedAlias }).IsUnique();
            entity.HasIndex(value => value.NormalizedAlias).HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        modelBuilder.Entity<SpecDefinition>(entity =>
        {
            entity.ToTable("spec_definitions", table => table.HasCheckConstraint(
                "ck_spec_definitions_numeric_range",
                "maximum_numeric_value IS NULL OR minimum_numeric_value IS NULL OR minimum_numeric_value <= maximum_numeric_value"));
            entity.Property(value => value.Code).HasMaxLength(100);
            entity.Property(value => value.Label).HasMaxLength(200);
            entity.Property(value => value.CanonicalUnit).HasMaxLength(60);
            entity.Property(value => value.Group).HasMaxLength(100);
            entity.Property(value => value.MinimumNumericValue).HasPrecision(18, 6);
            entity.Property(value => value.MaximumNumericValue).HasPrecision(18, 6);
            entity.HasIndex(value => value.Code).IsUnique();
        });

        modelBuilder.Entity<TrimSpec>(entity =>
        {
            entity.ToTable("trim_specs", table =>
            {
                table.HasCheckConstraint("ck_trim_specs_value_semantics", SourcedValueCheck(
                    "numeric_value", "text_value", "enum_value"));
                table.HasCheckConstraint("ck_trim_specs_official_provenance", OfficialProvenanceCheck());
            });
            entity.Property(value => value.NumericValue).HasPrecision(18, 6);
            entity.Property(value => value.TextValue).HasMaxLength(2000);
            entity.Property(value => value.EnumValue).HasMaxLength(160);
            entity.Property(value => value.OriginalValue).HasMaxLength(2000);
            entity.Property(value => value.OriginalUnit).HasMaxLength(60);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SpecDefinition>().WithMany().HasForeignKey(value => value.SpecDefinitionId).OnDelete(DeleteBehavior.Restrict);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => new { value.TrimId, value.SpecDefinitionId }).IsUnique();
        });

        modelBuilder.Entity<FeatureDefinition>(entity =>
        {
            entity.ToTable("feature_definitions", table => table.HasCheckConstraint(
                "ck_feature_definitions_numeric_range",
                "maximum_numeric_value IS NULL OR minimum_numeric_value IS NULL OR minimum_numeric_value <= maximum_numeric_value"));
            entity.Property(value => value.Code).HasMaxLength(100);
            entity.Property(value => value.Group).HasMaxLength(100);
            entity.Property(value => value.Label).HasMaxLength(200);
            entity.Property(value => value.MinimumNumericValue).HasPrecision(18, 6);
            entity.Property(value => value.MaximumNumericValue).HasPrecision(18, 6);
            entity.HasIndex(value => value.Code).IsUnique();
        });

        modelBuilder.Entity<TrimFeature>(entity =>
        {
            entity.ToTable("trim_features", table =>
            {
                table.HasCheckConstraint("ck_trim_features_value_semantics", SourcedValueCheck(
                    "boolean_value", "numeric_value", "text_value", "enum_value"));
                table.HasCheckConstraint("ck_trim_features_official_provenance", OfficialProvenanceCheck());
            });
            entity.Property(value => value.NumericValue).HasPrecision(18, 6);
            entity.Property(value => value.TextValue).HasMaxLength(2000);
            entity.Property(value => value.EnumValue).HasMaxLength(160);
            entity.Property(value => value.MarketingName).HasMaxLength(300);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<FeatureDefinition>().WithMany().HasForeignKey(value => value.FeatureDefinitionId).OnDelete(DeleteBehavior.Restrict);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => new { value.TrimId, value.FeatureDefinitionId }).IsUnique();
        });

        modelBuilder.Entity<VehicleColor>(entity =>
        {
            entity.ToTable("colors");
            entity.Property(value => value.Code).HasMaxLength(80);
            entity.Property(value => value.Name).HasMaxLength(160);
            entity.Property(value => value.HexHint).HasMaxLength(9);
            entity.Property(value => value.Type).HasMaxLength(40);
            entity.HasIndex(value => value.Code).IsUnique();
        });

        modelBuilder.Entity<TrimColor>(entity =>
        {
            entity.ToTable("trim_colors", table =>
            {
                table.HasCheckConstraint("ck_trim_colors_extra_price", "extra_price IS NULL OR extra_price >= 0");
                table.HasCheckConstraint("ck_trim_colors_official_provenance", "availability <> 'Available' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL");
            });
            entity.Property(value => value.ExtraPrice).HasPrecision(19, 2);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<VehicleColor>().WithMany().HasForeignKey(value => value.ColorId).OnDelete(DeleteBehavior.Restrict);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => new { value.TrimId, value.ColorId }).IsUnique();
        });

        modelBuilder.Entity<VehicleImage>(entity =>
        {
            entity.ToTable("vehicle_images", table =>
            {
                table.HasCheckConstraint("ck_vehicle_images_owner", "(trim_id IS NOT NULL) <> (model_id IS NOT NULL)");
                table.HasCheckConstraint("ck_vehicle_images_publishable_rights", "storage_url IS NULL OR rights_status IN ('Owned', 'Licensed', 'OfficialPressKit', 'Permitted')");
            });
            entity.Property(value => value.Type).HasMaxLength(80);
            entity.Property(value => value.StorageUrl).HasMaxLength(2048);
            entity.Property(value => value.SourceUrl).HasMaxLength(2048);
            entity.Property(value => value.ContentHash).HasMaxLength(128);
            entity.Property(value => value.RightsNote).HasMaxLength(1000);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<VehicleModel>().WithMany().HasForeignKey(value => value.ModelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<VehicleColor>().WithMany().HasForeignKey(value => value.ColorId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(value => value.ContentHash).IsUnique();
        });

        ConfigureProfile(modelBuilder.Entity<PowertrainProfile>(), "powertrain_profiles");
        ConfigureProfile(modelBuilder.Entity<EnergyProfile>(), "energy_profiles");
        ConfigureProfile(modelBuilder.Entity<WarrantyProfile>(), "warranty_profiles");

        modelBuilder.Entity<PowertrainProfile>(entity =>
        {
            entity.Property(value => value.FuelType).HasMaxLength(80);
            entity.Property(value => value.Gearbox).HasMaxLength(120);
            entity.Property(value => value.Drivetrain).HasMaxLength(80);
            entity.Property(value => value.EngineDisplacementCc).HasPrecision(18, 3);
            entity.Property(value => value.EnginePowerKw).HasPrecision(18, 4);
            entity.Property(value => value.MotorPowerKw).HasPrecision(18, 4);
            entity.Property(value => value.CombinedPowerKw).HasPrecision(18, 4);
            entity.Property(value => value.TorqueNm).HasPrecision(18, 4);
        });

        modelBuilder.Entity<EnergyProfile>(entity =>
        {
            entity.Property(value => value.RecommendedFuel).HasMaxLength(80);
            entity.Property(value => value.TestCycle).HasMaxLength(40);
            entity.Property(value => value.FuelConsumptionCondition).HasMaxLength(120);
            entity.Property(value => value.ElectricConsumptionCondition).HasMaxLength(120);
            entity.Property(value => value.PortType).HasMaxLength(80);
            entity.Property(value => value.ConsumptionNotes).HasMaxLength(2000);
            entity.Property(value => value.OfficialFuelLitresPer100Km).HasPrecision(18, 6);
            entity.Property(value => value.OfficialElectricKwhPer100Km).HasPrecision(18, 6);
            entity.Property(value => value.UsableBatteryKwh).HasPrecision(18, 6);
            entity.Property(value => value.OfficialRangeKm).HasPrecision(18, 3);
            entity.Property(value => value.AcMaxKw).HasPrecision(18, 4);
            entity.Property(value => value.DcMaxKw).HasPrecision(18, 4);
        });

        modelBuilder.Entity<WarrantyProfile>(entity => entity.Property(value => value.Conditions).HasMaxLength(2000));
    }

    private static void ConfigureProfile<TEntity>(EntityTypeBuilder<TEntity> entity, string tableName)
        where TEntity : SourcedEntity
    {
        entity.ToTable(tableName);
        entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
        entity.HasOne<Trim>().WithMany().HasForeignKey("TrimId").OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<SourceFact>().WithMany().HasForeignKey(value => value.SourceFactId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex("TrimId").IsUnique();
    }

    private static void ConfigureSourceFact<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : SourcedEntity
    {
        entity.HasOne<SourceFact>().WithMany().HasForeignKey(value => value.SourceFactId).OnDelete(DeleteBehavior.Restrict);
    }

    private static string SourcedValueCheck(params string[] columns)
    {
        var count = string.Join(" + ", columns.Select(column => $"({column} IS NOT NULL)::int"));
        var empty = string.Join(" AND ", columns.Select(column => $"{column} IS NULL"));
        return $"((status IN ('Expected', 'Official') AND ({count}) = 1) OR (status IN ('Unknown', 'NotAvailable', 'NotApplicable') AND {empty}))";
    }

    private static string OfficialProvenanceCheck() =>
        "status <> 'Official' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL";
}
