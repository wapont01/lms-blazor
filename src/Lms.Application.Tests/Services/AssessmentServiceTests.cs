using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class AssessmentServiceTests
{
    [Fact]
    public async Task EnsureDefaultAssessmentForCourseAsync_CreatesTenQuestionFinalAssessment()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Ten Question Course");

        var assessment = await fixture.AssessmentService.GetCourseAssessmentAsync(seed.CourseId);

        Assert.NotNull(assessment);
        Assert.Equal(10, assessment!.Questions.Count);

        var correctAnswers = await fixture.AssessmentService.GetCorrectAnswerMapAsync(seed.CourseId);
        Assert.Equal(10, correctAnswers.Count);
    }

    [Fact]
    public async Task SaveAssessmentEditorAsync_ResetsAttempts_AndRevokesExistingCertificates()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");

        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Editable Assessment Course");
        await fixture.PassAssessmentAsync(seed.CourseId, learner.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learner.Id, seed.RequiredLessonIds);

        var beforeCertificate = await fixture.DbContext.CompletionCertificates
            .AsNoTracking()
            .SingleOrDefaultAsync(certificate => certificate.EnrollmentId == seed.EnrollmentId);

        Assert.NotNull(beforeCertificate);
        Assert.False(beforeCertificate!.IsRevoked);

        var editor = await fixture.AssessmentService.GetAssessmentEditorAsync(seed.CourseId);
        Assert.NotNull(editor);

        editor!.Title = "Updated Final Assessment";
        editor.PassPercent = 90m;
        editor.Questions[0].Prompt = "Updated prompt";

        await fixture.AssessmentService.SaveAssessmentEditorAsync(seed.CourseId, editor, admin.Id, admin.Email);

        var attemptCount = await fixture.DbContext.AssessmentAttempts
            .CountAsync(attempt => attempt.UserAccountId == learner.Id);
        Assert.Equal(0, attemptCount);

        var afterCertificate = await fixture.DbContext.CompletionCertificates
            .AsNoTracking()
            .SingleAsync(certificate => certificate.EnrollmentId == seed.EnrollmentId);

        Assert.True(afterCertificate.IsRevoked);
        Assert.Contains("Assessment updated", afterCertificate.RevocationReason ?? string.Empty);
    }

    [Fact]
    public async Task SubmitAttemptAsync_EnforcesPolicyLimit_AndGrantAllowsAdditionalAttempt()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");

        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Retake Limits Course");

        var editor = await fixture.AssessmentService.GetAssessmentEditorAsync(seed.CourseId);
        Assert.NotNull(editor);
        editor!.RetakeCooldownMinutes = 0;
        await fixture.AssessmentService.SaveAssessmentEditorAsync(seed.CourseId, editor, admin.Id, admin.Email);

        var view = await fixture.AssessmentService.GetCourseAssessmentAsync(seed.CourseId);
        Assert.NotNull(view);

        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");
        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers));

        await fixture.AssessmentService.GrantRetakeAsync(seed.CourseId, learner.Id, 1, false, admin.Id, admin.Email);

        var fourthAttempt = await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        Assert.False(fourthAttempt.Passed);
    }

    [Fact]
    public async Task SubmitAttemptAsync_PrelicensingAllowsOneRetakeByDefault()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Prelicensing Retake Course");
        var course = await fixture.DbContext.Courses.SingleAsync(existing => existing.Id == seed.CourseId);
        course.ComplianceType = CourseComplianceTypes.Prelicensing;
        await fixture.DbContext.SaveChangesAsync();

        var view = await fixture.AssessmentService.GetCourseAssessmentAsync(seed.CourseId);
        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");

        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers));
    }

    [Fact]
    public async Task SubmitAttemptAsync_PostlicensingAllowsUnlimitedRetakesByDefault()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Postlicensing Retake Course");
        var course = await fixture.DbContext.Courses.SingleAsync(existing => existing.Id == seed.CourseId);
        course.ComplianceType = CourseComplianceTypes.Postlicensing;
        await fixture.DbContext.SaveChangesAsync();

        var view = await fixture.AssessmentService.GetCourseAssessmentAsync(seed.CourseId);
        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        }

        var eligibility = await fixture.AssessmentService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.True(eligibility.HasUnlimitedAttempts);
        Assert.Equal(5, eligibility.AttemptsUsed);
    }

    [Fact]
    public async Task GrantRetakeAsync_ResetCooldownTimer_AllowsImmediateRetry()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");

        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Cooldown Reset Course");

        var editor = await fixture.AssessmentService.GetAssessmentEditorAsync(seed.CourseId);
        Assert.NotNull(editor);
        editor!.RetakeCooldownMinutes = 30;
        await fixture.AssessmentService.SaveAssessmentEditorAsync(seed.CourseId, editor, admin.Id, admin.Email);

        var view = await fixture.AssessmentService.GetCourseAssessmentAsync(seed.CourseId);
        Assert.NotNull(view);

        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");
        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers));

        await fixture.AssessmentService.GrantRetakeAsync(seed.CourseId, learner.Id, 1, true, admin.Id, admin.Email);

        var retryAttempt = await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        Assert.False(retryAttempt.Passed);
    }

    [Fact]
    public async Task GetEligibilityAsync_AfterGrant_IncreasesAttemptsAllowed_AndClearsRetakeLimitBlock()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Eligibility Retake Grant Course");

        var editor = await fixture.AssessmentService.GetAssessmentEditorAsync(seed.CourseId);
        Assert.NotNull(editor);
        editor!.RetakeCooldownMinutes = 0;
        await fixture.AssessmentService.SaveAssessmentEditorAsync(seed.CourseId, editor, admin.Id, admin.Email);

        var view = await fixture.AssessmentService.GetCourseAssessmentAsync(seed.CourseId);
        Assert.NotNull(view);

        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");
        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);

        var beforeGrant = await fixture.AssessmentService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.True(beforeGrant.RequiresAssessment);
        Assert.False(beforeGrant.HasPassed);
        Assert.Equal(2, beforeGrant.AttemptsUsed);
        Assert.Equal(2, beforeGrant.AttemptsAllowed);
        Assert.Equal("Retake limit reached. Contact an admin for additional attempts.", beforeGrant.BlockingReason);

        await fixture.AssessmentService.GrantRetakeAsync(seed.CourseId, learner.Id, 1, false, admin.Id, admin.Email);

        var afterGrant = await fixture.AssessmentService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.True(afterGrant.RequiresAssessment);
        Assert.False(afterGrant.HasPassed);
        Assert.True(afterGrant.HasRetakeGrantOverride);
        Assert.Equal(2, afterGrant.AttemptsUsed);
        Assert.Equal(3, afterGrant.AttemptsAllowed);
        Assert.NotEqual("Retake limit reached. Contact an admin for additional attempts.", afterGrant.BlockingReason);
    }

    [Fact]
    public async Task GetEligibilityAsync_WhenRetakeGrantExists_OverridesHistoricalPassAndAllowsRetryAccess()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Retake Override Course");

        var view = await fixture.AssessmentService.GetCourseAssessmentAsync(seed.CourseId);
        Assert.NotNull(view);

        var correctAnswers = await fixture.AssessmentService.GetCorrectAnswerMapAsync(seed.CourseId);
        var passingAnswers = view!.Questions.ToDictionary(question => question.Id, question => correctAnswers[question.Id]);
        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, passingAnswers);
        var historicalPass = await fixture.AssessmentService.HasPassedRequiredAssessmentAsync(seed.CourseId, learner.Id);
        Assert.True(historicalPass);

        await fixture.AssessmentService.GrantRetakeAsync(seed.CourseId, learner.Id, 1, false, admin.Id, admin.Email);

        var eligibility = await fixture.AssessmentService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.True(eligibility.HasRetakeGrantOverride);
        Assert.False(eligibility.HasPassed);
        Assert.False(await fixture.AssessmentService.HasPassedRequiredAssessmentAsync(seed.CourseId, learner.Id));
        Assert.NotNull(eligibility.BlockingReason);
    }

    [Fact]
    public async Task GetAttemptHistoryAsync_ReturnsAttemptsWithAnswerReview()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "History Course");

        var view = await fixture.AssessmentService.GetCourseAssessmentAsync(seed.CourseId);
        Assert.NotNull(view);

        var answers = view!.Questions.ToDictionary(question => question.Id, _ => "D");
        await fixture.AssessmentService.SubmitAttemptAsync(seed.CourseId, learner.Id, answers);

        var history = await fixture.AssessmentService.GetAttemptHistoryAsync(seed.CourseId, learner.Id);

        Assert.Single(history);
        Assert.Equal(1, history[0].AttemptNumber);
        Assert.True(history[0].Answers.Count > 0);
    }

    [Fact]
    public async Task RetakeGrantSupport_CanListUpdateAndRevoke()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Grant Support Course");

        await fixture.AssessmentService.GrantRetakeAsync(seed.CourseId, learner.Id, 2, false, admin.Id, admin.Email);

        var grants = await fixture.AssessmentService.GetRetakeGrantsAsync(seed.CourseId);
        Assert.Single(grants);
        Assert.Equal(2, grants[0].GrantedAttempts);

        await fixture.AssessmentService.SetRetakeGrantAttemptsAsync(seed.CourseId, learner.Id, 5, false, admin.Id, admin.Email);

        grants = await fixture.AssessmentService.GetRetakeGrantsAsync(seed.CourseId);
        Assert.Single(grants);
        Assert.Equal(5, grants[0].GrantedAttempts);

        await fixture.AssessmentService.RevokeRetakeGrantAsync(seed.CourseId, learner.Id, admin.Id, admin.Email);

        grants = await fixture.AssessmentService.GetRetakeGrantsAsync(seed.CourseId);
        Assert.Empty(grants);
    }

    [Fact]
    public async Task ProctoringSessionLifecycle_ControlsAssessmentEligibility()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Proctored Course");
        var course = await fixture.DbContext.Courses.SingleAsync(existing => existing.Id == seed.CourseId);
        course.RequiresProctoredExam = true;
        await fixture.DbContext.SaveChangesAsync();

        var blocked = await fixture.AssessmentService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.Contains("proctoring session", blocked.BlockingReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(blocked.RequiresProctorVerification);

        var firstSession = await fixture.AssessmentService.RegisterProctoringSessionAsync(
            seed.CourseId, learner.Id, ProctoringDefaults.CompanyName, null, 120, admin.Id, admin.Email);
        Assert.Equal(ProctoringDefaults.CompanyName, firstSession.ProctorName);
        Assert.NotNull(await fixture.AssessmentService.GetActiveProctoringSessionAsync(seed.CourseId, learner.Id));
        Assert.False((await fixture.AssessmentService.GetEligibilityAsync(seed.CourseId, learner.Id)).RequiresProctorVerification);

        var replacement = await fixture.AssessmentService.RegisterProctoringSessionAsync(
            seed.CourseId, learner.Id, ProctoringDefaults.CompanyName, "replacement", 120, admin.Id, admin.Email);
        var expiredFirst = await fixture.DbContext.ExamProctoringSessions.AsNoTracking().SingleAsync(session => session.Id == firstSession.Id);
        Assert.True(expiredFirst.ExpiresAtUtc <= replacement.IdentityVerifiedAtUtc);

        await fixture.AssessmentService.ReportProctoringIncidentAsync(replacement.Id, "Unauthorized material observed.", admin.Id, admin.Email);
        Assert.Null(await fixture.AssessmentService.GetActiveProctoringSessionAsync(seed.CourseId, learner.Id));

        var finalSession = await fixture.AssessmentService.RegisterProctoringSessionAsync(
            seed.CourseId, learner.Id, ProctoringDefaults.CompanyName, null, 120, admin.Id, admin.Email);
        fixture.DbContext.ExamProctoringSessions.Add(new ExamProctoringSession
        {
            CourseId = seed.CourseId,
            UserAccountId = learner.Id,
            ProctorName = ProctoringDefaults.CompanyName,
            IdentityVerifiedAtUtc = DateTime.UtcNow,
            ClosedBookConfirmed = true,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(120)
        });
        await fixture.DbContext.SaveChangesAsync();
        await fixture.AssessmentService.ExpireProctoringSessionAsync(finalSession.Id, admin.Id, admin.Email);
        Assert.Null(await fixture.AssessmentService.GetActiveProctoringSessionAsync(seed.CourseId, learner.Id));
    }

    [Fact]
    public void GetCompletionTimestampUtc_PrefersPersistedAssessmentTimestamp()
    {
        var persistedSubmissionUtc = new DateTime(2026, 08, 21, 17, 30, 00, DateTimeKind.Utc);
        var queryTimestampUtc = new DateTime(2026, 08, 21, 18, 05, 00, DateTimeKind.Utc);
        var fallbackUtc = new DateTime(2026, 08, 21, 18, 30, 00, DateTimeKind.Utc);

        var resolvedUtc = AssessmentCompletionTimestampResolver.GetCompletionTimestampUtc(
            persistedSubmissionUtc,
            queryTimestampUtc,
            fallbackUtc);

        Assert.Equal(persistedSubmissionUtc, resolvedUtc);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IEnrollmentService EnrollmentService { get; }
        public IAssessmentService AssessmentService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IEnrollmentService enrollmentService, IAssessmentService assessmentService)
        {
            _connection = connection;
            DbContext = dbContext;
            EnrollmentService = enrollmentService;
            AssessmentService = assessmentService;
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

            var assessmentService = new AssessmentService(dbContext, auditLogService);
            var enrollmentService = new EnrollmentService(dbContext, auditLogService, assessmentService, new SchoolProfileService(dbContext, auditLogService));
            return new TestFixture(connection, dbContext, enrollmentService, assessmentService);
        }

        public async Task<(Guid CourseId, Guid EnrollmentId, List<Guid> RequiredLessonIds)> CreateCourseGraphWithEnrollmentAsync(Guid learnerId, string title)
        {
            var course = new Course
            {
                Title = title,
                Slug = Guid.NewGuid().ToString("N"),
                Description = "Test course",
                Level = "Beginner",
                DurationHours = 2,
                CreditHours = 2,
                Price = 0,
                IsPublished = true
            };

            DbContext.Courses.Add(course);

            var module = new Module
            {
                Course = course,
                Title = "Module 1",
                OrderIndex = 1
            };

            DbContext.Modules.Add(module);

            var lessonA = new Lesson
            {
                Module = module,
                Title = "Lesson A",
                ContentType = "Text",
                TextContent = "A",
                DurationMinutes = 10,
                OrderIndex = 1,
                IsRequired = true
            };

            var lessonB = new Lesson
            {
                Module = module,
                Title = "Lesson B",
                ContentType = "Text",
                TextContent = "B",
                DurationMinutes = 10,
                OrderIndex = 2,
                IsRequired = true
            };

            DbContext.Lessons.AddRange(lessonA, lessonB);

            var enrollment = new Enrollment
            {
                UserAccountId = learnerId,
                Course = course,
                EnrollmentSource = "LearnerPurchase",
                ConsentStatus = "NotRequired",
                ProgressPercent = 0,
                Completed = false,
                EnrolledAt = DateTime.UtcNow
            };

            DbContext.Enrollments.Add(enrollment);
            await DbContext.SaveChangesAsync();

            await AssessmentService.EnsureDefaultAssessmentForCourseAsync(course.Id);

            return (course.Id, enrollment.Id, [lessonA.Id, lessonB.Id]);
        }

        public async Task CompleteAllRequiredLessonsAsync(Guid learnerId, List<Guid> lessonIds)
        {
            foreach (var lessonId in lessonIds)
            {
                await EnrollmentService.SetLessonCompletionAsync(learnerId, lessonId, true);
            }
        }

        public async Task PassAssessmentAsync(Guid courseId, Guid learnerId)
        {
            await AssessmentService.EnsureDefaultAssessmentForCourseAsync(courseId);

            var assessment = await DbContext.CourseAssessments
                .Include(existing => existing.Questions)
                .FirstAsync(existing => existing.CourseId == courseId && existing.IsRequired);

            var answers = assessment.Questions.ToDictionary(question => question.Id, question => question.CorrectOption);
            await AssessmentService.SubmitAttemptAsync(courseId, learnerId, answers);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

