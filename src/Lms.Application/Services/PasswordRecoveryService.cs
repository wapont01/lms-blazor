using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Lms.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Application.Services;

public sealed class PasswordRecoveryOptions
{
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = string.Empty;
}

public interface IPasswordRecoveryService
{
    Task RequestAsync(string email, CancellationToken ct = default);
    Task<bool> ResetAsync(Guid id, string token, string password, CancellationToken ct = default);
}

public sealed class PasswordRecoveryService(
    ApplicationDbContext db, IEmailService email, PasswordRecoveryOptions options,
    IAuditLogService audit, TimeProvider clock, ILogger<PasswordRecoveryService> logger) : IPasswordRecoveryService
{
    public const string Purpose = "password-reset";

    public async Task RequestAsync(string address, CancellationToken ct = default)
    {
        if (!options.Enabled)
            throw new InvalidOperationException("Password recovery is unavailable.");
        var normalized = address.Trim().ToLowerInvariant();
        var user = await db.UserAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Email == normalized && item.IsActive, ct);
        if (user is null || await db.ExternalIdentities.AnyAsync(item => item.UserAccountId == user.Id, ct))
        {
            logger.LogInformation("Password recovery request had no eligible password account.");
            return;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var cooldown = now.AddMinutes(29);
        if (await db.EmailChallenges.AnyAsync(item => item.UserAccountId == user.Id && item.Purpose == Purpose
                && item.ExpiresAtUtc > cooldown, ct))
        {
            logger.LogInformation("Password recovery request suppressed by account cooldown.");
            return;
        }
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = new Lms.Domain.Entities.EmailChallenge
        {
            UserAccountId = user.Id, Purpose = Purpose,
            SecretHash = Hash(secret, user.PasswordHash, user.Email), ExpiresAtUtc = now.AddMinutes(30)
        };
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.EmailChallenges.Where(item => item.UserAccountId == user.Id && item.Purpose == Purpose
                && item.ConsumedAtUtc == null).ExecuteUpdateAsync(update =>
                    update.SetProperty(item => item.ConsumedAtUtc, now), ct);
            db.EmailChallenges.Add(challenge);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var link = HtmlEncoder.Default.Encode(
            $"{options.PublicBaseUrl.TrimEnd('/')}/reset-password?id={challenge.Id}&token={secret}");
        var sent = await email.SendAsync(new EmailMessage(user.Email, "Reset your LMS password",
            $"<h1>Reset your password</h1><p><a href=\"{link}\">Choose a new password</a></p>"
            + "<p>This link expires in 30 minutes and works once. If you did not request a reset, ignore this email. Your password has not changed.</p>"));
        if (!sent)
        {
            await db.EmailChallenges.Where(item => item.Id == challenge.Id).ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.ConsumedAtUtc, now), ct);
            logger.LogError("Password recovery email delivery failed for account {UserId}.", user.Id);
        }
        await audit.WriteAsync(user.Id, user.Email, sent ? "auth.password-reset.sent" : "auth.password-reset.delivery-failed",
            "UserAccount", user.Id);
    }

    public async Task<bool> ResetAsync(Guid id, string token, string password, CancellationToken ct = default)
    {
        UserAccountService.EnsurePasswordComplexity(password);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var challenge = await db.EmailChallenges.AsNoTracking().Include(item => item.UserAccount)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        var user = challenge?.UserAccount;
        if (!options.Enabled || challenge is null || user is null || !user.IsActive
            || challenge.Purpose != Purpose || challenge.ConsumedAtUtc.HasValue || challenge.ExpiresAtUtc <= now
            || challenge.FailedAttempts >= 5 || token.Length != 64
            || await db.ExternalIdentities.AnyAsync(item => item.UserAccountId == user.Id, ct)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge.SecretHash),
                Encoding.ASCII.GetBytes(Hash(token, user.PasswordHash, user.Email))))
        {
            if (challenge is not null && challenge.Purpose == Purpose && challenge.ConsumedAtUtc is null)
                await db.EmailChallenges.Where(item => item.Id == id).ExecuteUpdateAsync(update =>
                    update.SetProperty(item => item.FailedAttempts, item => item.FailedAttempts + 1), ct);
            logger.LogWarning("Rejected invalid, expired, or ineligible password reset.");
            await transaction.CommitAsync(ct);
            return false;
        }
        var passwordHash = UserAccountService.HashPassword(password);
        await db.UserAccounts.Where(item => item.Id == user.Id).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.PasswordHash, passwordHash)
            .SetProperty(item => item.PasswordUpdatedAt, now)
            .SetProperty(item => item.PasswordExpiresAt, now.AddDays(90))
            .SetProperty(item => item.ForcePasswordChange, false)
            .SetProperty(item => item.FailedLoginCount, 0)
            .SetProperty(item => item.LockoutEndUtc, (DateTime?)null), ct);
        await db.EmailChallenges.Where(item => item.UserAccountId == user.Id && item.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.ConsumedAtUtc, now), ct);
        await audit.WriteAsync(user.Id, user.Email, "auth.password-reset.completed", "UserAccount", user.Id);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static string Hash(string token, string passwordHash, string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{token}\n{passwordHash}\n{email}")));
}
