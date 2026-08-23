using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Admin;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Admin;

public interface IAdminAuthService
{
    Task EnsureBootstrapAsync(CancellationToken cancellationToken);
    Task<AdminLoginResponse?> LoginAsync(AdminLoginRequest request, HttpContext context, CancellationToken cancellationToken);
    Task<AdminActor?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken);
    Task LogoutAsync(AdminActor actor, string reason, HttpContext context, CancellationToken cancellationToken);
}

public sealed class AdminAuthService(
    AppDbContext database,
    IConfiguration configuration,
    TimeProvider timeProvider) : IAdminAuthService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan LoginLockout = TimeSpan.FromMinutes(15);

    public async Task EnsureBootstrapAsync(CancellationToken cancellationToken)
    {
        var email = configuration["ADMIN_BOOTSTRAP_EMAIL"]?.Trim();
        var password = configuration["ADMIN_BOOTSTRAP_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }
        if (password.Length < 14)
        {
            throw new InvalidOperationException("ADMIN_BOOTSTRAP_PASSWORD must contain at least 14 characters.");
        }

        var normalized = NormalizeEmail(email);
        if (await database.AdminUsers.AnyAsync(user => user.NormalizedEmail == normalized, cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var user = new AdminUser
        {
            Email = email,
            NormalizedEmail = normalized,
            DisplayName = "Bootstrap administrator",
            PasswordHash = AdminPasswordHasher.Hash(password),
            Role = AdministratorRole.Administrator,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.AdminUsers.Add(user);
        database.AuditEvents.Add(new AuditEvent
        {
            Actor = "system:bootstrap",
            Action = "AdminUserCreated",
            EntityType = "AdminUser",
            EntityId = user.Id,
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { user.Email, Role = user.Role.ToString() }),
            Reason = "Initial administrator bootstrap from server-side environment configuration.",
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminLoginResponse?> LoginAsync(
        AdminLoginRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalized = NormalizeEmail(request.Email);
        var user = await database.AdminUsers.SingleOrDefaultAsync(value => value.NormalizedEmail == normalized, cancellationToken);
        if (user is null || !user.Active || user.LockedUntil > now || !AdminPasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null && user.Active)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= 5)
                {
                    user.LockedUntil = now.Add(LoginLockout);
                    user.FailedLoginCount = 0;
                }
                user.UpdatedAt = now;
                await database.SaveChangesAsync(cancellationToken);
            }
            return null;
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        var token = Base64Url(RandomNumberGenerator.GetBytes(48));
        var session = new AdminSession
        {
            AdminUserId = user.Id,
            TokenHash = Hash(token),
            ExpiresAt = now.Add(SessionLifetime),
            LastSeenAt = now,
            ClientFingerprintHash = Fingerprint(context),
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.AdminSessions.Add(session);
        database.AuditEvents.Add(Audit(user.Email, "AdminLogin", "AdminSession", session.Id, "Administrator authenticated and session was rotated.", context, now));
        await database.SaveChangesAsync(cancellationToken);
        return new AdminLoginResponse(token, session.ExpiresAt, user.Id, user.Email, user.DisplayName, user.Role.ToString());
    }

    public async Task<AdminActor?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var token = authorization[7..].Trim();
        if (token.Length < 40)
        {
            return null;
        }
        var tokenHash = Hash(token);
        var now = timeProvider.GetUtcNow();
        var match = await (
                from session in database.AdminSessions
                join user in database.AdminUsers on session.AdminUserId equals user.Id
                where session.TokenHash == tokenHash
                    && session.RevokedAt == null
                    && session.ExpiresAt > now
                    && user.Active
                select new { Session = session, User = user })
            .SingleOrDefaultAsync(cancellationToken);
        if (match is null)
        {
            return null;
        }
        if (now - match.Session.LastSeenAt >= TimeSpan.FromMinutes(5))
        {
            match.Session.LastSeenAt = now;
            match.Session.UpdatedAt = now;
            await database.SaveChangesAsync(cancellationToken);
        }
        return new AdminActor(
            match.User.Id,
            match.Session.Id,
            match.User.Email,
            match.User.DisplayName,
            match.User.Role,
            match.Session.ExpiresAt);
    }

    public async Task LogoutAsync(
        AdminActor actor,
        string reason,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var session = await database.AdminSessions.SingleAsync(value => value.Id == actor.SessionId, cancellationToken);
        session.RevokedAt = now;
        session.UpdatedAt = now;
        database.AuditEvents.Add(Audit(actor.Email, "AdminLogout", "AdminSession", session.Id, reason, context, now));
        await database.SaveChangesAsync(cancellationToken);
    }

    private static AuditEvent Audit(
        string actor,
        string action,
        string entityType,
        Guid entityId,
        string reason,
        HttpContext context,
        DateTimeOffset now) => new()
        {
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Reason = reason,
            OccurredAt = now,
            CorrelationId = context.TraceIdentifier,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static string NormalizeEmail(string value) => value.Trim().ToUpperInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Fingerprint(HttpContext context) => Hash($"{context.Connection.RemoteIpAddress}|{context.Request.Headers.UserAgent}");
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class AdminPasswordHasher
{
    private const int Iterations = 310_000;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(24);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, 48);
        return $"pbkdf2-sha512${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4
            || parts[0] != "pbkdf2-sha512"
            || !int.TryParse(parts[1], out var iterations)
            || iterations < 100_000)
        {
            return false;
        }
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
