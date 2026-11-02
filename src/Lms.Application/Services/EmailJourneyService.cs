using System.Security.Cryptography;
using System.Text;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public sealed class EmailJourneyOptions
{
    public bool Enabled { get; set; }
    public string TestRecipient { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public bool IncludeCertificateAttachment { get; set; }

    public bool AppliesTo(string email) => Enabled
        && string.Equals(email.Trim(), TestRecipient, StringComparison.OrdinalIgnoreCase);
}

public interface IEmailJourneyService
{
    Task<Guid?> SendVerificationAsync(UserAccount user, CancellationToken ct = default);
    Task<Guid?> SendLoginCodeAsync(UserAccount user, CancellationToken ct = default);
    Task<UserAccount?> ConsumeAsync(Guid id, string purpose, string secret, CancellationToken ct = default);
    Task<bool> SendCertificateAsync(UserAccount user, CancellationToken ct = default);
    Task<DevelopmentCertificate?> GetCertificateAsync(Guid userId, CancellationToken ct = default);
}

public sealed class EmailJourneyService(
    ApplicationDbContext db, IEmailService email, EmailJourneyOptions options,
    IAuditLogService audit, TimeProvider clock) : IEmailJourneyService
{
    public const string VerifyPurpose = "verify-email";
    public const string LoginPurpose = "login-code";

    public Task<Guid?> SendVerificationAsync(UserAccount user, CancellationToken ct = default) =>
        IssueAsync(user, VerifyPurpose, ct);

    public Task<Guid?> SendLoginCodeAsync(UserAccount user, CancellationToken ct = default) =>
        IssueAsync(user, LoginPurpose, ct);

    private async Task<Guid?> IssueAsync(UserAccount user, string purpose, CancellationToken ct)
    {
        if (!options.AppliesTo(user.Email) || !user.IsActive || user.Role != "Learner")
            throw new InvalidOperationException("Email journey is restricted to the configured synthetic student.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (purpose == LoginPurpose && (user.EmailVerifiedAtUtc is null || !user.EmailTwoStepEnabled))
            throw new InvalidOperationException("Email must be verified before requesting a login code.");
        await db.EmailChallenges.Where(item => item.UserAccountId == user.Id && item.Purpose == purpose
                && item.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.ConsumedAtUtc, now), ct);
        var secret = purpose == VerifyPurpose
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            : RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new EmailChallenge
        {
            UserAccountId = user.Id, Purpose = purpose, SecretHash = Hash(secret),
            ExpiresAtUtc = now.AddMinutes(purpose == VerifyPurpose ? 30 : 10)
        };
        db.EmailChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        var link = $"{options.PublicBaseUrl.TrimEnd('/')}/email-test?step=verify&id={challenge.Id}&token={secret}";
        var message = purpose == VerifyPurpose
            ? new EmailMessage(user.Email, "Verify your LMS email",
                $"<h1>Verify your email</h1><p>Synthetic student registration.</p><p><a href=\"{link}\">Verify email address</a></p><p>This link expires in 30 minutes and works once. Email verification does not verify a professional license.</p>")
            : new EmailMessage(user.Email, "Your LMS sign-in code",
                $"<h1>Your sign-in code</h1><p><strong>{secret}</strong></p><p>Expires in 10 minutes. Do not share this code. If you did not request it, ignore this email.</p>");
        var sent = await email.SendAsync(message);
        if (!sent)
        {
            await db.EmailChallenges.Where(item => item.Id == challenge.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.ConsumedAtUtc, now), ct);
        }
        await audit.WriteAsync(user.Id, user.Email, sent ? $"auth.{purpose}.sent" : $"auth.{purpose}.delivery-failed",
            "UserAccount", user.Id);
        return sent ? challenge.Id : null;
    }

    public async Task<UserAccount?> ConsumeAsync(Guid id, string purpose, string secret, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var challenge = await db.EmailChallenges.AsNoTracking().Include(item => item.UserAccount)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        var user = challenge?.UserAccount;
        if (challenge is null || user is null || !user.IsActive || user.Role != "Learner"
            || !options.AppliesTo(user.Email) || challenge.Purpose != purpose
            || challenge.ConsumedAtUtc.HasValue || challenge.ExpiresAtUtc <= now || challenge.FailedAttempts >= 5
            || (purpose == LoginPurpose && (user.EmailVerifiedAtUtc is null || !user.EmailTwoStepEnabled))
            || (user.LockoutEndUtc > now) || (user.PasswordExpiresAt <= now))
        {
            await audit.WriteAsync(null, "system@lms.local", $"auth.{purpose}.rejected", "EmailChallenge", id);
            await transaction.CommitAsync(ct);
            return null;
        }
        var valid = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(challenge.SecretHash), Convert.FromHexString(Hash(secret)));
        if (!valid)
        {
            await db.EmailChallenges.Where(item => item.Id == id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.FailedAttempts, item => item.FailedAttempts + 1), ct);
            await audit.WriteAsync(user.Id, user.Email, $"auth.{purpose}.rejected", "EmailChallenge", id);
            await transaction.CommitAsync(ct);
            return null;
        }
        await db.EmailChallenges.Where(item => item.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.ConsumedAtUtc, now), ct);
        if (purpose == VerifyPurpose)
        {
            user.EmailVerifiedAtUtc = now;
            user.EmailTwoStepEnabled = true;
            await db.UserAccounts.Where(item => item.Id == user.Id).ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.EmailVerifiedAtUtc, now).SetProperty(item => item.EmailTwoStepEnabled, true), ct);
            var confirmed = await db.RosterAccountRequests
                .Where(item => item.UserAccountId == user.Id && item.Status == "Pending")
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, "Confirmed"), ct);
            if (confirmed > 0)
                await audit.WriteAsync(user.Id, user.Email, "roster-request.confirmed", "UserAccount", user.Id,
                    "Student's signup selection confirmed after email verification. Commission data unchanged; no verified ownership asserted.");
        }
        await audit.WriteAsync(user.Id, user.Email, $"auth.{purpose}.verified", "UserAccount", user.Id);
        await transaction.CommitAsync(ct);
        return user;
    }

    public async Task<DevelopmentCertificate?> GetCertificateAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null || !options.AppliesTo(user.Email) || !user.IsActive || user.EmailVerifiedAtUtc is null)
            return null;
        return new DevelopmentCertificate(user.Id, user.Email, "Taylor Test Student", "SYNTHETIC-0001",
            "Florida", "Ethics at Work", "3 simulated hours - NOT VALID FOR CREDIT",
            new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)));
    }

    public async Task<bool> SendCertificateAsync(UserAccount user, CancellationToken ct = default)
    {
        var certificate = await GetCertificateAsync(user.Id, ct);
        if (certificate is null)
            throw new InvalidOperationException("A verified synthetic student is required.");
        var link = $"{options.PublicBaseUrl.TrimEnd('/')}/auth/email-test/certificate";
        IReadOnlyList<EmailAttachment>? attachments = options.IncludeCertificateAttachment
            ? [new EmailAttachment(certificate.FileName, "application/pdf", DevelopmentCertificateService.GeneratePdf(certificate))]
            : null;
        var sent = await email.SendAsync(new EmailMessage(user.Email, "Your LMS sample certificate",
            $"<h1>Your sample certificate</h1><p>Taylor Test Student - Synthetic license SYNTHETIC-0001.</p><p>Not valid for credit. No actual course completion or license ownership is asserted.</p><p><a href=\"{link}\">Download certificate (sign-in required)</a></p><p><a href=\"https://www.myfloridalicense.com/wl11.asp?mode=0&amp;SID=\">Florida DBPR official license lookup</a> - reference only; this synthetic license has no official record.</p>",
            attachments));
        await audit.WriteAsync(user.Id, user.Email, sent ? "email.sample-certificate.sent" : "email.sample-certificate.delivery-failed",
            "UserAccount", user.Id);
        return sent;
    }

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
