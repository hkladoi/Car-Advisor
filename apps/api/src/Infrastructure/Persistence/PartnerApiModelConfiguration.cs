using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Partners;

namespace VietnamCarPlatform.Infrastructure.Persistence;

internal static class PartnerApiModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PartnerApiUsagePlan>(entity =>
        {
            entity.ToTable("partner_api_usage_plans", table =>
            {
                table.HasCheckConstraint(
                    "ck_partner_api_usage_plans_limits",
                    "requests_per_minute > 0 AND requests_per_month > 0 AND max_page_size BETWEEN 1 AND 100");
                table.HasCheckConstraint(
                    "ck_partner_api_usage_plans_code",
                    "code ~ '^[a-z][a-z0-9-]{2,31}$'");
            });
            entity.Property(value => value.Code).HasMaxLength(32);
            entity.Property(value => value.Name).HasMaxLength(120);
            entity.HasIndex(value => value.Code).IsUnique();
            entity.HasIndex(value => new { value.Active, value.Code });
        });

        modelBuilder.Entity<PartnerApiKey>(entity =>
        {
            entity.ToTable("partner_api_keys", table =>
            {
                table.HasCheckConstraint(
                    "ck_partner_api_keys_hash",
                    "key_hash ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint(
                    "ck_partner_api_keys_prefix",
                    "key_prefix ~ '^vcp_v1_[A-Za-z0-9_-]{10}$'");
                table.HasCheckConstraint(
                    "ck_partner_api_keys_scope",
                    "scope = 'catalog.read'");
                table.HasCheckConstraint(
                    "ck_partner_api_keys_policy",
                    "NULLIF(BTRIM(policy_version), '') IS NOT NULL");
                table.HasCheckConstraint(
                    "ck_partner_api_keys_expiry",
                    "expires_at IS NULL OR expires_at > issued_at");
                table.HasCheckConstraint(
                    "ck_partner_api_keys_revocation",
                    "(revoked_at IS NULL AND revoked_by IS NULL AND revocation_reason IS NULL) OR "
                    + "(revoked_at IS NOT NULL AND NULLIF(BTRIM(revoked_by), '') IS NOT NULL "
                    + "AND NULLIF(BTRIM(revocation_reason), '') IS NOT NULL)");
            });
            entity.Property(value => value.Name).HasMaxLength(160);
            entity.Property(value => value.KeyPrefix).HasMaxLength(17);
            entity.Property(value => value.KeyHash).HasMaxLength(64);
            entity.Property(value => value.Scope).HasMaxLength(80);
            entity.Property(value => value.PolicyVersion).HasMaxLength(32);
            entity.Property(value => value.IssuedBy).HasMaxLength(320);
            entity.Property(value => value.RevokedBy).HasMaxLength(320);
            entity.Property(value => value.RevocationReason).HasMaxLength(2000);
            entity.HasOne<PartnerApiUsagePlan>()
                .WithMany()
                .HasForeignKey(value => value.UsagePlanId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(value => value.KeyPrefix).IsUnique();
            entity.HasIndex(value => value.KeyHash).IsUnique();
            entity.HasIndex(value => new { value.UsagePlanId, value.RevokedAt, value.ExpiresAt });
        });
    }
}
