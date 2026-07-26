using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class LearnerDashboardServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_AdminUsesRequestedLearnerAndComputesCompletedMetadata()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Dashboard Completion Course");

        await fixture.PassAssessmentAsync(seed.CourseId, learner.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learner.Id, seed.RequiredLessonIds);

        var dashboard = await fixture.LearnerDashboardService.GetDashboardAsync(admin.Id, isAdminViewer: true, learner.Id);

        Assert.Equal(learner.Id, dashboard.Context.LearnerUserId);
        Assert.Equal(learner.Email, dashboard.Context.Email);
        Assert.Equal(1, dashboard.Context.EnrolledCourseCount);
        Assert.Equal(1, dashboard.Context.CompletedCourseCount);
        Assert.Single(dashboard.Courses);

        var course = dashboard.Courses[0];
        Assert.Equal("Completed", course.Status);
        Assert.Equal(100m, course.ProgressPercent);
        Assert.NotNull(course.CertificateId);
    }

    [Fact]
    public async Task GetDashboardAsync_AdminKeepsExplicitSelectionForLearnerWithoutEnrollments()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var emptyLearner = await fixture.UserAccountService.CreateAsync("empty-learner@lms.com", "Empty Learner", "Learner123!", "Learner");

        var dashboard = await fixture.LearnerDashboardService.GetDashboardAsync(admin.Id, isAdminViewer: true, emptyLearner.Id);

        Assert.Equal(emptyLearner.Id, dashboard.Context.LearnerUserId);
        Assert.Equal(emptyLearner.Email, dashboard.Context.Email);
        Assert.Empty(dashboard.Courses);
        Assert.Equal(0, dashboard.Context.EnrolledCourseCount);
        Assert.Equal(0, dashboard.Context.CompletedCourseCount);
    }

    [Fact]
    public async Task GetDashboardAsync_AdminDefaultsToFirstLearnerWithEnrollments()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        await fixture.UserAccountService.CreateAsync("aaa-empty@lms.com", "A Empty Learner", "Learner123!", "Learner");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Default Selection Course");

        var dashboard = await fixture.LearnerDashboardService.GetDashboardAsync(admin.Id, isAdminViewer: true, requestedLearnerId: null);

        Assert.Equal(learner.Id, dashboard.Context.LearnerUserId);
        Assert.Single(dashboard.Courses);
    }

    [Fact]
    public async Task GetDashboardAsync_DoesNotReturnDefaultCertificateId_WhenNoCertificateExists()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "No Certificate Course");

        var dashboard = await fixture.LearnerDashboardService.GetDashboardAsync(learner.Id, isAdminViewer: false, requestedLearnerId: null);

        var course = Assert.Single(dashboard.Courses);
        Assert.Null(course.CertificateId);
    }

    [Fact]
    public async Task GetDashboardAsync_AdminHonorsRequestedLearner_WhenMultipleLearnersHaveEnrollments()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var primaryLearner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var secondaryLearner = await fixture.UserAccountService.CreateAsync("second-learner@lms.com", "Second Learner", "Learner123!", "Learner");

        await fixture.CreateCourseGraphWithEnrollmentAsync(primaryLearner.Id, "Primary Learner Course");
        await fixture.CreateCourseGraphWithEnrollmentAsync(secondaryLearner.Id, "Second Learner Course");

        var dashboard = await fixture.LearnerDashboardService.GetDashboardAsync(admin.Id, isAdminViewer: true, secondaryLearner.Id);

        Assert.Equal(secondaryLearner.Id, dashboard.Context.LearnerUserId);
        var selectedCourse = Assert.Single(dashboard.Courses);
        Assert.Equal("Second Learner Course", selectedCourse.Title);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IUserAccountService UserAccountService { get; }
        public IEnrollmentService EnrollmentService { get; }
        public IAssessmentService AssessmentService { get; }
        public ILearnerDashboardService LearnerDashboardService { get; }

        private TestFixture(
            SqliteConnection connection,
            ApplicationDbContext dbContext,
            IUserAccountService userAccountService,
            IEnrollmentService enrollmentService,
            IAssessmentService assessmentService,
            ILearnerDashboardService learnerDashboardService)
        {
            _connection = connection;
            DbContext = dbContext;
            UserAccountService = userAccountService;
            EnrollmentService = enrollmentService;
            AssessmentService = assessmentService;
            LearnerDashboardService = learnerDashboardService;
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
            var enrollmentService = new EnrollmentService(dbContext, auditLogService, assessmentService);
            var learnerDashboardService = new LearnerDashboardService(enrollmentService, assessmentService, userAccountService);

            return new TestFixture(connection, dbContext, userAccountService, enrollmentService, assessmentService, learnerDashboardService);
        }

        public async Task<(Guid CourseId, List<Guid> RequiredLessonIds)> CreateCourseGraphWithEnrollmentAsync(Guid learnerId, string title)
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
            DbContext.Enrollments.Add(new Enrollment
            {
                UserAccountId = learnerId,
                Course = course,
                EnrollmentSource = "LearnerPurchase",
                ConsentStatus = "NotRequired",
                ProgressPercent = 0,
                Completed = false,
                EnrolledAt = DateTime.UtcNow
            });

            await DbContext.SaveChangesAsync();
            await AssessmentService.EnsureDefaultAssessmentForCourseAsync(course.Id);

            return (course.Id, [lessonA.Id, lessonB.Id]);
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