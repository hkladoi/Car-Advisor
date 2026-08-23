using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Accounts;
using VietnamCarPlatform.Domain.Catalog;

namespace VietnamCarPlatform.Infrastructure.Persistence;

internal static class AccountModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("user_accounts", table =>
            {
                table.HasCheckConstraint("ck_user_accounts_email", "normalized_email <> ''");
                table.HasCheckConstraint("ck_user_accounts_consent", "consented_at >= created_at AND privacy_policy_version <> ''");
                table.HasCheckConstraint("ck_user_accounts_failed_login", "failed_login_count >= 0");
            });
            entity.Property(value => value.Email).HasMaxLength(320);
            entity.Property(value => value.NormalizedEmail).HasMaxLength(320);
            entity.Property(value => value.DisplayName).HasMaxLength(240);
            entity.Property(value => value.PasswordHash).HasMaxLength(1000);
            entity.Property(value => value.PrivacyPolicyVersion).HasMaxLength(40);
            entity.HasIndex(value => value.NormalizedEmail).IsUnique();
            entity.HasIndex(value => value.Active);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions", table => table.HasCheckConstraint(
                "ck_user_sessions_expiry",
                "expires_at > created_at"));
            entity.Property(value => value.TokenHash).HasMaxLength(128);
            entity.Property(value => value.ClientFingerprintHash).HasMaxLength(128);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(value => value.UserAccountId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => value.TokenHash).IsUnique();
            entity.HasIndex(value => new { value.UserAccountId, value.ExpiresAt });
        });

        modelBuilder.Entity<SavedComparison>(entity =>
        {
            entity.ToTable("saved_comparisons", table => table.HasCheckConstraint(
                "ck_saved_comparisons_trim_ids",
                "jsonb_typeof(trim_ids_json) = 'array' AND jsonb_array_length(trim_ids_json) BETWEEN 2 AND 4"));
            entity.Property(value => value.Name).HasMaxLength(160);
            entity.Property(value => value.TrimIdsJson).HasColumnType("jsonb");
            entity.Property(value => value.RegionCode).HasMaxLength(20);
            entity.Property(value => value.ProfilePreset).HasMaxLength(80);
            entity.Property(value => value.FinancingPreset).HasMaxLength(80);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(value => value.UserAccountId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.UserAccountId, value.UpdatedAt });
        });

        modelBuilder.Entity<WatchlistEntry>(entity =>
        {
            entity.ToTable("watchlist_entries", table => table.HasCheckConstraint(
                "ck_watchlist_entries_target_price",
                "target_price IS NULL OR target_price >= 0"));
            entity.Property(value => value.RegionCode).HasMaxLength(20);
            entity.Property(value => value.TargetPrice).HasPrecision(19, 2);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(value => value.UserAccountId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Trim>().WithMany().HasForeignKey(value => value.TrimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => new { value.UserAccountId, value.TrimId }).IsUnique();
            entity.HasIndex(value => new { value.TrimId, value.PriceAlerts, value.PromotionAlerts, value.DealerOfferAlerts });
        });
    }
}
