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

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
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

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
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

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_RemindsProvisionalBrokerAtEighteenMonthDeadline()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        var initialLicensureDate = DateTime.UtcNow.AddMonths(-17).AddDays(-20);
        learner.InitialLicensureDate = initialLicensureDate;
        learner.LicenseStatus = "Provisional";
        learner.PostlicensingCompletedAt = null;
        await fixture.DbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var result = await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(now);

        Assert.Equal(1, result.Sent);
        Assert.Contains(
            await fixture.DbContext.SystemNotifications.Where(notification => notification.RecipientUserId == learner.Id).ToListAsync(),
            notification => notification.Category == "license-compliance" && notification.Message.Contains("postlicensing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_OffersStateMatchedCoursesForApplicableDeadline()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        learner.State = "North Carolina";
        learner.LicenseType = "PB";
        learner.InitialLicensureDate = DateTime.UtcNow.AddMonths(-17).AddDays(-20);
        learner.PostlicensingCompletedAt = null;
        fixture.DbContext.Courses.AddRange(
            CreatePublishedCourse("North Carolina Postlicensing", "North Carolina", CourseComplianceTypes.Postlicensing),
            CreatePublishedCourse("Florida Postlicensing", "Florida", CourseComplianceTypes.Postlicensing));
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(DateTime.UtcNow);

        var offering = await fixture.DbContext.SystemNotifications.SingleAsync(notification =>
            notification.RecipientUserId == learner.Id && notification.Category == "course-offering");

        Assert.Contains("North Carolina Postlicensing", offering.Message);
        Assert.DoesNotContain("Florida Postlicensing", offering.Message);
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_DoesNotDuplicateTheSameMilestone()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        learner.InitialLicensureDate = DateTime.UtcNow.AddMonths(-19);
        learner.LicenseStatus = "Provisional";
        learner.PostlicensingCompletedAt = null;
        await fixture.DbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var firstRun = await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(now);
        var secondRun = await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(now.AddHours(1));

        Assert.Equal(1, firstRun.Sent);
        Assert.Equal(0, secondRun.Sent);
        Assert.Equal(1, await fixture.DbContext.LicenseComplianceReminders.CountAsync(reminder => reminder.UserAccountId == learner.Id));
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_DerivedRenewalDateSendsSoftReminder()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        learner.LicenseRenewalDueDate = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        learner.LicenseRenewalDueDateSource = LicenseDateProvenance.Derived;
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc));

        var notification = await fixture.DbContext.SystemNotifications.SingleAsync(item => item.RecipientUserId == learner.Id && item.Category == "license-compliance");
        Assert.Equal("Estimated license date approaching", notification.Title);
        Assert.Contains("Soft reminder only", notification.Message);
        Assert.Contains("confirm the official date", notification.Message);
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_UnknownRenewalDateDoesNotSendRenewalReminder()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        learner.LicenseRenewalDueDate = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        learner.LicenseRenewalDueDateSource = LicenseDateProvenance.Unknown;
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc));

        Assert.DoesNotContain(
            await fixture.DbContext.SystemNotifications.Where(item => item.RecipientUserId == learner.Id).ToListAsync(),
            item => item.Category == "license-compliance" && item.Message.Contains("license renewal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_TexasAuthoritativeRenewalDrivesStateCeCycleReminder()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        learner.State = "TX";
        learner.LicenseType = "SA";
        learner.InitialLicensureDate = null;
        learner.ContinuingEducationDueDate = null;
        learner.LicenseRenewalDueDate = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
        learner.LicenseRenewalDueDateSource = LicenseDateProvenance.Authoritative;
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc));

        var notifications = await fixture.DbContext.SystemNotifications
            .Where(item => item.RecipientUserId == learner.Id && item.Category == "license-compliance")
            .ToListAsync();

        Assert.Contains(notifications, item =>
            item.Title == "License requirement approaching" &&
            item.Message.Contains("Texas Sales Agent continuing education requirement", StringComparison.Ordinal) &&
            item.Message.Contains("18 hours per 2-year cycle", StringComparison.Ordinal));
        Assert.True(await fixture.DbContext.LicenseComplianceReminders.AnyAsync(reminder =>
            reminder.UserAccountId == learner.Id && reminder.ReminderType == "state-ce-cycle-deadline-authoritative"));
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_ProjectedStateCeCycleSendsSoftReminderOnly()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        learner.State = "New York";
        learner.LicenseType = "Salesperson";
        learner.InitialLicensureDate = new DateTime(2024, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        learner.PostlicensingCompletedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        learner.ContinuingEducationDueDate = null;
        learner.LicenseRenewalDueDate = null;
        learner.LicenseRenewalDueDateSource = LicenseDateProvenance.Unknown;
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));

        var notifications = await fixture.DbContext.SystemNotifications
            .Where(item => item.RecipientUserId == learner.Id && item.Category == "license-compliance")
            .ToListAsync();

        Assert.Contains(notifications, item =>
            item.Title == "Estimated license date approaching" &&
            item.Message.Contains("Soft reminder only", StringComparison.Ordinal) &&
            item.Message.Contains("New York Salesperson continuing education requirement", StringComparison.Ordinal) &&
            item.Message.Contains("22.5 hours per 2-year cycle", StringComparison.Ordinal));
        Assert.True(await fixture.DbContext.LicenseComplianceReminders.AnyAsync(reminder =>
            reminder.UserAccountId == learner.Id && reminder.ReminderType == "state-ce-cycle-deadline-derived"));
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_GeorgiaSalespersonGetsTwelveMonthPostlicensingDeadline()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        learner.State = "Georgia";
        learner.LicenseType = "Salesperson";
        learner.InitialLicensureDate = new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        learner.PostlicensingCompletedAt = null;
        learner.ContinuingEducationDueDate = null;
        learner.LicenseRenewalDueDate = null;
        learner.LicenseRenewalDueDateSource = LicenseDateProvenance.Unknown;
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));

        var notifications = await fixture.DbContext.SystemNotifications
            .Where(item => item.RecipientUserId == learner.Id && item.Category == "license-compliance")
            .ToListAsync();

        Assert.Contains(notifications, item =>
            item.Message.Contains("Georgia Salesperson postlicensing education requirement", StringComparison.Ordinal));
        var reminder = await fixture.DbContext.LicenseComplianceReminders.SingleAsync(item =>
            item.UserAccountId == learner.Id && item.ReminderType == "postlicensing-deadline");
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), reminder.DueAtUtc);
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_SouthCarolinaAssociateHasNoPostlicensingDeadline()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        learner.State = "South Carolina";
        learner.LicenseType = "Associate";
        learner.InitialLicensureDate = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        learner.PostlicensingCompletedAt = null;
        learner.ContinuingEducationDueDate = null;
        learner.LicenseRenewalDueDate = null;
        learner.LicenseRenewalDueDateSource = LicenseDateProvenance.Unknown;
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(await fixture.DbContext.LicenseComplianceReminders.AnyAsync(item =>
            item.UserAccountId == learner.Id && item.ReminderType == "postlicensing-deadline"));
        Assert.DoesNotContain(
            await fixture.DbContext.SystemNotifications.Where(item => item.RecipientUserId == learner.Id).ToListAsync(),
            item => item.Message.Contains("postlicensing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_NorthCarolinaCeNotifiesLearnerBrokerAndAdmins()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "maya.thompson@wlr.com");
        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.broker@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Role == "Admin");
        learner.State = "North Carolina";
        learner.LicenseType = "B";
        learner.InitialLicensureDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        learner.PostlicensingCompletedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        learner.ContinuingEducationDueDate = null;
        fixture.DbContext.BrokerLearnerAssignments.Add(new BrokerLearnerAssignment
        {
            BrokerUserId = broker.Id,
            LearnerUserId = learner.Id
        });
        fixture.DbContext.Courses.Add(CreatePublishedCourse("North Carolina CE Update", "North Carolina", CourseComplianceTypes.ContinuingEducation));
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var notifications = await fixture.DbContext.SystemNotifications
            .Where(notification => notification.Category == "license-compliance" || notification.Category == "course-offering")
            .ToListAsync();

        Assert.Contains(notifications, notification => notification.RecipientUserId == learner.Id && notification.Message.Contains("North Carolina", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notifications, notification => notification.RecipientUserId == broker.Id && notification.Message.Contains("Maya", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notifications, notification => notification.RecipientUserId == admin.Id && notification.Message.Contains("Maya", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notifications, notification => notification.RecipientUserId == learner.Id && notification.Category == "course-offering" && notification.Message.Contains("North Carolina CE Update", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessLicenseComplianceRemindersAsync_UsesLinkedFloridaRenewalAsFirstPostlicenseDeadline()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "sofia.martinez.fl@wlr.com");
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "Florida",
            SourceName = "DBPR capture",
            SourceUrl = "https://example.test/florida",
            RetrievedAtUtc = DateTime.UtcNow,
            Status = RegulatorySnapshotStatuses.Approved
        };
        var licensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "Florida",
            LicenseNumber = learner.LicenseNumber!,
            Name = learner.DisplayName,
            LicenseType = "SL",
            LicenseStatus = "Current",
            LicenseIssuedDate = new DateOnly(2026, 1, 10),
            LicenseRenewalDate = new DateOnly(2026, 9, 30)
        };
        fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
        fixture.DbContext.RegulatoryLicensees.Add(licensee);
        fixture.DbContext.UserRegulatoryLicenseLinks.Add(new UserRegulatoryLicenseLink
        {
            UserAccountId = learner.Id,
            RegulatorySnapshot = snapshot,
            RegulatoryLicensee = licensee
        });
        await fixture.DbContext.SaveChangesAsync();

        await fixture.DueDateReminderService.ProcessLicenseComplianceRemindersAsync(new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc));

        Assert.Contains(
            await fixture.DbContext.SystemNotifications.Where(notification => notification.RecipientUserId == learner.Id).ToListAsync(),
            notification => notification.Category == "license-compliance"
                && notification.Message.Contains("Florida first-renewal postlicensing", StringComparison.OrdinalIgnoreCase)
                && notification.Message.Contains("2026-09-30", StringComparison.Ordinal));
    }

    private static Course CreatePublishedCourse(string title, string state, string complianceType) => new()
    {
        Title = title,
        Slug = Guid.NewGuid().ToString("N"),
        Description = "State-aware offering test course",
        Level = "Beginner",
        DurationHours = 1,
        CreditHours = 1,
        Price = 0,
        State = state,
        ComplianceType = complianceType,
        IsPublished = true
    };

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
