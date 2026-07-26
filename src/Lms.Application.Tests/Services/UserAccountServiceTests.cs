using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class UserAccountServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_LocksUserAfterFiveFailedAttempts()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.UserAccountService;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var invalidResult = await service.AuthenticateAsync("broker@lms.com", "WrongPass1!");
            Assert.Equal(LoginStatus.InvalidCredentials, invalidResult.Status);
        }

        var lockoutResult = await service.AuthenticateAsync("broker@lms.com", "WrongPass1!");
        Assert.Equal(LoginStatus.LockedOut, lockoutResult.Status);
        Assert.NotNull(lockoutResult.LockoutRemaining);

        var validDuringLockout = await service.AuthenticateAsync("broker@lms.com", "Broker123!");
        Assert.Equal(LoginStatus.LockedOut, validDuringLockout.Status);

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        Assert.Equal(0, broker.FailedLoginCount);
        Assert.True(broker.LockoutEndUtc.HasValue);
        Assert.True(broker.LockoutEndUtc.Value > DateTime.UtcNow);
    }

    [Fact]
    public async Task AdminResetPassword_SetsForceChange_AndChangePasswordClearsIt()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.UserAccountService;

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        await service.AdminResetPasswordAsync(admin.Id, admin.Email, learner.Id, "TempLearner1!");

        var loginWithResetPassword = await service.AuthenticateAsync(learner.Email, "TempLearner1!");
        Assert.Equal(LoginStatus.Succeeded, loginWithResetPassword.Status);
        Assert.NotNull(loginWithResetPassword.User);
        Assert.True(loginWithResetPassword.User!.ForcePasswordChange);

        await service.ChangePasswordAsync(learner.Id, "TempLearner1!", "Learner999!");

        var refreshedLearner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        Assert.False(refreshedLearner.ForcePasswordChange);

        var loginWithNewPassword = await service.AuthenticateAsync(refreshedLearner.Email, "Learner999!");
        Assert.Equal(LoginStatus.Succeeded, loginWithNewPassword.Status);
        Assert.NotNull(loginWithNewPassword.User);
        Assert.False(loginWithNewPassword.User!.ForcePasswordChange);
    }

    [Fact]
    public async Task EnsureSeedUsersAsync_BackfillsAdditionalSeededUsers_WhenDatabaseAlreadyHasUsers()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var brokerTwo = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "broker2@lms.com");
        var learnerTwo = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "learner2@lms.com");
        var learnerThree = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "learner3@lms.com");

        fixture.DbContext.UserAccounts.RemoveRange(brokerTwo, learnerTwo, learnerThree);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.UserAccountService.EnsureSeedUsersAsync();

        var restoredBrokerTwo = await fixture.DbContext.UserAccounts
            .SingleOrDefaultAsync(user => user.Email == "broker2@lms.com");
        var restoredLearnerTwo = await fixture.DbContext.UserAccounts
            .SingleOrDefaultAsync(user => user.Email == "learner2@lms.com");
        var restoredLearnerThree = await fixture.DbContext.UserAccounts
            .SingleOrDefaultAsync(user => user.Email == "learner3@lms.com");

        Assert.NotNull(restoredBrokerTwo);
        Assert.Equal("Broker", restoredBrokerTwo!.Role);
        Assert.True(restoredBrokerTwo.IsActive);
        Assert.NotNull(restoredLearnerTwo);
        Assert.Equal("Learner", restoredLearnerTwo!.Role);
        Assert.True(restoredLearnerTwo.IsActive);
        Assert.NotNull(restoredLearnerThree);
        Assert.Equal("Learner", restoredLearnerThree!.Role);
        Assert.True(restoredLearnerThree.IsActive);
    }

    [Fact]
    public async Task SeedEnrollmentsAsync_BackfillsMissingLearnerEnrollments_WithoutDuplicatingExistingRows()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await CourseSeed.SeedAsync(fixture.DbContext);
        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);

        var learnerTwo = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "learner2@lms.com");
        var learnerThree = await fixture.DbContext.UserAccounts
            .SingleAsync(user => user.Email == "learner3@lms.com");

        var learnerTwoEnrollments = await fixture.DbContext.Enrollments
            .Where(enrollment => enrollment.UserAccountId == learnerTwo.Id)
            .ToListAsync();
        var learnerThreeEnrollments = await fixture.DbContext.Enrollments
            .Where(enrollment => enrollment.UserAccountId == learnerThree.Id)
            .ToListAsync();
        fixture.DbContext.Enrollments.RemoveRange(learnerTwoEnrollments);
        fixture.DbContext.Enrollments.RemoveRange(learnerThreeEnrollments);
        await fixture.DbContext.SaveChangesAsync();

        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);

        var enrollmentCountsByEmail = await fixture.DbContext.Enrollments
            .Join(
                fixture.DbContext.UserAccounts,
                enrollment => enrollment.UserAccountId,
                user => user.Id,
                (enrollment, user) => new { user.Email, enrollment.CourseId })
            .Where(row => row.Email == "learner@lms.com" || row.Email == "learner2@lms.com" || row.Email == "learner3@lms.com")
            .GroupBy(row => row.Email)
            .Select(group => new { Email = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Email, group => group.Count);

        Assert.Equal(3, enrollmentCountsByEmail["learner@lms.com"]);
        Assert.Equal(2, enrollmentCountsByEmail["learner2@lms.com"]);
        Assert.Equal(2, enrollmentCountsByEmail["learner3@lms.com"]);

        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);

        var countsAfterSecondRun = await fixture.DbContext.Enrollments
            .Join(
                fixture.DbContext.UserAccounts,
                enrollment => enrollment.UserAccountId,
                user => user.Id,
                (enrollment, user) => new { user.Email, enrollment.CourseId })
            .Where(row => row.Email == "learner@lms.com" || row.Email == "learner2@lms.com" || row.Email == "learner3@lms.com")
            .GroupBy(row => row.Email)
            .Select(group => new { Email = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Email, group => group.Count);

        Assert.Equal(3, countsAfterSecondRun["learner@lms.com"]);
        Assert.Equal(2, countsAfterSecondRun["learner2@lms.com"]);
        Assert.Equal(2, countsAfterSecondRun["learner3@lms.com"]);
    }

    [Fact]
    public async Task SeedEnrollmentsAsync_AlsoSeedsBrokerPurchases_Idempotently()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await CourseSeed.SeedAsync(fixture.DbContext);
        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);

        var firstCount = await fixture.DbContext.Enrollments
            .Where(enrollment => enrollment.EnrollmentSource == "LearnerPurchase")
            .Join(
                fixture.DbContext.UserAccounts,
                enrollment => enrollment.UserAccountId,
                user => user.Id,
                (enrollment, user) => new { user.Role })
            .CountAsync(row => row.Role == "Broker");
        Assert.True(firstCount > 0);

        await CourseSeed.SeedEnrollmentsAsync(fixture.DbContext);
        var secondCount = await fixture.DbContext.Enrollments
            .Where(enrollment => enrollment.EnrollmentSource == "LearnerPurchase")
            .Join(
                fixture.DbContext.UserAccounts,
                enrollment => enrollment.UserAccountId,
                user => user.Id,
                (enrollment, user) => new { user.Role })
            .CountAsync(row => row.Role == "Broker");

        Assert.Equal(firstCount, secondCount);
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

        var existing = await fixture.UserAccountService.CreateAsync("existing.sso@lms.com", "Existing SSO", "Admin123!a", "Broker");
        var updated = await fixture.UserAccountService.UpsertExternalUserAsync(existing.Email, "Existing SSO Updated");

        Assert.Equal(existing.Id, updated.Id);
        Assert.Equal("Existing SSO Updated", updated.DisplayName);
        Assert.Equal("Broker", updated.Role);
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
