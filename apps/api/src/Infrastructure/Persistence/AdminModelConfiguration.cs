using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Admin;

namespace VietnamCarPlatform.Infrastructure.Persistence;

internal static class AdminModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("admin_users", table =>
            {
                table.HasCheckConstraint("ck_admin_users_email", "normalized_email <> ''");
                table.HasCheckConstraint("ck_admin_users_failed_login", "failed_login_count >= 0");
            });
            entity.Property(value => value.Email).HasMaxLength(320);
            entity.Property(value => value.NormalizedEmail).HasMaxLength(320);
            entity.Property(value => value.DisplayName).HasMaxLength(240);
            entity.Property(value => value.PasswordHash).HasMaxLength(1000);
            entity.HasIndex(value => value.NormalizedEmail).IsUnique();
            entity.HasIndex(value => new { value.Active, value.Role });
        });

        modelBuilder.Entity<AdminSession>(entity =>
        {
            entity.ToTable("admin_sessions", table => table.HasCheckConstraint(
                "ck_admin_sessions_expiry",
                "expires_at > created_at"));
            entity.Property(value => value.TokenHash).HasMaxLength(128);
            entity.Property(value => value.ClientFingerprintHash).HasMaxLength(128);
            entity.HasOne<AdminUser>().WithMany().HasForeignKey(value => value.AdminUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(value => value.TokenHash).IsUnique();
            entity.HasIndex(value => new { value.AdminUserId, value.ExpiresAt });
        });

        modelBuilder.Entity<FieldLock>(entity =>
        {
            entity.ToTable("field_locks", table => table.HasCheckConstraint(
                "ck_field_locks_reason",
                "NULLIF(BTRIM(reason), '') IS NOT NULL"));
            entity.Property(value => value.EntityType).HasMaxLength(160);
            entity.Property(value => value.FieldPath).HasMaxLength(500);
            entity.Property(value => value.Reason).HasMaxLength(2000);
            entity.Property(value => value.Actor).HasMaxLength(320);
            entity.HasIndex(value => new { value.EntityType, value.EntityId, value.FieldPath, value.Active });
        });

        modelBuilder.Entity<ManualImport>(entity =>
        {
            entity.ToTable("manual_imports", table => table.HasCheckConstraint(
                "ck_manual_imports_reason",
                "NULLIF(BTRIM(reason), '') IS NOT NULL"));
            entity.Property(value => value.FileName).HasMaxLength(500);
            entity.Property(value => value.Format).HasMaxLength(20);
            entity.Property(value => value.ContentHash).HasMaxLength(128);
            entity.Property(value => value.ContentText).HasColumnType("text");
            entity.Property(value => value.ValidationReportJson).HasColumnType("jsonb");
            entity.Property(value => value.SubmittedBy).HasMaxLength(320);
            entity.Property(value => value.Reason).HasMaxLength(2000);
            entity.HasIndex(value => value.ContentHash);
            entity.HasIndex(value => new { value.Status, value.SubmittedAt });
        });
    }
}
