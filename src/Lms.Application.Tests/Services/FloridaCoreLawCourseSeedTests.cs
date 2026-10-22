using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class FloridaCoreLawCourseSeedTests
{
    [Fact]
    public async Task EnsureAsync_RemovesLegacyDemoEnrollmentsOnce_AndPreservesLaterRealEnrollments()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var courseId = Guid.NewGuid();
        var learner = new UserAccount
        {
            Email = "demo-learner@example.test",
            DisplayName = "Demo Learner",
            PasswordHash = "test-hash",
            Role = "Learner"
        };
        var oldCourse = new Course
        {
            Id = courseId,
            Title = "Florida Mock Course",
            Slug = "florida-mock-course",
            Level = "Beginner",
            Description = "Legacy demo course",
            Price = 0m
        };
        var oldEnrollment = new Enrollment
        {
            UserAccount = learner,
            CourseId = courseId,
            ProgressPercent = 100m,
            Completed = true,
            CompletedAtUtc = DateTime.UtcNow
        };
        var oldCertificate = new CompletionCertificate
        {
            CertificateNumber = "DEMO-FL-001",
            VerificationCode = "demo-verification-code",
            UserAccount = learner,
            CourseId = courseId,
            Enrollment = oldEnrollment,
            CreditHours = 3,
            IsRevoked = true
        };
        dbContext.Courses.Add(oldCourse);
        dbContext.Enrollments.Add(oldEnrollment);
        dbContext.CompletionCertificates.Add(oldCertificate);
        await dbContext.SaveChangesAsync();

        await FloridaCoreLawCourseSeed.EnsureAsync(dbContext, courseId, FindWorkspaceRoot());

        Assert.Empty(await dbContext.Enrollments.AsNoTracking().Where(enrollment => enrollment.CourseId == courseId).ToListAsync());
        Assert.Empty(await dbContext.CompletionCertificates.AsNoTracking().Where(certificate => certificate.CourseId == courseId).ToListAsync());
        Assert.Equal(1, await dbContext.AuditLogs.CountAsync(log => log.TargetId == courseId && log.Action == "course.legacy-demo-enrollments.removed"));

        var realEnrollment = new Enrollment
        {
            UserAccountId = learner.Id,
            CourseId = courseId,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            ProgressPercent = 5m
        };
        dbContext.Enrollments.Add(realEnrollment);
        await dbContext.SaveChangesAsync();

        await FloridaCoreLawCourseSeed.EnsureAsync(dbContext, courseId, FindWorkspaceRoot());

        var retainedEnrollment = await dbContext.Enrollments.AsNoTracking().SingleAsync(enrollment => enrollment.Id == realEnrollment.Id);
        Assert.Equal(5m, retainedEnrollment.ProgressPercent);
        Assert.Equal(1, await dbContext.AuditLogs.CountAsync(log => log.TargetId == courseId && log.Action == "course.legacy-demo-enrollments.removed"));
    }

    [Fact]
    public async Task EnsureAsync_ImportsSourceCourseAsDraft_AndIsIdempotent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var courseId = Guid.NewGuid();
        var workspaceRoot = FindWorkspaceRoot();

        await FloridaCoreLawCourseSeed.EnsureAsync(dbContext, courseId, workspaceRoot);

        var course = await dbContext.Courses
            .AsNoTracking()
            .Include(existing => existing.Units)
                .ThenInclude(unit => unit.Lessons)
            .SingleAsync(existing => existing.Id == courseId);
        var exams = await dbContext.CourseExams
            .AsNoTracking()
            .Include(exam => exam.Questions)
            .Where(exam => exam.CourseId == courseId)
            .ToListAsync();

        Assert.Equal("Florida Core Law", course.Title);
        Assert.Equal("Florida", course.State);
        Assert.Equal(CourseComplianceTypes.ContinuingEducation, course.ComplianceType);
        Assert.Equal(ContinuingEducationTypes.CoreLaw, course.ContinuingEducationType);
        Assert.Equal(3, course.DurationHours);
        Assert.Equal(3, course.CreditHours);
        Assert.Equal(180, course.RequiredInstructionalMinutes);
        Assert.False(course.IsPublished);
        Assert.Equal(CourseReviewStatuses.Draft, course.ReviewStatus);
        Assert.Equal(5, course.Units.Count);
        Assert.Contains(course.Units, unit => unit.Title == "Unit 5: Laws Affecting Real Estate");
        Assert.All(course.Units.SelectMany(unit => unit.Lessons), lesson => Assert.Equal(0, lesson.DurationMinutes));
        Assert.Equal(5, exams.Count);
        Assert.Equal(4, exams.Count(exam => exam.UnitId.HasValue));
        Assert.Single(exams.Where(exam => exam.UnitId is null));
        Assert.All(exams, exam => Assert.Equal(80m, exam.PassPercent));

        await FloridaCoreLawCourseSeed.EnsureAsync(dbContext, courseId, workspaceRoot);

        Assert.Equal(5, await dbContext.Units.CountAsync(unit => unit.CourseId == courseId));
        Assert.Equal(5, await dbContext.CourseExams.CountAsync(exam => exam.CourseId == courseId));
    }

    [Fact]
    public void ParseCoursePrint_ExtractsFiveUnitsAndCourseText()
    {
        var coursePrintPath = Path.Combine(
            FindWorkspaceRoot(),
            "docs",
            "FLORIDA",
            "COURSES",
            "CE",
            "Florida Core Law",
            "txts",
            "Course Print - Florida Core Law.txt");
        var coursePrint = File.ReadAllText(coursePrintPath);

        var units = FloridaCoreLawCourseSeed.ParseCoursePrint(coursePrint);

        Assert.Equal(5, units.Count);
        Assert.Equal("Unit 1: Course Introduction: Florida Core Law", units[0].Title);
        Assert.Contains(units[1].Lessons, lesson => lesson.Title == "Requirements of Licensing in Florida");
        Assert.Contains(units[4].Lessons, lesson => lesson.Title == "Updates to Homestead Law in Florida");
        Assert.Contains(units.SelectMany(unit => unit.Lessons), lesson => lesson.TextContent.Contains("Three hours of Florida core law", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "FLORIDA")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the workspace docs folder.");
    }
}