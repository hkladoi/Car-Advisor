using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietnamCarPlatform.Domain.Affordability;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Domain.Sources;

namespace VietnamCarPlatform.Infrastructure.Persistence;

internal static class OperationalModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ConfigureSources(modelBuilder);
        ConfigureRulesAndEnergy(modelBuilder);
        ConfigureProfiles(modelBuilder);
    }

    private static void ConfigureSources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Source>(entity =>
        {
            entity.ToTable("sources");
            entity.Property(value => value.Name).HasMaxLength(240);
            entity.Property(value => value.Url).HasMaxLength(2048);
            entity.Property(value => value.Domain).HasMaxLength(253);
            entity.Property(value => value.RobotsNote).HasMaxLength(2000);
            entity.Property(value => value.TermsNote).HasMaxLength(2000);
            entity.HasIndex(value => value.Url).IsUnique();
            entity.HasIndex(value => new { value.Domain, value.Active, value.Priority });
        });

        modelBuilder.Entity<SourceSnapshot>(entity =>
        {
            entity.ToTable("source_snapshots", table =>
            {
                table.HasCheckConstraint("ck_source_snapshots_http_status", "http_status BETWEEN 0 AND 599");
                table.HasCheckConstraint("ck_source_snapshots_object_key", "object_key <> ''");
            });
            entity.Property(value => value.ContentHash).HasMaxLength(128);
            entity.Property(value => value.ObjectKey).HasMaxLength(1024);
            entity.Property(value => value.ParserVersion).HasMaxLength(100);
            entity.Property(value => value.Etag).HasMaxLength(512);
            entity.Property(value => value.FetchError).HasMaxLength(4000);
            entity.HasOne<Source>().WithMany().HasForeignKey(value => value.SourceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => value.ObjectKey).IsUnique();
            entity.HasIndex(value => new { value.SourceId, value.ContentHash }).IsUnique();
            entity.HasIndex(value => new { value.SourceId, value.FetchedAt });
        });

        modelBuilder.Entity<SourceFact>(entity =>
        {
            entity.ToTable("source_facts", table => table.HasCheckConstraint(
                "ck_source_facts_value_semantics",
                "((status IN ('Expected', 'Official') AND normalized_value IS NOT NULL) OR (status IN ('Unknown', 'NotAvailable', 'NotApplicable') AND normalized_value IS NULL))"));
            entity.Property(value => value.EntityType).HasMaxLength(160);
            entity.Property(value => value.FieldPath).HasMaxLength(500);
            entity.Property(value => value.RawValue).HasColumnType("text");
            entity.Property(value => value.NormalizedValue).HasColumnType("text");
            entity.Property(value => value.ExtractionContext).HasColumnType("text");
            entity.HasOne<SourceSnapshot>().WithMany().HasForeignKey(value => value.SnapshotId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.EntityType, value.EntityId, value.FieldPath });
            entity.HasIndex(value => new { value.SnapshotId, value.FieldPath });
        });

        modelBuilder.Entity<DataChange>(entity =>
        {
            entity.ToTable("data_changes");
            entity.Property(value => value.EntityType).HasMaxLength(160);
            entity.Property(value => value.FieldPath).HasMaxLength(500);
            entity.Property(value => value.OldValue).HasColumnType("text");
            entity.Property(value => value.NewValue).HasColumnType("text");
            entity.HasOne<SourceFact>().WithMany().HasForeignKey(value => value.SourceFactId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AuditEvent>().WithMany().HasForeignKey(value => value.ReviewedAuditEventId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.Status, value.RiskLevel, value.DetectedAt });
            entity.HasIndex(value => new { value.EntityType, value.EntityId, value.FieldPath });
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events", table => table.HasCheckConstraint(
                "ck_audit_events_reason",
                "NULLIF(BTRIM(reason), '') IS NOT NULL"));
            entity.Property(value => value.Actor).HasMaxLength(320);
            entity.Property(value => value.Action).HasMaxLength(160);
            entity.Property(value => value.EntityType).HasMaxLength(160);
            entity.Property(value => value.BeforeJson).HasColumnType("jsonb");
            entity.Property(value => value.AfterJson).HasColumnType("jsonb");
            entity.Property(value => value.Reason).HasMaxLength(2000);
            entity.Property(value => value.CorrelationId).HasMaxLength(128);
            entity.HasIndex(value => new { value.EntityType, value.EntityId, value.OccurredAt });
            entity.HasIndex(value => value.CorrelationId);
        });

        modelBuilder.Entity<CoverageMetric>(entity =>
        {
            entity.ToTable("coverage_metrics", table =>
            {
                table.HasCheckConstraint("ck_coverage_metrics_completeness", "completeness BETWEEN 0 AND 1");
                table.HasCheckConstraint("ck_coverage_metrics_freshness", "freshness BETWEEN 0 AND 1");
                table.HasCheckConstraint("ck_coverage_metrics_counts", "missing_core_count >= 0 AND discovered_count >= 0 AND mapped_count >= 0 AND published_count >= 0 AND blocked_count >= 0 AND stale_count >= 0");
            });
            entity.Property(value => value.Completeness).HasPrecision(7, 6);
            entity.Property(value => value.Freshness).HasPrecision(7, 6);
            entity.HasOne<Brand>().WithMany().HasForeignKey(value => value.BrandId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<VehicleModel>().WithMany().HasForeignKey(value => value.ModelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.BrandId, value.ModelId, value.TrimId, value.CalculatedAt });
        });
    }

    private static void ConfigureRulesAndEnergy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Region>(entity =>
        {
            entity.ToTable("regions", table =>
                table.HasCheckConstraint("ck_regions_provenance", ProvenanceCheck()));
            entity.Property(value => value.Code).HasMaxLength(20);
            entity.Property(value => value.Name).HasMaxLength(240);
            entity.Property(value => value.Type).HasMaxLength(80);
            entity.Property(value => value.AreaClass).HasMaxLength(80);
            entity.Property(value => value.ParentCode).HasMaxLength(20);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            ConfigureSourceFact(entity);
            entity.HasAlternateKey(value => value.Code);
            entity.HasOne<Region>().WithMany().HasForeignKey(value => value.ParentCode).HasPrincipalKey(value => value.Code).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RegistrationRule>(entity =>
        {
            entity.ToTable("registration_rules", table =>
            {
                table.HasCheckConstraint("ck_registration_rules_effective_period", EffectivePeriodCheck());
                table.HasCheckConstraint("ck_registration_rules_priority", "priority >= 0");
                table.HasCheckConstraint("ck_registration_rules_version", "version > 0");
                table.HasCheckConstraint("ck_registration_rules_provenance", ProvenanceCheck());
            });
            entity.Property(value => value.ScopeJson).HasColumnType("jsonb");
            entity.Property(value => value.ParametersJson).HasColumnType("jsonb");
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => value.ScopeJson).HasMethod("gin").HasOperators("jsonb_path_ops");
            entity.HasIndex(value => new { value.Component, value.EffectiveFrom, value.Priority });
        });

        modelBuilder.Entity<EnergyPrice>(entity =>
        {
            entity.ToTable("energy_prices", table =>
            {
                table.HasCheckConstraint("ck_energy_prices_effective_period", EffectivePeriodCheck());
                table.HasCheckConstraint("ck_energy_prices_amount", "amount >= 0");
                table.HasCheckConstraint("ck_energy_prices_tax_rate", "tax_rate >= 0 AND tax_rate <= 1");
                table.HasCheckConstraint("ck_energy_prices_tier", "tier_from_inclusive >= 0 AND (tier_to_inclusive IS NULL OR tier_from_inclusive <= tier_to_inclusive)");
                table.HasCheckConstraint("ck_energy_prices_provenance", ProvenanceCheck());
            });
            entity.Property(value => value.Provider).HasMaxLength(200);
            entity.Property(value => value.RegionCode).HasMaxLength(20);
            entity.Property(value => value.Amount).HasPrecision(19, 6);
            entity.Property(value => value.Unit).HasMaxLength(40);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.TaxRate).HasPrecision(9, 6);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => new { value.EnergyType, value.Provider, value.RegionCode, value.EffectiveFrom });
        });

        modelBuilder.Entity<ChargingProvider>(entity =>
        {
            entity.ToTable("charging_providers", table =>
                table.HasCheckConstraint("ck_charging_providers_provenance", ProvenanceCheck()));
            entity.Property(value => value.Name).HasMaxLength(240);
            entity.Property(value => value.Slug).HasMaxLength(260);
            entity.Property(value => value.OfficialUrl).HasMaxLength(2048);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => value.Slug).IsUnique();
        });

        modelBuilder.Entity<ChargingTariff>(entity =>
        {
            entity.ToTable("charging_tariffs", table =>
            {
                table.HasCheckConstraint("ck_charging_tariffs_effective_period", EffectivePeriodCheck());
                table.HasCheckConstraint("ck_charging_tariffs_power_band", "maximum_power_kw IS NULL OR minimum_power_kw IS NULL OR minimum_power_kw <= maximum_power_kw");
                table.HasCheckConstraint("ck_charging_tariffs_amounts", "COALESCE(amount_per_kwh, amount_per_session, overstay_amount_per_minute) IS NOT NULL AND (amount_per_kwh IS NULL OR amount_per_kwh >= 0) AND (amount_per_session IS NULL OR amount_per_session >= 0) AND (overstay_amount_per_minute IS NULL OR overstay_amount_per_minute >= 0)");
                table.HasCheckConstraint("ck_charging_tariffs_overstay_cap", "overstay_cap_per_session IS NULL OR overstay_cap_per_session >= 0");
                table.HasCheckConstraint("ck_charging_tariffs_provenance", ProvenanceCheck());
            });
            entity.Property(value => value.ConnectorType).HasMaxLength(80);
            entity.Property(value => value.MinimumPowerKw).HasPrecision(18, 4);
            entity.Property(value => value.MaximumPowerKw).HasPrecision(18, 4);
            entity.Property(value => value.AmountPerKwh).HasPrecision(19, 6);
            entity.Property(value => value.AmountPerSession).HasPrecision(19, 2);
            entity.Property(value => value.OverstayAmountPerMinute).HasPrecision(19, 6);
            entity.Property(value => value.OverstayRulesJson).HasColumnType("jsonb");
            entity.Property(value => value.OverstayCapPerSession).HasPrecision(19, 2);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.RegionScope).HasMaxLength(120);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<ChargingProvider>().WithMany().HasForeignKey(value => value.ProviderId).OnDelete(DeleteBehavior.Cascade);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => new { value.ProviderId, value.ConnectorType, value.MinimumPowerKw, value.EffectiveFrom });
        });

        modelBuilder.Entity<ChargingPromotion>(entity =>
        {
            entity.ToTable("charging_promotions", table =>
            {
                table.HasCheckConstraint("ck_charging_promotions_effective_period", EffectivePeriodCheck());
                table.HasCheckConstraint("ck_charging_promotions_scope", "provider_id IS NOT NULL OR brand_id IS NOT NULL OR model_id IS NOT NULL");
                table.HasCheckConstraint("ck_charging_promotions_benefit_value", "benefit_value IS NULL OR benefit_value >= 0");
                table.HasCheckConstraint("ck_charging_promotions_provenance", ProvenanceCheck());
            });
            entity.Property(value => value.EligibilityJson).HasColumnType("jsonb");
            entity.Property(value => value.CapsJson).HasColumnType("jsonb");
            entity.Property(value => value.BenefitValue).HasPrecision(19, 6);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<ChargingProvider>().WithMany().HasForeignKey(value => value.ProviderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Brand>().WithMany().HasForeignKey(value => value.BrandId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<VehicleModel>().WithMany().HasForeignKey(value => value.ModelId).OnDelete(DeleteBehavior.Cascade);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => value.EligibilityJson).HasMethod("gin").HasOperators("jsonb_path_ops");
            entity.HasIndex(value => new { value.ProviderId, value.BrandId, value.ModelId, value.EffectiveFrom });
        });
    }

    private static void ConfigureProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AffordabilityProfile>(entity =>
        {
            entity.ToTable("affordability_profiles", table => table.HasCheckConstraint(
                "ck_affordability_profiles_nonnegative",
                "net_monthly_income >= 0 AND rent_housing >= 0 AND essential_expenses >= 0 AND other_fixed_debt >= 0 AND savings_target >= 0 AND monthly_kilometres >= 0 AND parking_monthly >= 0 AND household_base_kwh >= 0"));
            entity.Property(value => value.OwnerSubjectId).HasMaxLength(320);
            entity.Property(value => value.Name).HasMaxLength(240);
            entity.Property(value => value.NetMonthlyIncome).HasPrecision(19, 2);
            entity.Property(value => value.RentHousing).HasPrecision(19, 2);
            entity.Property(value => value.EssentialExpenses).HasPrecision(19, 2);
            entity.Property(value => value.OtherFixedDebt).HasPrecision(19, 2);
            entity.Property(value => value.SavingsTarget).HasPrecision(19, 2);
            entity.Property(value => value.MonthlyKilometres).HasPrecision(18, 3);
            entity.Property(value => value.ParkingMonthly).HasPrecision(19, 2);
            entity.Property(value => value.HouseholdBaseKwh).HasPrecision(18, 3);
            entity.Property(value => value.RegionCode).HasMaxLength(20);
            entity.Property(value => value.AssumptionsJson).HasColumnType("jsonb");
            entity.HasIndex(value => new { value.OwnerSubjectId, value.Name });
        });

        modelBuilder.Entity<FinancingScenario>(entity =>
        {
            entity.ToTable("financing_scenarios", table =>
            {
                table.HasCheckConstraint("ck_financing_scenarios_amounts", "available_cash >= 0 AND down_payment >= 0 AND principal >= 0 AND annual_interest_rate >= 0 AND origination_fees >= 0");
                table.HasCheckConstraint("ck_financing_scenarios_term", "(purchase_method = 'Cash' AND term_months = 0 AND principal = 0) OR (purchase_method = 'Loan' AND term_months > 0)");
            });
            entity.Property(value => value.AvailableCash).HasPrecision(19, 2);
            entity.Property(value => value.DownPayment).HasPrecision(19, 2);
            entity.Property(value => value.Principal).HasPrecision(19, 2);
            entity.Property(value => value.AnnualInterestRate).HasPrecision(9, 6);
            entity.Property(value => value.OriginationFees).HasPrecision(19, 2);
            entity.Property(value => value.DealerFinancingConditionsJson).HasColumnType("jsonb");
            entity.HasOne<AffordabilityProfile>().WithMany().HasForeignKey(value => value.AffordabilityProfileId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.TrimId, value.CreatedAt });
        });
    }

    private static void ConfigureSourceFact<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.HasOne<SourceFact>().WithMany().HasForeignKey("SourceFactId").OnDelete(DeleteBehavior.Restrict);
    }

    private static string EffectivePeriodCheck() => "effective_to IS NULL OR effective_from < effective_to";

    private static string ProvenanceCheck() =>
        "source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL";
}
