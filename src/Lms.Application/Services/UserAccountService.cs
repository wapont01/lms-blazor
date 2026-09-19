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
    Task<UserAccount> RegisterLearnerAsync(string email, string displayName, string password, string? state = null, string? licenseNumber = null, bool marketingOptIn = true);
    Task SetMarketingEmailPreferenceAsync(Guid userId, bool optIn);
    Task UpdateRoleAsync(Guid userId, string role, Guid? actorUserId = null, string? actorEmail = null);
    Task UpdateLicenseComplianceAsync(Guid userId, string? licenseStatus, DateTime? initialLicensureDate, DateTime? postlicensingCompletedAt, DateTime? brokerageExperienceStartDate, DateTime? bicCourseCompletedAt, DateTime? bicEligibleAt, DateTime? bicDesignationAt, DateTime? continuingEducationDueDate, DateTime? licenseRenewalDueDate, DateTime? bicUpdateCourseDueDate, bool isBicEligible, Guid? actorUserId = null, string? actorEmail = null);
    Task ToggleActiveAsync(Guid userId, Guid? actorUserId = null, string? actorEmail = null);
    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<UserAccount> UpdateInstructorProfileAsync(Guid userId, string email, string? licenseNumber, string? phoneNumber, string? permanentAddress);
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

    public async Task<UserAccount> UpdateInstructorProfileAsync(
        Guid userId,
        string email,
        string? licenseNumber,
        string? phoneNumber,
        string? permanentAddress)
    {
        var user = await _dbContext.UserAccounts.SingleOrDefaultAsync(existing => existing.Id == userId)
            ?? throw new InvalidOperationException("User account was not found.");
        if (!string.Equals(user.Role, "Instructor", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only instructor accounts can update an instructor profile.");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new InvalidOperationException("Email is required.");
        }
        if (await _dbContext.UserAccounts.AnyAsync(existing => existing.Id != userId && existing.Email == normalizedEmail))
        {
            throw new InvalidOperationException("A user with this email already exists.");
        }

        var previousEmail = user.Email;
        user.Email = normalizedEmail;
        user.LicenseNumber = NormalizeOptional(licenseNumber);
        user.PhoneNumber = NormalizeOptional(phoneNumber);
        user.PermanentAddress = NormalizeOptional(permanentAddress);

        var schoolProfile = await _dbContext.SchoolProfiles
            .Include(profile => profile.StaffMembers)
            .OrderBy(profile => profile.UpdatedAtUtc)
            .FirstOrDefaultAsync();
        if (schoolProfile is not null)
        {
            var staffMember = schoolProfile.StaffMembers.FirstOrDefault(member =>
                    string.Equals(member.Email, previousEmail, StringComparison.OrdinalIgnoreCase))
                ?? schoolProfile.StaffMembers.FirstOrDefault(member =>
                    string.Equals(member.Name, user.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (staffMember is null)
            {
                staffMember = new SchoolStaffMember
                {
                    SchoolProfileId = schoolProfile.Id,
                    Name = user.DisplayName,
                    Role = SchoolStaffRoles.Instructor,
                    Title = "Instructor"
                };
                _dbContext.SchoolStaffMembers.Add(staffMember);
            }

            staffMember.Email = user.Email;
            staffMember.LicenseNumber = user.LicenseNumber;
            staffMember.Telephone = user.PhoneNumber;

            if (string.Equals(schoolProfile.PrimaryInstructorName, user.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                schoolProfile.InstructorLicenseNumber = user.LicenseNumber;
                schoolProfile.PrimaryInstructorEmail = user.Email;
                if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
                {
                    schoolProfile.PrimaryInstructorTelephone = user.PhoneNumber;
                }
            }
        }

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(user.Id, user.Email, "instructor.profile.updated", "UserAccount", user.Id, $"LicenseNumber={user.LicenseNumber ?? ""};Phone={user.PhoneNumber ?? ""}");
        return user;
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

    public async Task<UserAccount> RegisterLearnerAsync(string email, string displayName, string password, string? state = null, string? licenseNumber = null, bool marketingOptIn = true)
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
            State = NormalizeOptional(state),
            LicenseNumber = NormalizeOptional(licenseNumber),
            MarketingEmailOptOutAtUtc = marketingOptIn ? null : now,
            PasswordHash = HashPassword(password),
            Role = "Learner",
            IsActive = true,
            CreatedAt = now,
            PasswordUpdatedAt = now,
            PasswordExpiresAt = now.AddDays(PasswordExpiryDays)
        };

        _dbContext.UserAccounts.Add(user);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(user.Id, user.Email, "user.self-registered", "UserAccount", user.Id, $"State={user.State ?? ""};License={user.LicenseNumber ?? ""};Marketing={(marketingOptIn ? "opt-in" : "opt-out")}");

        return user;
    }

    public async Task SetMarketingEmailPreferenceAsync(Guid userId, bool optIn)
    {
        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(existing => existing.Id == userId)
            ?? throw new InvalidOperationException("User account was not found.");

        user.MarketingEmailOptOutAtUtc = optIn ? null : DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(user.Id, user.Email, "user.marketing-preference.updated", "UserAccount", user.Id, optIn ? "opt-in" : "opt-out");
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

    public async Task UpdateLicenseComplianceAsync(Guid userId, string? licenseStatus, DateTime? initialLicensureDate, DateTime? postlicensingCompletedAt, DateTime? brokerageExperienceStartDate, DateTime? bicCourseCompletedAt, DateTime? bicEligibleAt, DateTime? bicDesignationAt, DateTime? continuingEducationDueDate, DateTime? licenseRenewalDueDate, DateTime? bicUpdateCourseDueDate, bool isBicEligible, Guid? actorUserId = null, string? actorEmail = null)
    {
        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(existing => existing.Id == userId);
        if (user is null)
        {
            return;
        }

        user.LicenseStatus = string.IsNullOrWhiteSpace(licenseStatus) ? null : licenseStatus.Trim();
        user.InitialLicensureDate = initialLicensureDate;
        user.PostlicensingCompletedAt = postlicensingCompletedAt;
        user.BrokerageExperienceStartDate = brokerageExperienceStartDate;
        user.BicCourseCompletedAt = bicCourseCompletedAt;
        user.BicEligibleAt = bicEligibleAt;
        user.BicDesignationAt = bicDesignationAt;
        user.ContinuingEducationDueDate = continuingEducationDueDate;
        user.LicenseRenewalDueDate = licenseRenewalDueDate;
        user.BicUpdateCourseDueDate = bicUpdateCourseDueDate;
        user.IsBicEligible = isBicEligible;

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "user.license-compliance.updated", "UserAccount", user.Id, $"LicenseStatus={user.LicenseStatus ?? "Unset"};BicEligible={user.IsBicEligible}");
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
            new SeedUserDefinition("jordan.lee@wlr.com", "Jordan M. Lee", "Instructor123!", "Instructor", "Jordan Morgan Lee", "NC-41001", State: "North Carolina", PhoneNumber: "704-555-0101", PermanentAddress: "4101 Park Road, Charlotte, NC 28209", LegacyEmail: "instructor@lms.com"),
            new SeedUserDefinition("clara.aponte@wlr.com", "Clara M. Aponte", "Instructor234!", "Instructor", "Clara Maria Aponte", "NC-41002", State: "North Carolina", PhoneNumber: "704-555-0102", PermanentAddress: "2202 Providence Road, Charlotte, NC 28211", LegacyEmail: "instructor2@lms.com"),
            new SeedUserDefinition("susan.malloy@wlr.com", "Susan (Jill) Malloy", "Instructor345!", "Instructor", "Susan Jill Malloy", "2277", State: "North Carolina", PhoneNumber: "704-555-0103", PermanentAddress: "3303 Selwyn Avenue, Charlotte, NC 28209", LegacyEmail: "instructor3@lms.com"),
            new SeedUserDefinition("william.aponte@wlr.com", "William A. Aponte", "Instructor456!", "Instructor", "William Alfonso Aponte", "NC-41004", State: "North Carolina", PhoneNumber: "786-555-0104", PermanentAddress: "17214 Belmont Stakes Lane, Charlotte, NC 28278", LegacyEmail: "instructor4@lms.com"),
            new SeedUserDefinition("william.broker@wlr.com", "William A. Aponte", "Broker123!", "Broker", "William Alfonso Aponte", State: "North Carolina", LicenseType: "B", LegacyEmail: "broker@lms.com"),
            new SeedUserDefinition("clara.broker@wlr.com", "Clara M. Aponte", "Broker234!", "Broker", "Clara Maria Aponte", State: "North Carolina", LicenseType: "PB", LegacyEmail: "broker2@lms.com"),
            new SeedUserDefinition(
                "william.learner@wlr.com",
                "WILLIAM ALFONSO APONTE",
                "Learner123!",
                "Learner",
                "WILLIAM ALFONSO APONTE",
                "352750",
                "B",
                State: "North Carolina",
                PhoneNumber: "704-555-0121",
                "17214 Belmont Stakes Lane, Charlotte, NC 28278",
                "learner@lms.com",
                true),
            new SeedUserDefinition("maya.thompson@wlr.com", "Maya Thompson", "Learner234!", "Learner", "Maya Elise Thompson", "481205", "PB", State: "North Carolina", PhoneNumber: "704-555-0122", PermanentAddress: "811 East Boulevard, Charlotte, NC 28203", LegacyEmail: "learner2@lms.com"),
            new SeedUserDefinition("ethan.brooks@wlr.com", "Ethan Brooks", "Learner345!", "Learner", "Ethan James Brooks", "517842", "PB", State: "North Carolina", PhoneNumber: "704-555-0123", PermanentAddress: "1420 Central Avenue, Charlotte, NC 28205", LegacyEmail: "learner3@lms.com"),
            new SeedUserDefinition("sofia.martinez.fl@wlr.com", "Sofia Martinez", "FloridaLearner123!", "Learner", "Sofia Elena Martinez", "FL-DEMO-SL-001", "SL", State: "Florida", PhoneNumber: "305-555-0141", PermanentAddress: "1450 Brickell Avenue, Miami, FL 33131"),
            new SeedUserDefinition("noah.williams.fl@wlr.com", "Noah Williams", "FloridaLearner234!", "Learner", "Noah James Williams", "FL-DEMO-BL-002", "BL", State: "Florida", PhoneNumber: "407-555-0142", PermanentAddress: "820 Lake Eola Drive, Orlando, FL 32801"),
            new SeedUserDefinition("ava.johnson.fl@wlr.com", "Ava Johnson", "FloridaLearner345!", "Learner", "Ava Renee Johnson", "FL-DEMO-BK-003", "BK", State: "Florida", PhoneNumber: "813-555-0143", PermanentAddress: "100 North Tampa Street, Tampa, FL 33602"),
            new SeedUserDefinition("marcus.reed.fl@wlr.com", "Marcus Reed", "FloridaBroker123!", "Broker", "Marcus Daniel Reed", "FL-DEMO-BK-004", "BK", State: "Florida", PhoneNumber: "305-555-0144", PermanentAddress: "900 Biscayne Boulevard, Miami, FL 33132"),
            new SeedUserDefinition("elena.rossi.fl@wlr.com", "Elena Rossi", "FloridaBroker234!", "Broker", "Elena Marie Rossi", "FL-DEMO-BL-005", "BL", State: "Florida", PhoneNumber: "954-555-0145", PermanentAddress: "110 East Broward Boulevard, Fort Lauderdale, FL 33301")
        };

        var existingUsers = await _dbContext.UserAccounts
            .ToListAsync();

        foreach (var seededUser in seededUsers)
        {
            var existingUser = existingUsers.SingleOrDefault(user =>
                string.Equals(user.Email, seededUser.Email, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(seededUser.LegacyEmail)
                    && string.Equals(user.Email, seededUser.LegacyEmail, StringComparison.OrdinalIgnoreCase)));
            if (existingUser is not null)
            {
                if (seededUser.Role == "Instructor")
                {
                    if (string.Equals(existingUser.Email, seededUser.LegacyEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        existingUser.Email = seededUser.Email;
                        hasChanges = true;
                    }

                    if (existingUser.DisplayName.StartsWith("Instructor ", StringComparison.Ordinal))
                    {
                        existingUser.DisplayName = seededUser.DisplayName;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.LegalName))
                    {
                        existingUser.LegalName = seededUser.LegalName;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.State))
                    {
                        existingUser.State = seededUser.State;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.LicenseNumber))
                    {
                        existingUser.LicenseNumber = seededUser.LicenseNumber;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.PhoneNumber))
                    {
                        existingUser.PhoneNumber = seededUser.PhoneNumber;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.PermanentAddress))
                    {
                        existingUser.PermanentAddress = seededUser.PermanentAddress;
                        hasChanges = true;
                    }

                    if (seededUser.IsBicEligible && !existingUser.IsBicEligible)
                    {
                        existingUser.IsBicEligible = true;
                        hasChanges = true;
                    }
                }

                if (seededUser.Role == "Broker")
                {
                    if (string.Equals(existingUser.Email, seededUser.LegacyEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        existingUser.Email = seededUser.Email;
                        hasChanges = true;
                    }

                    if (existingUser.DisplayName.StartsWith("Broker ", StringComparison.Ordinal))
                    {
                        existingUser.DisplayName = seededUser.DisplayName;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.LegalName))
                    {
                        existingUser.LegalName = seededUser.LegalName;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.LicenseType))
                    {
                        existingUser.LicenseType = seededUser.LicenseType;
                        hasChanges = true;
                    }
                }

                if (seededUser.Role == "Learner")
                {
                    if (string.Equals(existingUser.Email, seededUser.LegacyEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        existingUser.Email = seededUser.Email;
                        hasChanges = true;
                    }

                    if (existingUser.DisplayName.StartsWith("Learner ", StringComparison.Ordinal))
                    {
                        existingUser.DisplayName = seededUser.DisplayName;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.LegalName))
                    {
                        existingUser.LegalName = seededUser.LegalName;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.LicenseNumber))
                    {
                        existingUser.LicenseNumber = seededUser.LicenseNumber;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.LicenseType))
                    {
                        existingUser.LicenseType = seededUser.LicenseType;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.PhoneNumber))
                    {
                        existingUser.PhoneNumber = seededUser.PhoneNumber;
                        hasChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingUser.PermanentAddress))
                    {
                        existingUser.PermanentAddress = seededUser.PermanentAddress;
                        hasChanges = true;
                    }

                    if (existingUser.Role != "Learner")
                    {
                        existingUser.Role = "Learner";
                        hasChanges = true;
                    }

                    if (seededUser.IsBicEligible && !existingUser.IsBicEligible)
                    {
                        existingUser.IsBicEligible = true;
                        hasChanges = true;
                    }
                }

                if (string.IsNullOrWhiteSpace(existingUser.State) && !string.IsNullOrWhiteSpace(seededUser.State))
                {
                    existingUser.State = seededUser.State;
                    hasChanges = true;
                }

                continue;
            }

            _dbContext.UserAccounts.Add(new UserAccount
            {
                Email = seededUser.Email,
                DisplayName = seededUser.DisplayName,
                LegalName = seededUser.LegalName,
                LicenseNumber = seededUser.LicenseNumber,
                LicenseType = seededUser.LicenseType,
                State = seededUser.State,
                PhoneNumber = seededUser.PhoneNumber,
                PermanentAddress = seededUser.PermanentAddress,
                IsBicEligible = seededUser.IsBicEligible,
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

    private sealed record SeedUserDefinition(
        string Email,
        string DisplayName,
        string Password,
        string Role,
        string? LegalName = null,
        string? LicenseNumber = null,
        string? LicenseType = null,
        string? State = null,
        string? PhoneNumber = null,
        string? PermanentAddress = null,
        string? LegacyEmail = null,
        bool IsBicEligible = false);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
