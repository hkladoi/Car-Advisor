using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Admin;
using VietnamCarPlatform.Domain.Affordability;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Infrastructure.Catalog;

namespace VietnamCarPlatform.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
    public DbSet<FieldLock> FieldLocks => Set<FieldLock>();
    public DbSet<ManualImport> ManualImports => Set<ManualImport>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<BrandScope> BrandScopes => Set<BrandScope>();
    public DbSet<VehicleModel> Models => Set<VehicleModel>();
    public DbSet<ModelAlias> ModelAliases => Set<ModelAlias>();
    public DbSet<Generation> Generations => Set<Generation>();
    public DbSet<ModelYear> ModelYears => Set<ModelYear>();
    public DbSet<Trim> Trims => Set<Trim>();
    public DbSet<TrimAlias> TrimAliases => Set<TrimAlias>();
    public DbSet<SpecDefinition> SpecDefinitions => Set<SpecDefinition>();
    public DbSet<TrimSpec> TrimSpecs => Set<TrimSpec>();
    public DbSet<FeatureDefinition> FeatureDefinitions => Set<FeatureDefinition>();
    public DbSet<TrimFeature> TrimFeatures => Set<TrimFeature>();
    public DbSet<VehicleColor> Colors => Set<VehicleColor>();
    public DbSet<TrimColor> TrimColors => Set<TrimColor>();
    public DbSet<VehicleImage> VehicleImages => Set<VehicleImage>();
    public DbSet<PowertrainProfile> PowertrainProfiles => Set<PowertrainProfile>();
    public DbSet<EnergyProfile> EnergyProfiles => Set<EnergyProfile>();
    public DbSet<WarrantyProfile> WarrantyProfiles => Set<WarrantyProfile>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<PriceHistory> PriceHistory => Set<PriceHistory>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<Dealer> Dealers => Set<Dealer>();
    public DbSet<DealerBranch> DealerBranches => Set<DealerBranch>();
    public DbSet<DealerOffer> DealerOffers => Set<DealerOffer>();
    public DbSet<DealerOfferBenefit> DealerOfferBenefits => Set<DealerOfferBenefit>();
    public DbSet<AffordabilityProfile> AffordabilityProfiles => Set<AffordabilityProfile>();
    public DbSet<FinancingScenario> FinancingScenarios => Set<FinancingScenario>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<RegistrationRule> RegistrationRules => Set<RegistrationRule>();
    public DbSet<EnergyPrice> EnergyPrices => Set<EnergyPrice>();
    public DbSet<ChargingProvider> ChargingProviders => Set<ChargingProvider>();
    public DbSet<ChargingTariff> ChargingTariffs => Set<ChargingTariff>();
    public DbSet<ChargingPromotion> ChargingPromotions => Set<ChargingPromotion>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SourceSnapshot> SourceSnapshots => Set<SourceSnapshot>();
    public DbSet<SourceFact> SourceFacts => Set<SourceFact>();
    public DbSet<DataChange> DataChanges => Set<DataChange>();
    public DbSet<PublicationVersion> PublicationVersions => Set<PublicationVersion>();
    public DbSet<IngestionJobRun> IngestionJobRuns => Set<IngestionJobRun>();
    public DbSet<MonitoringAlert> MonitoringAlerts => Set<MonitoringAlert>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CoverageMetric> CoverageMetrics => Set<CoverageMetric>();
    public DbSet<CurrentSearchableTrim> CurrentSearchableTrims => Set<CurrentSearchableTrim>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<FactStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<ConfidenceLevel>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<ValueDataType>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<PowertrainType>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<BodyType>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<VehicleSegment>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<MarketStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<AvailabilityStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<RightsStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<PriceType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<PriceStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<BenefitType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<OfferStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<AffordabilityPolicy>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<PurchaseFundingSource>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<PurchaseMethod>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<LoanRepaymentMethod>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<RegistrationComponent>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<CalculationType>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<EnergyType>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<ChargingNetworkType>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<ChargingPromotionBenefit>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<SourceAuthorityLevel>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<SourceContentType>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<ChangeRiskLevel>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<ChangeStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<PublicationStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<IngestionRunStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<MonitoringAlertStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<MonitoringAlertSeverity>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<AdministratorRole>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<ManualImportStatus>().HaveConversion<string>().HaveMaxLength(24);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("btree_gist");

        CatalogModelConfiguration.Configure(modelBuilder);
        CommerceModelConfiguration.Configure(modelBuilder);
        OperationalModelConfiguration.Configure(modelBuilder);
        AdminModelConfiguration.Configure(modelBuilder);

        modelBuilder.Entity<CurrentSearchableTrim>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("current_searchable_trims");
            entity.Property(value => value.MsrpAmount).HasPrecision(19, 2);
            entity.Property(value => value.CurrentPriceAmount).HasPrecision(19, 2);
            entity.Property(value => value.OnRoadMinAmount).HasPrecision(19, 2);
            entity.Property(value => value.OnRoadMaxAmount).HasPrecision(19, 2);
            entity.Property(value => value.Seats).HasPrecision(18, 6);
            entity.Property(value => value.LengthMm).HasPrecision(18, 6);
            entity.Property(value => value.WidthMm).HasPrecision(18, 6);
            entity.Property(value => value.HeightMm).HasPrecision(18, 6);
            entity.Property(value => value.WheelbaseMm).HasPrecision(18, 6);
            entity.Property(value => value.OfficialRangeKm).HasPrecision(18, 3);
            entity.Property(value => value.UsableBatteryKwh).HasPrecision(18, 6);
            entity.Property(value => value.FuelLitresPer100Km)
                .HasColumnName("fuel_litres_per100_km")
                .HasPrecision(18, 6);
            entity.Property(value => value.ElectricKwhPer100Km)
                .HasColumnName("electric_kwh_per100_km")
                .HasPrecision(18, 6);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(entityType => typeof(Entity).IsAssignableFrom(entityType.ClrType)))
        {
            entityType.FindProperty(nameof(Entity.CreatedAt))?.SetDefaultValueSql("CURRENT_TIMESTAMP");
            entityType.FindProperty(nameof(Entity.UpdatedAt))?.SetDefaultValueSql("CURRENT_TIMESTAMP");
        }
    }
}
