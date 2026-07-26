using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class NotificationServiceTests
{
    [Fact]
    public async Task MarkAsReadAsync_UpdatesUnreadCount_ForRecipientOnly()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var otherLearner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner2@lms.com");

        await fixture.NotificationService.CreateAsync(learner.Id, "enrollment", "Enrollment created", "You were enrolled in a course.");
        await fixture.NotificationService.CreateAsync(learner.Id, "assignment", "Broker assigned", "A broker was assigned to your portfolio.");
        await fixture.NotificationService.CreateAsync(otherLearner.Id, "assignment", "Broker assigned", "A broker was assigned to your portfolio.");

        var beforeUnread = await fixture.NotificationService.GetUnreadCountAsync(learner.Id);
        Assert.Equal(2, beforeUnread);

        var firstNotification = (await fixture.NotificationService.GetForUserAsync(learner.Id, unreadOnly: true, take: 10)).First();
        await fixture.NotificationService.MarkAsReadAsync(learner.Id, firstNotification.Id);

        var afterUnread = await fixture.NotificationService.GetUnreadCountAsync(learner.Id);
        var otherUnread = await fixture.NotificationService.GetUnreadCountAsync(otherLearner.Id);

        Assert.Equal(1, afterUnread);
        Assert.Equal(1, otherUnread);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_MarksOnlyTargetUserNotifications_AndReturnsCount()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var otherLearner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner2@lms.com");

        await fixture.NotificationService.CreateAsync(learner.Id, "enrollment", "Enrollment updated", "Your enrollment was corrected.");
        await fixture.NotificationService.CreateAsync(learner.Id, "assessment", "Assessment retake granted", "You have additional attempts.");
        await fixture.NotificationService.CreateAsync(otherLearner.Id, "assignment", "Broker assigned", "A broker was assigned to your portfolio.");

        var markedCount = await fixture.NotificationService.MarkAllAsReadAsync(learner.Id);

        var learnerUnread = await fixture.NotificationService.GetUnreadCountAsync(learner.Id);
        var otherUnread = await fixture.NotificationService.GetUnreadCountAsync(otherLearner.Id);

        Assert.Equal(2, markedCount);
        Assert.Equal(0, learnerUnread);
        Assert.Equal(1, otherUnread);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public INotificationService NotificationService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, INotificationService notificationService)
        {
            _connection = connection;
            DbContext = dbContext;
            NotificationService = notificationService;
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

            var notificationService = new NotificationService(dbContext);
            return new TestFixture(connection, dbContext, notificationService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}