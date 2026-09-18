using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class ComplianceReportingServiceTests
{
    [Fact]
    public async Task GetPendingCourseCompletionReportsAsync_ReturnsOnlyUnsubmittedCompletedEnrollments()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        var instructor = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "jordan.lee@wlr.com");

        var course = new Course
        {
            Title = "Completion Report Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            CommissionCourseNumber = "NC-CR-1",
            OwnerInstructorId = instructor.Id,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var completedAtUtc = DateTime.UtcNow.AddDays(-1);
        var enrollment = new Enrollment
        {
            UserAccountId = learner.Id,
            CourseId = course.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow.AddDays(-5),
            ProgressPercent = 100,
            Completed = true,
            CompletedAtUtc = completedAtUtc
        };
        fixture.DbContext.Enrollments.Add(enrollment);

        var otherLearner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "maya.thompson@wlr.com");
        var notCompletedEnrollment = new Enrollment
        {
            UserAccountId = otherLearner.Id,
            CourseId = course.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow,
            ProgressPercent = 40,
            Completed = false
        };
        fixture.DbContext.Enrollments.Add(notCompletedEnrollment);

        await fixture.DbContext.SaveChangesAsync();

        var rows = await fixture.ComplianceReportingService.GetPendingCourseCompletionReportsAsync();

        var row = Assert.Single(rows.Where(existing => existing.EnrollmentId == enrollment.Id));
        Assert.Equal(learner.Email, row.StudentEmail);
        Assert.Equal("NC-CR-1", row.CommissionCourseNumber);
        Assert.Equal(instructor.LegalName ?? instructor.DisplayName, row.InstructorName);
        Assert.Equal(completedAtUtc.AddDays(7), row.DueAtUtc);
        Assert.False(row.IsOverdue);
    }

    [Fact]
    public async Task MarkCourseCompletionReportSubmittedAsync_ExcludesEnrollmentFromPendingList()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");

        var course = new Course
        {
            Title = "Submitted Report Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var enrollment = new Enrollment
        {
            UserAccountId = learner.Id,
            CourseId = course.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow.AddDays(-5),
            ProgressPercent = 100,
            Completed = true,
            CompletedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        fixture.DbContext.Enrollments.Add(enrollment);
        await fixture.DbContext.SaveChangesAsync();

        var marked = await fixture.ComplianceReportingService.MarkCourseCompletionReportSubmittedAsync(enrollment.Id, admin.Id, admin.Email);
        Assert.True(marked);

        var repeated = await fixture.ComplianceReportingService.MarkCourseCompletionReportSubmittedAsync(enrollment.Id, admin.Id, admin.Email);
        Assert.False(repeated);

        var rows = await fixture.ComplianceReportingService.GetPendingCourseCompletionReportsAsync();
        Assert.DoesNotContain(rows, row => row.EnrollmentId == enrollment.Id);
    }

    [Fact]
    public async Task GetMonthlySummaryReportAsync_CountsEnrollmentsCompletionsAndExamFailsWithinMonth()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");

        var course = new Course
        {
            Title = "Monthly Summary Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var targetMonthUtc = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var enrollment = new Enrollment
        {
            UserAccountId = learner.Id,
            CourseId = course.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = targetMonthUtc,
            ProgressPercent = 100,
            Completed = true,
            CompletedAtUtc = targetMonthUtc.AddDays(2)
        };
        fixture.DbContext.Enrollments.Add(enrollment);

        var assessment = new CourseAssessment
        {
            CourseId = course.Id,
            Title = "Final",
            PassPercent = 80,
            IsRequired = true
        };
        fixture.DbContext.CourseAssessments.Add(assessment);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.AssessmentAttempts.Add(new AssessmentAttempt
        {
            CourseAssessmentId = assessment.Id,
            UserAccountId = learner.Id,
            StartedAt = targetMonthUtc,
            SubmittedAt = targetMonthUtc,
            ScorePercent = 40,
            Passed = false
        });
        await fixture.DbContext.SaveChangesAsync();

        var rows = await fixture.ComplianceReportingService.GetMonthlySummaryReportAsync(2026, 3);

        var row = Assert.Single(rows.Where(existing => existing.CourseId == course.Id));
        Assert.Equal(1, row.EnrollmentCount);
        Assert.Equal(1, row.CompletionCount);
        Assert.Equal(1, row.ExamFailCount);
    }

    [Fact]
    public async Task GetPendingCheatingIncidentReportsAsync_ReturnsOnlyUnreportedIncidents()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");

        var course = new Course
        {
            Title = "Proctored Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            CommissionCourseNumber = "NC-PR-1",
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var incidentAtUtc = DateTime.UtcNow.AddDays(-2);
        var incidentSession = new ExamProctoringSession
        {
            CourseId = course.Id,
            UserAccountId = learner.Id,
            ProctorName = "ProctorCo",
            IdentityVerifiedAtUtc = incidentAtUtc.AddMinutes(-30),
            ClosedBookConfirmed = true,
            ExpiresAtUtc = incidentAtUtc,
            SecurityIncidentReported = true,
            SecurityIncidentNotes = "Unauthorized material observed."
        };
        var cleanSession = new ExamProctoringSession
        {
            CourseId = course.Id,
            UserAccountId = learner.Id,
            ProctorName = "ProctorCo",
            IdentityVerifiedAtUtc = DateTime.UtcNow,
            ClosedBookConfirmed = true,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(120)
        };
        fixture.DbContext.ExamProctoringSessions.AddRange(incidentSession, cleanSession);
        await fixture.DbContext.SaveChangesAsync();

        var rows = await fixture.ComplianceReportingService.GetPendingCheatingIncidentReportsAsync();

        var row = Assert.Single(rows);
        Assert.Equal(incidentSession.Id, row.ProctoringSessionId);
        Assert.Equal("NC-PR-1", row.CommissionCourseNumber);
        Assert.Equal(incidentAtUtc.AddDays(10), row.DueAtUtc);
        Assert.False(row.IsOverdue);
    }

    [Fact]
    public async Task MarkCheatingIncidentReportedAsync_ExcludesSessionFromPendingList()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");

        var course = new Course
        {
            Title = "Proctored Course Two",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var incidentSession = new ExamProctoringSession
        {
            CourseId = course.Id,
            UserAccountId = learner.Id,
            ProctorName = "ProctorCo",
            IdentityVerifiedAtUtc = DateTime.UtcNow,
            ClosedBookConfirmed = true,
            ExpiresAtUtc = DateTime.UtcNow,
            SecurityIncidentReported = true,
            SecurityIncidentNotes = "Unauthorized material observed."
        };
        fixture.DbContext.ExamProctoringSessions.Add(incidentSession);
        await fixture.DbContext.SaveChangesAsync();

        var marked = await fixture.ComplianceReportingService.MarkCheatingIncidentReportedAsync(incidentSession.Id, admin.Id, admin.Email);
        Assert.True(marked);

        var repeated = await fixture.ComplianceReportingService.MarkCheatingIncidentReportedAsync(incidentSession.Id, admin.Id, admin.Email);
        Assert.False(repeated);

        var rows = await fixture.ComplianceReportingService.GetPendingCheatingIncidentReportsAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetEducationDirectorOrientationStatusAsync_FlagsOverdueWhenVideoNotViewedWithin30Days()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await fixture.ComplianceReportingService.GetEducationDirectorOrientationStatusAsync();
        var profile = await fixture.DbContext.SchoolProfiles.SingleAsync();
        profile.EducationDirectorDesignatedAtUtc = DateTime.UtcNow.AddDays(-31);
        await fixture.DbContext.SaveChangesAsync();

        var status = await fixture.ComplianceReportingService.GetEducationDirectorOrientationStatusAsync();
        Assert.True(status.IsOverdue);

        await fixture.ComplianceReportingService.MarkEducationDirectorOrientationViewedAsync(Guid.NewGuid(), "admin@lms.com");

        var updatedStatus = await fixture.ComplianceReportingService.GetEducationDirectorOrientationStatusAsync();
        Assert.NotNull(updatedStatus.ViewedAtUtc);
        Assert.False(updatedStatus.IsOverdue);
    }

    [Fact]
    public async Task GetInstructorComplianceStatusAsync_FlagsOverdueHireReportAndObservation()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        await fixture.ComplianceReportingService.GetEducationDirectorOrientationStatusAsync();
        var profile = await fixture.DbContext.SchoolProfiles.SingleAsync();
        var staffMember = new SchoolStaffMember
        {
            SchoolProfileId = profile.Id,
            Name = "New Instructor",
            Role = SchoolStaffRoles.Instructor,
            AddedAtUtc = DateTime.UtcNow.AddDays(-400)
        };
        fixture.DbContext.SchoolStaffMembers.Add(staffMember);
        await fixture.DbContext.SaveChangesAsync();

        var rows = await fixture.ComplianceReportingService.GetInstructorComplianceStatusAsync();
        var row = Assert.Single(rows.Where(existing => existing.StaffMemberId == staffMember.Id));
        Assert.True(row.IsHireReportOverdue);
        Assert.True(row.IsObservationOverdue);

        var hireMarked = await fixture.ComplianceReportingService.MarkInstructorHireReportedAsync(staffMember.Id, admin.Id, admin.Email);
        Assert.True(hireMarked);

        var observed = await fixture.ComplianceReportingService.RecordInstructorObservationAsync(staffMember.Id, admin.Id, admin.Email);
        Assert.True(observed);

        var updatedRows = await fixture.ComplianceReportingService.GetInstructorComplianceStatusAsync();
        var updatedRow = Assert.Single(updatedRows.Where(existing => existing.StaffMemberId == staffMember.Id));
        Assert.False(updatedRow.IsHireReportOverdue);
        Assert.False(updatedRow.IsObservationOverdue);
    }

    [Fact]
    public async Task GetRecordRetentionSummaryAsync_FlagsCategoriesWithRecordsOlderThanThreeYears()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");

        var course = new Course
        {
            Title = "Retention Test Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var oldEnrollment = new Enrollment
        {
            UserAccountId = learner.Id,
            CourseId = course.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow.AddYears(-4),
            ProgressPercent = 100,
            Completed = true
        };
        fixture.DbContext.Enrollments.Add(oldEnrollment);
        await fixture.DbContext.SaveChangesAsync();

        var rows = await fixture.ComplianceReportingService.GetRecordRetentionSummaryAsync();

        var enrollmentSummary = Assert.Single(rows.Where(row => row.CategoryName == "Enrollment & Attendance Records"));
        Assert.Equal(1, enrollmentSummary.RecordCount);
        Assert.Equal(3, enrollmentSummary.RetentionYears);
        Assert.True(enrollmentSummary.HasRecordsEligibleForArchivalReview);

        var disclosureSummary = Assert.Single(rows.Where(row => row.CategoryName == "Policy Disclosure Acknowledgments"));
        Assert.Equal(0, disclosureSummary.RecordCount);
        Assert.False(disclosureSummary.HasRecordsEligibleForArchivalReview);
    }

    [Fact]
    public async Task ScheduleCourseSessionAsync_FlagsJuneBlackoutAndDailyDurationWarnings()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "CE Elective Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            ComplianceType = CourseComplianceTypes.ContinuingEducation,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var blackoutStart = new DateTime(2027, 6, 15, 9, 0, 0, DateTimeKind.Utc);

        var result = await fixture.ComplianceReportingService.ScheduleCourseSessionAsync(course.Id, blackoutStart, blackoutStart.AddHours(9), admin.Id, admin.Email);

        Assert.Contains(result.Warnings, warning => warning.Contains("blackout", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("daily maximum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetPendingScheduleNotificationsAsync_ComputesTenDayDueDateAndMarksSubmitted()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "Scheduled Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var start = DateTime.UtcNow.AddDays(5);
        var scheduleResult = await fixture.ComplianceReportingService.ScheduleCourseSessionAsync(course.Id, start, start.AddHours(4), admin.Id, admin.Email);

        var pending = await fixture.ComplianceReportingService.GetPendingScheduleNotificationsAsync();
        var row = Assert.Single(pending.Where(existing => existing.SessionId == scheduleResult.SessionId));
        Assert.Equal(start.AddDays(-10), row.DueAtUtc);
        Assert.True(row.IsOverdue);

        var marked = await fixture.ComplianceReportingService.MarkScheduleNotificationSentAsync(scheduleResult.SessionId, admin.Id, admin.Email);
        Assert.True(marked);

        var pendingAfter = await fixture.ComplianceReportingService.GetPendingScheduleNotificationsAsync();
        Assert.DoesNotContain(pendingAfter, existing => existing.SessionId == scheduleResult.SessionId);
    }

    [Fact]
    public async Task RescheduleCourseSessionAsync_FlagsPendingChangeReport_WhenWithinFiveDaysOfStart()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "Reschedule Test Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var originalStart = DateTime.UtcNow.AddDays(2);
        var scheduleResult = await fixture.ComplianceReportingService.ScheduleCourseSessionAsync(course.Id, originalStart, originalStart.AddHours(4), admin.Id, admin.Email);

        var rescheduled = await fixture.ComplianceReportingService.RescheduleCourseSessionAsync(scheduleResult.SessionId, originalStart.AddDays(1), originalStart.AddDays(1).AddHours(4), admin.Id, admin.Email);
        Assert.True(rescheduled);

        var pendingChanges = await fixture.ComplianceReportingService.GetPendingScheduleChangeReportsAsync();
        var row = Assert.Single(pendingChanges.Where(existing => existing.SessionId == scheduleResult.SessionId));
        Assert.False(row.IsOverdue);

        var reported = await fixture.ComplianceReportingService.MarkScheduleChangeReportedAsync(scheduleResult.SessionId, admin.Id, admin.Email);
        Assert.True(reported);

        var pendingAfter = await fixture.ComplianceReportingService.GetPendingScheduleChangeReportsAsync();
        Assert.DoesNotContain(pendingAfter, existing => existing.SessionId == scheduleResult.SessionId);
    }

    [Fact]
    public async Task ProcessComplianceRemindersAsync_NotifiesAdminsOnceForOverdueItems()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var course = new Course
        {
            Title = "Overdue Schedule Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.ComplianceReportingService.ScheduleCourseSessionAsync(course.Id, DateTime.UtcNow.AddDays(5), DateTime.UtcNow.AddDays(5).AddHours(4), admin.Id, admin.Email);

        var firstRun = await fixture.ComplianceReportingService.ProcessComplianceRemindersAsync();
        Assert.True(firstRun.NotificationsSent > 0);

        var notifications = await fixture.NotificationService.GetForUserAsync(admin.Id);
        Assert.Contains(notifications, notification => notification.Category == "compliance");

        var secondRun = await fixture.ComplianceReportingService.ProcessComplianceRemindersAsync();
        Assert.Equal(0, secondRun.NotificationsSent);
    }

    [Fact]
    public async Task GetScheduledCourseSessionsAsync_ExcludesCancelledSessions()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var course = new Course
        {
            Title = "Manage Sessions Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var start = DateTime.UtcNow.AddDays(20);
        var keepResult = await fixture.ComplianceReportingService.ScheduleCourseSessionAsync(course.Id, start, start.AddHours(4), admin.Id, admin.Email);
        var cancelResult = await fixture.ComplianceReportingService.ScheduleCourseSessionAsync(course.Id, start.AddDays(1), start.AddDays(1).AddHours(4), admin.Id, admin.Email);
        await fixture.ComplianceReportingService.CancelCourseSessionAsync(cancelResult.SessionId, admin.Id, admin.Email);

        var sessions = await fixture.ComplianceReportingService.GetScheduledCourseSessionsAsync();

        Assert.Contains(sessions, session => session.SessionId == keepResult.SessionId);
        Assert.DoesNotContain(sessions, session => session.SessionId == cancelResult.SessionId);
    }

    [Fact]
    public async Task ProcessComplianceRemindersAsync_ReNotifiesEducationDirectorOrientation_AfterReDesignation()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        await fixture.ComplianceReportingService.GetEducationDirectorOrientationStatusAsync();
        var profile = await fixture.DbContext.SchoolProfiles.SingleAsync();
        profile.EducationDirectorDesignatedAtUtc = DateTime.UtcNow.AddDays(-31);
        await fixture.DbContext.SaveChangesAsync();

        var firstRun = await fixture.ComplianceReportingService.ProcessComplianceRemindersAsync();
        Assert.True(firstRun.NotificationsSent > 0);

        var secondRun = await fixture.ComplianceReportingService.ProcessComplianceRemindersAsync();
        Assert.Equal(0, secondRun.NotificationsSent);

        // A later re-designation (new director) must be able to trigger a fresh reminder.
        profile.EducationDirectorDesignatedAtUtc = DateTime.UtcNow.AddDays(-45);
        await fixture.DbContext.SaveChangesAsync();

        var thirdRun = await fixture.ComplianceReportingService.ProcessComplianceRemindersAsync();
        Assert.True(thirdRun.NotificationsSent > 0);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IComplianceReportingService ComplianceReportingService { get; }
        public INotificationService NotificationService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IComplianceReportingService complianceReportingService, INotificationService notificationService)
        {
            _connection = connection;
            DbContext = dbContext;
            ComplianceReportingService = complianceReportingService;
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

            var schoolProfileService = new SchoolProfileService(dbContext, auditLogService);
            var notificationService = new NotificationService(dbContext);
            var complianceReportingService = new ComplianceReportingService(dbContext, auditLogService, schoolProfileService, notificationService);

            return new TestFixture(connection, dbContext, complianceReportingService, notificationService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
