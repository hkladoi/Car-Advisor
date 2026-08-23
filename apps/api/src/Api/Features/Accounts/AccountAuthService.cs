using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Accounts;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Accounts;

public interface IAccountAuthService
{
    Task<AccountAuthResponse> RegisterAsync(AccountRegisterRequest request, HttpContext context, CancellationToken cancellationToken);
    Task<AccountAuthResponse?> LoginAsync(AccountLoginRequest request, HttpContext context, CancellationToken cancellationToken);
    Task<AccountActor?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken);
    Task LogoutAsync(AccountActor actor, CancellationToken cancellationToken);
    Task DeleteAsync(AccountActor actor, AccountDeleteRequest request, CancellationToken cancellationToken);
}

public sealed class AccountAuthService(
    AppDbContext database,
    TimeProvider timeProvider) : IAccountAuthService
{
    public const string PrivacyPolicyVersion = "2026-08-v1";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan LoginLockout = TimeSpan.FromMinutes(15);

    public async Task<AccountAuthResponse> RegisterAsync(
        AccountRegisterRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!request.PrivacyConsent)
        {
            throw Error(400, "ACCOUNT_CONSENT_REQUIRED", "Explicit privacy consent is required before profile data can be persisted.");
        }
        var email = ValidateEmail(request.Email);
        ValidatePassword(request.Password);
        var displayName = request.DisplayName.Trim();
        if (displayName.Length is < 2 or > 80)
        {
            throw Error(400, "ACCOUNT_DISPLAY_NAME_INVALID", "Display name must contain 2 to 80 characters.");
        }
        var normalized = NormalizeEmail(email);
        if (await database.UserAccounts.AnyAsync(value => value.NormalizedEmail == normalized, cancellationToken))
        {
            throw Error(409, "ACCOUNT_EMAIL_EXISTS", "An account already exists for this email address.");
        }

        var now = timeProvider.GetUtcNow();
        var user = new UserAccount
        {
            Email = email,
            NormalizedEmail = normalized,
            DisplayName = displayName,
            PasswordHash = AccountPasswordHasher.Hash(request.Password),
            Active = true,
            ConsentedAt = now,
            PrivacyPolicyVersion = PrivacyPolicyVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.UserAccounts.Add(user);
        var response = CreateSession(user, context, now);
        await database.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AccountAuthResponse?> LoginAsync(
        AccountLoginRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(request.Email);
        var now = timeProvider.GetUtcNow();
        var user = await database.UserAccounts.SingleOrDefaultAsync(value => value.NormalizedEmail == normalized, cancellationToken);
        if (user is null || !user.Active || user.LockedUntil > now || !AccountPasswordHasher.Verify(request.Password, user.PasswordHash))
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
        var response = CreateSession(user, context, now);
        await database.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AccountActor?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[7..].Trim();
        if (token.Length < 40) return null;
        var tokenHash = Hash(token);
        var now = timeProvider.GetUtcNow();
        var match = await (
                from session in database.UserSessions
                join user in database.UserAccounts on session.UserAccountId equals user.Id
                where session.TokenHash == tokenHash
                    && session.RevokedAt == null
                    && session.ExpiresAt > now
                    && user.Active
                select new { Session = session, User = user })
            .SingleOrDefaultAsync(cancellationToken);
        if (match is null) return null;
        if (now - match.Session.LastSeenAt >= TimeSpan.FromMinutes(5))
        {
            match.Session.LastSeenAt = now;
            match.Session.UpdatedAt = now;
            await database.SaveChangesAsync(cancellationToken);
        }
        return new AccountActor(
            match.User.Id,
            match.Session.Id,
            match.User.Email,
            match.User.DisplayName,
            match.Session.ExpiresAt);
    }

    public async Task LogoutAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var session = await database.UserSessions.SingleOrDefaultAsync(value => value.Id == actor.SessionId, cancellationToken);
        if (session is null) return;
        var now = timeProvider.GetUtcNow();
        session.RevokedAt = now;
        session.UpdatedAt = now;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(AccountActor actor, AccountDeleteRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Confirmation.Trim(), "DELETE", StringComparison.Ordinal))
        {
            throw Error(400, "ACCOUNT_DELETE_CONFIRMATION_INVALID", "Type DELETE exactly to confirm permanent account deletion.");
        }
        var user = await database.UserAccounts.SingleAsync(value => value.Id == actor.UserId, cancellationToken);
        if (!AccountPasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw Error(401, "ACCOUNT_DELETE_PASSWORD_INVALID", "The current password is invalid.");
        }
        var ownerSubjectId = actor.UserId.ToString("D");
        var profiles = await database.AffordabilityProfiles
            .Where(value => value.OwnerSubjectId == ownerSubjectId)
            .ToArrayAsync(cancellationToken);
        database.AffordabilityProfiles.RemoveRange(profiles);
        database.UserAccounts.Remove(user);
        await database.SaveChangesAsync(cancellationToken);
    }

    private AccountAuthResponse CreateSession(UserAccount user, HttpContext context, DateTimeOffset now)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(48));
        var session = new UserSession
        {
            UserAccountId = user.Id,
            TokenHash = Hash(token),
            ExpiresAt = now.Add(SessionLifetime),
            LastSeenAt = now,
            ClientFingerprintHash = Fingerprint(context),
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.UserSessions.Add(session);
        return new AccountAuthResponse(token, session.ExpiresAt, user.Id, user.Email, user.DisplayName);
    }

    private static string ValidateEmail(string value)
    {
        var candidate = value.Trim();
        if (candidate.Length > 320)
        {
            throw Error(400, "ACCOUNT_EMAIL_INVALID", "Email address is invalid.");
        }
        try
        {
            var parsed = new MailAddress(candidate);
            if (!string.Equals(parsed.Address, candidate, StringComparison.OrdinalIgnoreCase)) throw new FormatException();
            return candidate;
        }
        catch (FormatException)
        {
            throw Error(400, "ACCOUNT_EMAIL_INVALID", "Email address is invalid.");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length is < 12 or > 256
            || !password.Any(char.IsLetter)
            || !password.Any(char.IsDigit))
        {
            throw Error(400, "ACCOUNT_PASSWORD_WEAK", "Password must contain 12 to 256 characters, including a letter and a number.");
        }
    }

    private static string NormalizeEmail(string value) => value.Trim().ToUpperInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Fingerprint(HttpContext context) => Hash($"{context.Connection.RemoteIpAddress}|{context.Request.Headers.UserAgent}");
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static AccountOperationException Error(int status, string code, string message) => new(status, code, message);
}

public static class AccountPasswordHasher
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
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha512" || !int.TryParse(parts[1], out var iterations) || iterations < 100_000)
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
