using System.Security.Cryptography;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IUserAccountService
{
    Task<LoginResult> AuthenticateAsync(string email, string password);
    Task<List<UserAccount>> GetAllAsync();
    Task<UserAccount?> GetByIdAsync(Guid id);
    Task<UserAccount> CreateAsync(string email, string displayName, string password, string role, Guid? actorUserId = null, string? actorEmail = null);
    Task UpdateRoleAsync(Guid userId, string role, Guid? actorUserId = null, string? actorEmail = null);
    Task ToggleActiveAsync(Guid userId, Guid? actorUserId = null, string? actorEmail = null);
    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task AdminResetPasswordAsync(Guid actorUserId, string actorEmail, Guid targetUserId, string newPassword);
    Task<UserAccount> UpsertExternalUserAsync(string email, string displayName);
    Task EnsureSeedUsersAsync();
}

public enum LoginStatus
{
    Succeeded,
    InvalidCredentials,
    Inactive,
    LockedOut,
    PasswordExpired
}

public sealed record LoginResult(LoginStatus Status, UserAccount? User = null, TimeSpan? LockoutRemaining = null);

public class UserAccountService : IUserAccountService
{
    private const int Iterations = 120000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int PasswordExpiryDays = 90;
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationService? _notificationService;

    public UserAccountService(ApplicationDbContext dbContext, IAuditLogService auditLogService, INotificationService? notificationService = null)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _notificationService = notificationService;
    }

    public async Task<LoginResult> AuthenticateAsync(string email, string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(existing => existing.Email == normalizedEmail);

        if (user is null)
        {
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            return new LoginResult(LoginStatus.Inactive);
        }

        var now = DateTime.UtcNow;
        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now)
        {
            return new LoginResult(LoginStatus.LockedOut, LockoutRemaining: user.LockoutEndUtc.Value - now);
        }

        if (IsPasswordExpired(user))
        {
            return new LoginResult(LoginStatus.PasswordExpired);
        }

        var credentialsValid = VerifyPassword(password, user.PasswordHash);
        if (!credentialsValid)
        {
            user.FailedLoginCount += 1;

            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEndUtc = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
                await _dbContext.SaveChangesAsync();
                await _auditLogService.WriteAsync(user.Id, user.Email, "auth.lockout.started", "UserAccount", user.Id, $"Until={user.LockoutEndUtc:O}");
                if (_notificationService is not null)
                {
                    await _notificationService.CreateAsync(user.Id, "security", "Account locked temporarily", "Too many failed sign-in attempts triggered a temporary lockout.");
                }
                return new LoginResult(LoginStatus.LockedOut, LockoutRemaining: LockoutDuration);
            }

            await _dbContext.SaveChangesAsync();
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        var changed = false;
        if (user.FailedLoginCount != 0)
        {
            user.FailedLoginCount = 0;
            changed = true;
        }

        if (user.LockoutEndUtc.HasValue)
        {
            user.LockoutEndUtc = null;
            changed = true;
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync();
        }

        return new LoginResult(LoginStatus.Succeeded, user);
    }

    public async Task<List<UserAccount>> GetAllAsync()
    {
        return await _dbContext.UserAccounts
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .ToListAsync();
    }

    public async Task<UserAccount?> GetByIdAsync(Guid id)
    {
        return await _dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(user => user.Id == id);
    }

    public async Task<UserAccount> CreateAsync(string email, string displayName, string password, string role, Guid? actorUserId = null, string? actorEmail = null)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (await _dbContext.UserAccounts.AnyAsync(existing => existing.Email == normalizedEmail))
        {
            throw new InvalidOperationException("A user with this email already exists.");
        }

        EnsurePasswordComplexity(password);

        var now = DateTime.UtcNow;

        var user = new UserAccount
        {
            Email = normalizedEmail,
            DisplayName = displayName.Trim(),
            PasswordHash = HashPassword(password),
            Role = role,
            IsActive = true,
            CreatedAt = now,
            PasswordUpdatedAt = now,
            PasswordExpiresAt = now.AddDays(PasswordExpiryDays)
        };

        _dbContext.UserAccounts.Add(user);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "user.created", "UserAccount", user.Id, $"Role={user.Role}");

        return user;
    }

    public async Task UpdateRoleAsync(Guid userId, string role, Guid? actorUserId = null, string? actorEmail = null)
    {
        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(existing => existing.Id == userId);
        if (user is null)
        {
            return;
        }

        var previousRole = user.Role;
        user.Role = role;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "user.role.updated", "UserAccount", user.Id, $"{previousRole} -> {role}");
    }

    public async Task ToggleActiveAsync(Guid userId, Guid? actorUserId = null, string? actorEmail = null)
    {
        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(existing => existing.Id == userId);
        if (user is null)
        {
            return;
        }

        user.IsActive = !user.IsActive;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "user.status.toggled", "UserAccount", user.Id, user.IsActive ? "Active" : "Inactive");
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        EnsurePasswordComplexity(newPassword);

        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(existing => existing.Id == userId);
        if (user is null)
        {
            throw new InvalidOperationException("User account was not found.");
        }

        if (!VerifyPassword(currentPassword, user.PasswordHash))
        {
            throw new InvalidOperationException("Current password is incorrect.");
        }

        var now = DateTime.UtcNow;
        user.PasswordHash = HashPassword(newPassword);
        user.PasswordUpdatedAt = now;
        user.PasswordExpiresAt = now.AddDays(PasswordExpiryDays);
        user.ForcePasswordChange = false;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(user.Id, user.Email, "user.password.changed", "UserAccount", user.Id);
    }

    public async Task AdminResetPasswordAsync(Guid actorUserId, string actorEmail, Guid targetUserId, string newPassword)
    {
        EnsurePasswordComplexity(newPassword);

        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(existing => existing.Id == targetUserId);
        if (user is null)
        {
            throw new InvalidOperationException("User account was not found.");
        }

        var now = DateTime.UtcNow;
        user.PasswordHash = HashPassword(newPassword);
        user.PasswordUpdatedAt = now;
        user.PasswordExpiresAt = now.AddDays(PasswordExpiryDays);
        user.ForcePasswordChange = true;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "user.password.reset", "UserAccount", user.Id, "Reset by admin");
        if (_notificationService is not null)
        {
            await _notificationService.CreateAsync(user.Id, "security", "Password reset required", "An administrator reset your password. You must change it at next sign-in.");
        }
    }

    public async Task<UserAccount> UpsertExternalUserAsync(string email, string displayName)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new InvalidOperationException("External identity email is required.");
        }

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? normalizedEmail
            : displayName.Trim();

        var existingUser = await _dbContext.UserAccounts.FirstOrDefaultAsync(user => user.Email == normalizedEmail);
        if (existingUser is not null)
        {
            if (!string.Equals(existingUser.DisplayName, resolvedDisplayName, StringComparison.Ordinal))
            {
                existingUser.DisplayName = resolvedDisplayName;
                await _dbContext.SaveChangesAsync();
            }

            return existingUser;
        }

        var now = DateTime.UtcNow;
        var user = new UserAccount
        {
            Email = normalizedEmail,
            DisplayName = resolvedDisplayName,
            PasswordHash = HashPassword($"{Guid.NewGuid():N}!Aa1"),
            Role = "Learner",
            IsActive = true,
            CreatedAt = now,
            PasswordUpdatedAt = now,
            PasswordExpiresAt = now.AddDays(PasswordExpiryDays),
            ForcePasswordChange = false,
            FailedLoginCount = 0,
            LockoutEndUtc = null
        };

        _dbContext.UserAccounts.Add(user);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(user.Id, user.Email, "user.created.external-sso", "UserAccount", user.Id, "Role=Learner");
        return user;
    }

    public async Task EnsureSeedUsersAsync()
    {
        var usersWithMissingMetadata = await _dbContext.UserAccounts
            .Where(user => user.PasswordUpdatedAt == null || user.PasswordExpiresAt == null)
            .ToListAsync();

        var hasChanges = false;
        if (usersWithMissingMetadata.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var user in usersWithMissingMetadata)
            {
                user.PasswordUpdatedAt ??= now;
                user.PasswordExpiresAt ??= now.AddDays(PasswordExpiryDays);
                user.ForcePasswordChange = false;
                user.FailedLoginCount = 0;
            }

            hasChanges = true;
        }

        var seededNow = DateTime.UtcNow;
        var seededUsers = new[]
        {
            new SeedUserDefinition("admin@lms.com", "Admin User", "Admin123!", "Admin"),
            new SeedUserDefinition("instructor@lms.com", "Instructor User", "Instructor123!", "Instructor"),
            new SeedUserDefinition("broker@lms.com", "Broker User", "Broker123!", "Broker"),
            new SeedUserDefinition("broker2@lms.com", "Broker Two", "Broker234!", "Broker"),
            new SeedUserDefinition("learner@lms.com", "Learner User", "Learner123!", "Learner"),
            new SeedUserDefinition("learner2@lms.com", "Learner Two", "Learner234!", "Learner"),
            new SeedUserDefinition("learner3@lms.com", "Learner Three", "Learner345!", "Learner")
        };

        var existingEmails = await _dbContext.UserAccounts
            .Select(user => user.Email)
            .ToListAsync();

        foreach (var seededUser in seededUsers)
        {
            if (existingEmails.Contains(seededUser.Email, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _dbContext.UserAccounts.Add(new UserAccount
            {
                Email = seededUser.Email,
                DisplayName = seededUser.DisplayName,
                PasswordHash = HashPassword(seededUser.Password),
                Role = seededUser.Role,
                IsActive = true,
                CreatedAt = seededNow,
                PasswordUpdatedAt = seededNow,
                PasswordExpiresAt = seededNow.AddDays(PasswordExpiryDays),
                ForcePasswordChange = false,
                FailedLoginCount = 0
            });

            hasChanges = true;
        }

        if (hasChanges)
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private sealed record SeedUserDefinition(string Email, string DisplayName, string Password, string Role);

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var hashParts = storedHash.Split(':');
        if (hashParts.Length != 2)
        {
            return false;
        }

        var salt = Convert.FromBase64String(hashParts[0]);
        var expectedHash = Convert.FromBase64String(hashParts[1]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static bool IsPasswordExpired(UserAccount user)
    {
        return user.PasswordExpiresAt.HasValue && user.PasswordExpiresAt.Value <= DateTime.UtcNow;
    }

    private static void EnsurePasswordComplexity(string password)
    {
        var candidate = password.Trim();

        var hasUpper = candidate.Any(char.IsUpper);
        var hasLower = candidate.Any(char.IsLower);
        var hasDigit = candidate.Any(char.IsDigit);
        var hasSpecial = candidate.Any(character => !char.IsLetterOrDigit(character));

        if (candidate.Length < 10 || !hasUpper || !hasLower || !hasDigit || !hasSpecial)
        {
            throw new InvalidOperationException("Password must be at least 10 characters and include uppercase, lowercase, number, and special character.");
        }
    }
}
