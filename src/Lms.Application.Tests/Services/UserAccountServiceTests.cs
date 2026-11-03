using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class UserAccountServiceTests
{
    [Theory]
    [InlineData("william.learner@wlr.com")]
    [InlineData("learner@lms.com")]
    public async Task EnsureSeedUsersAsync_RenamesDemoLoginInPlace(string previousEmail)
    {
        await using var fixture = await TestFixture.CreateAsync();
        var user = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var id = user.Id;
        var passwordHash = user.PasswordHash;
        var count = await fixture.DbContext.UserAccounts.CountAsync();
        user.Email = previousEmail;
        await fixture.DbContext.SaveChangesAsync();

        await fixture.UserAccountService.EnsureSeedUsersAsync();
        await fixture.UserAccountService.EnsureSeedUsersAsync();

        var renamed = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        Assert.Equal(id, renamed.Id);
        Assert.Equal(passwordHash, renamed.PasswordHash);
        Assert.Equal(count, await fixture.DbContext.UserAccounts.CountAsync());
        Assert.Equal("Learner", renamed.Role);
        var login = await fixture.UserAccountService.AuthenticateAsync(renamed.Email, "Learner123!");
        Assert.Equal(LoginStatus.Succeeded, login.Status);
    }

    [Theory]
    [InlineData("Guest Learner")]
    [InlineData("Guest Student")]
    public async Task EnsureSeedUsersAsync_NormalizesGuestPlaceholderWithoutChangingIdentity(string displayName)
    {
        await using var fixture = await TestFixture.CreateAsync();
        var guest = await fixture.UserAccountService.RegisterLearnerAsync("guest@example.com", "GuestTest123!");
        guest.DisplayName = displayName;
        var passwordHash = guest.PasswordHash;
        await fixture.DbContext.SaveChangesAsync();

        await fixture.UserAccountService.EnsureSeedUsersAsync();
        await fixture.UserAccountService.EnsureSeedUsersAsync();

        var updated = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Id == guest.Id);
        Assert.Equal("Guest", updated.DisplayName);
        Assert.Equal("guest@example.com", updated.Email);
        Assert.Equal(passwordHash, updated.PasswordHash);
        Assert.Equal("Learner", updated.Role);
    }

    [Fact]
    public async Task AuthenticateAsync_LocksUserAfterFiveFailedAttempts()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.UserAccountService;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var invalidResult = await service.AuthenticateAsync("william.broker@wlr.com", "WrongPass1!");
            Assert.Equal(LoginStatus.InvalidCredentials, invalidResult.Status);
        }

        var lockoutResult = await service.AuthenticateAsync("william.broker@wlr.com", "WrongPass1!");
        Assert.Equal(LoginStatus.LockedOut, lockoutResult.Status);
        Assert.NotNull(lockoutResult.LockoutRemaining);

        var validDuringLockout = await service.AuthenticateAsync("william.broker@wlr.com", "Broker123!");
        Assert.Equal(LoginStatus.LockedOut, validDuringLockout.Status);

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.broker@wlr.com");
        Assert.Equal(0, broker.FailedLoginCount);
        Assert.True(broker.LockoutEndUtc.HasValue);
        Assert.True(broker.LockoutEndUtc.Value > DateTime.UtcNow);
    }

    [Fact]
    public async Task AdminResetPassword_SetsForceChange_AndChangePasswordClearsIt()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.UserAccountService;

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");

        await service.AdminResetPasswordAsync(admin.Id, admin.Email, learner.Id, "abcdefgh");

        var loginWithResetPassword = await service.AuthenticateAsync(learner.Email, "abcdefgh");
        Assert.Equal(LoginStatus.Succeeded, loginWithResetPassword.Status);
        Assert.NotNull(loginWithResetPassword.User);
        Assert.True(loginWithResetPassword.User!.ForcePasswordChange);

        await service.ChangePasswordAsync(learner.Id, "abcdefgh", "two words");

        var refreshedLearner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        Assert.False(refreshedLearner.ForcePasswordChange);

        var loginWithNewPassword = await service.AuthenticateAsync(refreshedLearner.Email, "two words");
        Assert.Equal(LoginStatus.Succeeded, loginWithNewPassword.Status);
        Assert.NotNull(loginWithNewPassword.User);
        Assert.False(loginWithNewPassword.User!.ForcePasswordChange);
    }

    [Fact]
    public async Task EnsureSeedUsersAsync_MigratesLegacyAdminEmailToWlrDomain()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        admin.Email = string.Join("@", "admin", "lms.com");
        await fixture.DbContext.SaveChangesAsync();

        await fixture.UserAccountService.EnsureSeedUsersAsync();

        var migratedAdmin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var legacyEmailSuffix = string.Concat("@", "lms.com");
        var usersWithLegacyEmailDomain = await fixture.DbContext.UserAccounts
            .Where(user => user.Email.EndsWith(legacyEmailSuffix))
            .ToListAsync();

        Assert.Equal(admin.Id, migratedAdmin.Id);
        Assert.Empty(usersWithLegacyEmailDomain);
    }

    [Fact]
    public async Task EnsureSeedUsersAsync_BackfillsAdditionalSeededUsers_WhenDatabaseAlreadyHasUsers()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var primaryLearner = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "william.guest@wlr.com");
        var brokerTwo = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "clara.broker@wlr.com");
        var learnerTwo = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "maya.thompson@wlr.com");
        var learnerThree = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "ethan.brooks@wlr.com");
        var instructorOne = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "jordan.lee@wlr.com");

        primaryLearner.DisplayName = "Learner User";
        primaryLearner.LegalName = null;
        primaryLearner.LicenseNumber = null;
        primaryLearner.LicenseType = null;
        primaryLearner.Role = "Broker";
        instructorOne.DisplayName = "Instructor User";
        instructorOne.LegalName = null;
        instructorOne.LicenseNumber = null;
        instructorOne.PhoneNumber = null;
        instructorOne.PermanentAddress = null;
        fixture.DbContext.UserAccounts.RemoveRange(brokerTwo, learnerTwo, learnerThree);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.UserAccountService.EnsureSeedUsersAsync();

        var restoredBrokerTwo = await fixture.DbContext.UserAccounts
            .SingleOrDefaultAsync(user => user.Email == "clara.broker@wlr.com");
        var restoredLearnerTwo = await fixture.DbContext.UserAccounts
            .SingleOrDefaultAsync(user => user.Email == "maya.thompson@wlr.com");
        var restoredLearnerThree = await fixture.DbContext.UserAccounts
            .SingleOrDefaultAsync(user => user.Email == "ethan.brooks@wlr.com");
        var reconciledPrimaryLearner = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "william.guest@wlr.com");

        Assert.Equal("WILLIAM ALFONSO APONTE", reconciledPrimaryLearner.DisplayName);
        Assert.Equal("WILLIAM ALFONSO APONTE", reconciledPrimaryLearner.LegalName);
        Assert.Equal("352750", reconciledPrimaryLearner.LicenseNumber);
        Assert.Equal("B", reconciledPrimaryLearner.LicenseType);
        Assert.Equal("Learner", reconciledPrimaryLearner.Role);
        Assert.NotNull(restoredBrokerTwo);
        Assert.Equal("Broker", restoredBrokerTwo!.Role);
        Assert.True(restoredBrokerTwo.IsActive);
        Assert.NotNull(restoredLearnerTwo);
        Assert.Equal("Learner", restoredLearnerTwo!.Role);
        Assert.True(restoredLearnerTwo.IsActive);
        Assert.NotNull(restoredLearnerThree);
        Assert.Equal("Learner", restoredLearnerThree!.Role);
        Assert.True(restoredLearnerThree.IsActive);

        var learners = await fixture.DbContext.UserAccounts
            .Where(user => user.Role == "Learner")
            .OrderBy(user => user.Email)
            .ToListAsync();
        Assert.Equal(6, learners.Count);
        Assert.All(learners, learner =>
        {
            Assert.False(string.IsNullOrWhiteSpace(learner.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(learner.LegalName));
            Assert.EndsWith("@wlr.com", learner.Email);
            Assert.False(string.IsNullOrWhiteSpace(learner.LicenseNumber));
            Assert.False(string.IsNullOrWhiteSpace(learner.LicenseType));
            Assert.False(string.IsNullOrWhiteSpace(learner.PhoneNumber));
            Assert.False(string.IsNullOrWhiteSpace(learner.PermanentAddress));
        });
        Assert.Equal("Maya Thompson", learners.Single(user => user.Email == "maya.thompson@wlr.com").DisplayName);
        Assert.Equal("Ethan Brooks", learners.Single(user => user.Email == "ethan.brooks@wlr.com").DisplayName);
        Assert.Contains(learners, user => user.Email == "sofia.martinez.fl@wlr.com" && user.LicenseType == "SL");
        Assert.Contains(learners, user => user.Email == "noah.williams.fl@wlr.com" && user.LicenseType == "BL");
        Assert.Contains(learners, user => user.Email == "ava.johnson.fl@wlr.com" && user.LicenseType == "BK");

        var instructors = await fixture.DbContext.UserAccounts
            .Where(user => user.Role == "Instructor")
            .OrderBy(user => user.Email)
            .ToListAsync();
        Assert.Equal(4, instructors.Count);
        Assert.All(instructors, instructor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(instructor.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(instructor.LegalName));
            Assert.False(string.IsNullOrWhiteSpace(instructor.LicenseNumber));
            Assert.False(string.IsNullOrWhiteSpace(instructor.PhoneNumber));
            Assert.False(string.IsNullOrWhiteSpace(instructor.PermanentAddress));
        });
        Assert.Equal("Jordan M. Lee", instructors.Single(user => user.Email == "jordan.lee@wlr.com").DisplayName);
        Assert.Equal("2277", instructors.Single(user => user.Email == "susan.malloy@wlr.com").LicenseNumber);
        Assert.Contains(instructors, user => user.Email == "william.aponte@wlr.com" && user.DisplayName == "William A. Aponte");
    }

    [Fact]
    public async Task SeedEnrollmentsAsync_DoesNotCreateMockEnrollments()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await CourseSeed.SeedAsync(fixture.DbContext);
        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);
        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);

        Assert.Empty(await fixture.DbContext.Enrollments.ToListAsync());
        var titles = await fixture.DbContext.Courses.Select(course => course.Title).ToListAsync();
        Assert.Contains("Florida Core Law", titles);
        Assert.DoesNotContain(titles, title => title is "Real Estate Licensing Basics" or "Advanced Brokerage Compliance"
            or "Draft Course Outline" or "Property Marketing and Lead Conversion"
            or "Transaction Coordination Mastery" or "Client Communication and Negotiation Strategies");
    }

    [Fact]
    public async Task SeedEnrollmentsAsync_DoesNotSeedMockBrokerPurchases()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await CourseSeed.SeedAsync(fixture.DbContext);
        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);

        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);
        Assert.Empty(await fixture.DbContext.Enrollments.ToListAsync());
    }

    [Fact]
    public async Task UpsertExternalUserAsync_CreatesLearnerByDefault()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var created = await fixture.UserAccountService.UpsertExternalUserAsync("external.user@contoso.com", "External User");

        Assert.Equal("external.user@contoso.com", created.Email);
        Assert.Equal("External User", created.DisplayName);
        Assert.Equal("Learner", created.Role);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task UpsertExternalUserAsync_UpdatesDisplayName_ButPreservesRole()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var existing = await fixture.UserAccountService.CreateAsync("existing.sso@wlr.com", "Existing SSO", "Admin123!a", "Broker");
        var updated = await fixture.UserAccountService.UpsertExternalUserAsync(existing.Email, "Existing SSO Updated");

        Assert.Equal(existing.Id, updated.Id);
        Assert.Equal("Existing SSO Updated", updated.DisplayName);
        Assert.Equal("Broker", updated.Role);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateInstructorProfileAsync_RespectsExplicitFacultyOwnership(bool hasOwnedRecord)
    {
        await using var fixture = await TestFixture.CreateAsync();
        var instructor = await fixture.DbContext.UserAccounts.SingleAsync(u => u.Email == "jordan.lee@wlr.com");
        var other = await fixture.DbContext.UserAccounts.SingleAsync(u => u.Email == "susan.malloy@wlr.com");
        var school = new SchoolProfile { State = "Test" };
        var otherRecord = new SchoolStaffMember
        {
            SchoolProfileId = school.Id, UserAccountId = other.Id, Role = SchoolStaffRoles.Instructor,
            Name = instructor.DisplayName, Email = instructor.Email, LicenseNumber = "KEEP-OTHER"
        };
        var ownedRecord = new SchoolStaffMember
        {
            SchoolProfileId = school.Id, UserAccountId = instructor.Id, Role = SchoolStaffRoles.Instructor,
            Name = "Previous name", Email = "previous@example.invalid", LicenseNumber = "OLD"
        };
        fixture.DbContext.SchoolProfiles.Add(school);
        fixture.DbContext.SchoolStaffMembers.Add(otherRecord);
        if (hasOwnedRecord) fixture.DbContext.SchoolStaffMembers.Add(ownedRecord);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.UserAccountService.UpdateInstructorProfileAsync(instructor.Id, "updated@example.invalid",
            "NEW-LICENSE", "555-0100", null);

        fixture.DbContext.ChangeTracker.Clear();
        var records = await fixture.DbContext.SchoolStaffMembers.ToListAsync();
        var unchanged = records.Single(m => m.Id == otherRecord.Id);
        Assert.Equal("KEEP-OTHER", unchanged.LicenseNumber);
        Assert.Equal(other.Id, unchanged.UserAccountId);
        Assert.Equal("jordan.lee@wlr.com", unchanged.Email);
        var updated = records.Single(m => m.Id != otherRecord.Id);
        Assert.Equal("NEW-LICENSE", updated.LicenseNumber);
        Assert.Equal("updated@example.invalid", updated.Email);
        Assert.Equal("555-0100", updated.Telephone);
        if (hasOwnedRecord) Assert.Equal(instructor.Id, updated.UserAccountId);
        else Assert.Null(updated.UserAccountId);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IUserAccountService UserAccountService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IUserAccountService userAccountService)
        {
            _connection = connection;
            DbContext = dbContext;
            UserAccountService = userAccountService;
        }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var auditLogService = new AuditLogService(dbContext);
            var userAccountService = new UserAccountService(dbContext, auditLogService);
            await userAccountService.EnsureSeedUsersAsync();

            return new TestFixture(connection, dbContext, userAccountService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
