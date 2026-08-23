using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Domain.Sources;

namespace VietnamCarPlatform.Infrastructure.Persistence;

internal static class CommerceModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Price>(entity =>
        {
            entity.ToTable("prices", table =>
            {
                table.HasCheckConstraint("ck_prices_effective_period", EffectivePeriodCheck());
                table.HasCheckConstraint("ck_prices_amount_semantics", "(price_type = 'Unannounced' AND amount IS NULL) OR (price_type <> 'Unannounced' AND amount IS NOT NULL AND amount > 0)");
                table.HasCheckConstraint("ck_prices_official_provenance", OfficialProvenanceCheck("status"));
                table.HasCheckConstraint("ck_prices_version", "version > 0");
            });
            entity.Property(value => value.Amount).HasPrecision(19, 2);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.RegionScope).HasMaxLength(120);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => new { value.TrimId, value.PriceType, value.RegionScope, value.Version }).IsUnique();
            entity.HasIndex(value => new { value.TrimId, value.EffectiveFrom });
        });

        modelBuilder.Entity<PriceHistory>(entity =>
        {
            entity.ToTable("price_history", table => table.HasCheckConstraint(
                "ck_price_history_effective_period",
                EffectivePeriodCheck()));
            entity.Property(value => value.Amount).HasPrecision(19, 2);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.RegionScope).HasMaxLength(120);
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SourceFact>().WithMany().HasForeignKey(value => value.SourceFactId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.PriceId, value.ArchivedAt });
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.ToTable("promotions", table =>
            {
                table.HasCheckConstraint("ck_promotions_effective_period", EffectivePeriodCheck());
                table.HasCheckConstraint("ck_promotions_scope", "(trim_id IS NOT NULL) <> (brand_id IS NOT NULL)");
                table.HasCheckConstraint("ck_promotions_value", "value IS NULL OR value >= 0");
                table.HasCheckConstraint("ck_promotions_published_provenance", PublishedProvenanceCheck("status"));
            });
            entity.Property(value => value.Value).HasPrecision(19, 2);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.ConditionsJson).HasColumnType("jsonb");
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Brand>().WithMany().HasForeignKey(value => value.BrandId).OnDelete(DeleteBehavior.Cascade);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => value.ConditionsJson).HasMethod("gin").HasOperators("jsonb_path_ops");
            entity.HasIndex(value => new { value.TrimId, value.EffectiveFrom });
            entity.HasIndex(value => new { value.BrandId, value.EffectiveFrom });
        });

        modelBuilder.Entity<Dealer>(entity =>
        {
            entity.ToTable("dealers");
            entity.Property(value => value.Name).HasMaxLength(240);
            entity.Property(value => value.Slug).HasMaxLength(260);
            entity.Property(value => value.OfficialUrl).HasMaxLength(2048);
            entity.HasOne<Brand>().WithMany().HasForeignKey(value => value.BrandId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.BrandId, value.Slug }).IsUnique();
        });

        modelBuilder.Entity<DealerBranch>(entity =>
        {
            entity.ToTable("dealer_branches", table =>
            {
                table.HasCheckConstraint("ck_dealer_branches_latitude", "latitude IS NULL OR latitude BETWEEN -90 AND 90");
                table.HasCheckConstraint("ck_dealer_branches_longitude", "longitude IS NULL OR longitude BETWEEN -180 AND 180");
            });
            entity.Property(value => value.Name).HasMaxLength(240);
            entity.Property(value => value.ProvinceCode).HasMaxLength(20);
            entity.Property(value => value.Address).HasMaxLength(1000);
            entity.Property(value => value.Latitude).HasPrecision(9, 6);
            entity.Property(value => value.Longitude).HasPrecision(9, 6);
            entity.HasOne<Dealer>().WithMany().HasForeignKey(value => value.DealerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Region>().WithMany().HasForeignKey(value => value.ProvinceCode).HasPrincipalKey(value => value.Code).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => new { value.DealerId, value.Name, value.ProvinceCode }).IsUnique();
        });

        modelBuilder.Entity<DealerOffer>(entity =>
        {
            entity.ToTable("dealer_offers", table =>
            {
                table.HasCheckConstraint("ck_dealer_offers_effective_period", EffectivePeriodCheck());
                table.HasCheckConstraint("ck_dealer_offers_published_provenance", PublishedProvenanceCheck("status"));
            });
            entity.Property(value => value.Headline).HasMaxLength(500);
            entity.Property(value => value.CombinabilityGroup).HasMaxLength(100);
            entity.Property(value => value.ConditionsJson).HasColumnType("jsonb");
            entity.Property(value => value.ManualOverrideReason).HasMaxLength(1000);
            entity.HasOne<DealerBranch>().WithMany().HasForeignKey(value => value.BranchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Restrict);
            ConfigureSourceFact(entity);
            entity.HasIndex(value => value.ConditionsJson).HasMethod("gin").HasOperators("jsonb_path_ops");
            entity.HasIndex(value => new { value.BranchId, value.TrimId, value.EffectiveFrom });
        });

        modelBuilder.Entity<DealerOfferBenefit>(entity =>
        {
            entity.ToTable("dealer_offer_benefits", table =>
            {
                table.HasCheckConstraint("ck_dealer_offer_benefits_cash_value", "cash_value IS NULL OR cash_value >= 0");
                table.HasCheckConstraint("ck_dealer_offer_benefits_stated_value", "stated_value IS NULL OR stated_value >= 0");
                table.HasCheckConstraint("ck_dealer_offer_benefits_cash_equivalent", "NOT is_cash_equivalent OR cash_value IS NOT NULL");
            });
            entity.Property(value => value.CashValue).HasPrecision(19, 2);
            entity.Property(value => value.StatedValue).HasPrecision(19, 2);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.ExclusivityGroup).HasMaxLength(100);
            entity.Property(value => value.Note).HasMaxLength(1000);
            entity.HasOne<DealerOffer>().WithMany().HasForeignKey(value => value.OfferId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.OfferId, value.Type, value.ExclusivityGroup });
        });
    }

    private static void ConfigureSourceFact<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.HasOne<SourceFact>().WithMany().HasForeignKey("SourceFactId").OnDelete(DeleteBehavior.Restrict);
    }

    private static string EffectivePeriodCheck() => "effective_to IS NULL OR effective_from < effective_to";

    private static string OfficialProvenanceCheck(string statusColumn) =>
        $"{statusColumn} <> 'Official' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL";

    private static string PublishedProvenanceCheck(string statusColumn) =>
        $"{statusColumn} <> 'Published' OR source_fact_id IS NOT NULL OR NULLIF(BTRIM(manual_override_reason), '') IS NOT NULL";
}
