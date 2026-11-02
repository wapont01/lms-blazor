using System.Net.Mail;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public sealed class FederatedSignInException(string message) : InvalidOperationException(message);

public interface IFederatedAccountService
{
    Task<UserAccount> ResolveAsync(string issuer, string subject, string? email, string? name, CancellationToken ct = default);
}

public sealed class FederatedAccountService(
    ApplicationDbContext db, IUserAccountService accounts, IAuditLogService audit) : IFederatedAccountService
{
    public async Task<UserAccount> ResolveAsync(
        string issuer, string subject, string? email, string? name, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme != "https" || issuer.Length > 200
            || string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
            throw new FederatedSignInException("The provider did not supply a valid identity.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var identity = await db.ExternalIdentities.Include(item => item.UserAccount)
            .SingleOrDefaultAsync(item => item.Issuer == issuer && item.Subject == subject, ct);
        UserAccount user;
        if (identity is not null)
        {
            user = identity.UserAccount!;
        }
        else
        {
            var normalizedEmail = email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail) || normalizedEmail.Length > 120
                || !MailAddress.TryCreate(normalizedEmail, out var address) || address.Address != normalizedEmail)
                throw new FederatedSignInException("The provider did not supply a usable email address.");
            // Email is contact data, never authority to take over an existing LMS account.
            if (await db.UserAccounts.AnyAsync(item => item.Email == normalizedEmail, ct))
                throw new FederatedSignInException("An LMS account already uses this email. Sign in with its existing method.");
            user = await accounts.UpsertExternalUserAsync(normalizedEmail,
                string.IsNullOrWhiteSpace(name) ? "Guest" : name.Trim()[..Math.Min(name.Trim().Length, 120)]);
            db.ExternalIdentities.Add(new ExternalIdentity { Issuer = issuer, Subject = subject, UserAccountId = user.Id });
            await db.SaveChangesAsync(ct);
        }
        if (!user.IsActive || user.LockoutEndUtc > DateTime.UtcNow || user.ForcePasswordChange || user.EmailTwoStepEnabled)
            throw new FederatedSignInException("This account cannot use federated sign-in. Use its existing sign-in method or contact an administrator.");
        await audit.WriteAsync(user.Id, user.Email, "auth.sso.succeeded", "UserAccount", user.Id, $"Federated sign-in; issuer={issuer}");
        await transaction.CommitAsync(ct);
        return user;
    }
}
