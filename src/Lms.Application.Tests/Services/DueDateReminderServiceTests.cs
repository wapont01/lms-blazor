using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class DueDateReminderServiceTests
{
    [Fact]
    public async Task ProcessRemindersAsync_SendsDueSoonAndOverdueNotificationsOnce()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var courses = await fixture.DbContext.Courses.OrderBy(course => course.Title).Take(2).ToListAsync();

        var now = DateTime.UtcNow;

        fixture.DbContext.Enrollments.Add(new Enrollment
        {
            UserAccountId = learner.Id,
            CourseId = courses[0].Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = now,
            DueAtUtc = now.AddDays(2),
            ProgressPercent = 10,
            Completed = false
        });

        fixture.DbContext.Enrollments.Add(new Enrollment
        {
            UserAccountId = learner.Id,
            CourseId = courses[1].Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = now,
            DueAtUtc = now.AddDays(-1),
            ProgressPercent = 20,
            Completed = false
        });

        await fixture.DbContext.SaveChangesAsync();

        var firstRun = await fixture.DueDateReminderService.ProcessRemindersAsync(now);
        Assert.Equal(1, firstRun.DueSoonSent);
        Assert.Equal(1, firstRun.OverdueSent);

        var secondRun = await fixture.DueDateReminderService.ProcessRemindersAsync(now.AddHours(1));
        Assert.Equal(0, secondRun.DueSoonSent);
        Assert.Equal(0, secondRun.OverdueSent);

        var notifications = await fixture.DbContext.SystemNotifications
            .Where(notification => notification.RecipientUserId == learner.Id)
            .ToListAsync();

        Assert.Equal(2, notifications.Count);
    }

    [Fact]
    public async Task SetDueDateAsync_UpdatesDueDateAndResetsReminderFlags()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var course = await fixture.DbContext.Courses.FirstAsync();

        var enrollment = new Enrollment
        {
            UserAccountId = learner.Id,
            CourseId = course.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow,
            DueAtUtc = DateTime.UtcNow.AddDays(1),
            DueSoonReminderSentAt = DateTime.UtcNow,
            OverdueReminderSentAt = DateTime.UtcNow,
            ProgressPercent = 0,
            Completed = false
        };

        fixture.DbContext.Enrollments.Add(enrollment);
        await fixture.DbContext.SaveChangesAsync();

        var targetDueDate = DateTime.UtcNow.AddDays(7);
        var updated = await fixture.DueDateReminderService.SetDueDateAsync(learner.Id, course.Id, targetDueDate, learner.Id, learner.Email);

        Assert.True(updated);

        var refreshed = await fixture.DbContext.Enrollments.FirstAsync(existing => existing.Id == enrollment.Id);
        Assert.NotNull(refreshed.DueAtUtc);
        Assert.Equal(DateTimeKind.Utc, refreshed.DueAtUtc!.Value.Kind);
        Assert.Null(refreshed.DueSoonReminderSentAt);
        Assert.Null(refreshed.OverdueReminderSentAt);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IDueDateReminderService DueDateReminderService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IDueDateReminderService dueDateReminderService)
        {
            _connection = connection;
            DbContext = dbContext;
            DueDateReminderService = dueDateReminderService;
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
            var notificationService = new NotificationService(dbContext);
            var userAccountService = new UserAccountService(dbContext, auditLogService, notificationService);
            await userAccountService.EnsureSeedUsersAsync();
            await CourseSeed.SeedAsync(dbContext);

            var dueDateReminderService = new DueDateReminderService(dbContext, notificationService, auditLogService);
            return new TestFixture(connection, dbContext, dueDateReminderService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
