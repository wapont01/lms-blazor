using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public sealed class FloridaCourseSeedTests
{
    private static readonly Guid FloridaCourseId = Guid.Parse("f1a6e7d8-c9b0-4a12-8e34-56789abcdef0");

    [Fact]
    public async Task SeedAsync_ImportsFloridaAndNorthCarolinaCoursesWithFinalExams_Idempotently()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        await new UserAccountService(dbContext, new AuditLogService(dbContext)).EnsureSeedUsersAsync();
        await CourseSeed.SeedAsync(dbContext);
        await CourseSeed.SeedEnrollmentsAsync(dbContext);
        await CourseSeed.SeedAsync(dbContext);
        await CourseSeed.SeedEnrollmentsAsync(dbContext);

        var courses = await dbContext.Courses.AsNoTracking().ToListAsync();
        Assert.True(courses.Count == 8, string.Join("; ", courses.Select(existing => $"{existing.Title} ({existing.State})")));
        var course = Assert.Single(courses, existing => existing.Id == FloridaCourseId);
        var exams = await dbContext.CourseExams
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .ToListAsync();
        var floridaExams = exams.Where(exam => exam.CourseId == FloridaCourseId).ToList();

        Assert.Equal(FloridaCourseId, course.Id);
        Assert.Equal("Florida Core Law", course.Title);
        Assert.Equal("Florida", course.State);
        Assert.Equal(CourseComplianceTypes.ContinuingEducation, course.ComplianceType);
        Assert.Equal(CourseIndustries.RealEstate, course.Industry);
        Assert.Equal(ContinuingEducationTypes.CoreLaw, course.ContinuingEducationType);
        Assert.Equal(3, course.DurationHours);
        Assert.Equal(3, course.CreditHours);
        Assert.Equal(30m, course.Price);
        Assert.Equal(180, course.RequiredInstructionalMinutes);
        Assert.False(course.IsPublished);
        Assert.Equal(CourseReviewStatuses.Draft, course.ReviewStatus);
        var floridaUnits = await dbContext.Units
            .Where(unit => unit.CourseId == FloridaCourseId)
            .Select(unit => unit.Title)
            .ToListAsync();
        Assert.True(floridaUnits.Count == 5, string.Join("; ", floridaUnits));
        Assert.Equal(12, exams.Count);
        Assert.Equal(5, floridaExams.Count);
        Assert.Equal(4, floridaExams.Count(exam => exam.UnitId.HasValue));
        Assert.Equal(15, Assert.Single(floridaExams, exam => exam.UnitId is null).Questions.Count);
        var ethicsCourse = Assert.Single(courses, existing => existing.Title == "Ethics at Work");
        Assert.Equal("Florida", ethicsCourse.State);
        Assert.Equal(CourseIndustries.RealEstate, ethicsCourse.Industry);
        Assert.Equal(3, ethicsCourse.DurationHours);
        Assert.Equal(3, ethicsCourse.CreditHours);
        Assert.Equal(30m, ethicsCourse.Price);
        Assert.Equal(180, ethicsCourse.RequiredInstructionalMinutes);
        var marketAnalysisCourse = Assert.Single(courses, existing =>
            existing.Title == "Preparing a Market Analysis - Best Practices"
            && existing.State == "North Carolina");
        Assert.Equal(4, marketAnalysisCourse.DurationHours);
        Assert.Equal(CourseIndustries.RealEstate, marketAnalysisCourse.Industry);
        Assert.Equal(4, marketAnalysisCourse.CreditHours);
        Assert.Equal(35m, marketAnalysisCourse.Price);
        Assert.Equal(240, marketAnalysisCourse.RequiredInstructionalMinutes);
        var ncCourse = Assert.Single(courses, existing =>
            existing.Title == "Document Diligence: Safeguarding Your Transactions"
            && existing.State == "North Carolina");
        Assert.Equal(CourseComplianceTypes.ContinuingEducation, ncCourse.ComplianceType);
        Assert.Equal(CourseIndustries.RealEstate, ncCourse.Industry);
        Assert.Equal(ContinuingEducationTypes.Elective, ncCourse.ContinuingEducationType);
        Assert.Equal(4, ncCourse.DurationHours);
        Assert.Equal(4, ncCourse.CreditHours);
        Assert.Equal(35m, ncCourse.Price);
        Assert.Equal(240, ncCourse.RequiredInstructionalMinutes);
        Assert.Equal(75, ncCourse.MinimumPassingPercent);
        Assert.Equal(3, await dbContext.Units.CountAsync(unit => unit.CourseId == ncCourse.Id));
        Assert.Equal(
            19,
            await dbContext.Lessons.CountAsync(lesson =>
                dbContext.Units.Any(unit => unit.Id == lesson.UnitId && unit.CourseId == ncCourse.Id)));
        var propertyManagementCourse = Assert.Single(courses, existing =>
            existing.Title == "Residential Property Management Essentials");
        Assert.Equal("Florida", propertyManagementCourse.State);
        Assert.Equal(4, propertyManagementCourse.DurationHours);
        Assert.Equal(CourseIndustries.RealEstate, propertyManagementCourse.Industry);
        Assert.Equal(4, propertyManagementCourse.CreditHours);
        Assert.Equal(35m, propertyManagementCourse.Price);
        Assert.Equal(240, propertyManagementCourse.RequiredInstructionalMinutes);
        Assert.Equal(6, await dbContext.Units.CountAsync(unit => unit.CourseId == propertyManagementCourse.Id));
        Assert.Equal(
            36,
            await dbContext.Lessons.CountAsync(lesson =>
                dbContext.Units.Any(unit => unit.Id == lesson.UnitId && unit.CourseId == propertyManagementCourse.Id)));
        var assistanceAnimalsCourse = Assert.Single(courses, existing =>
            existing.Title == "Assistance Animals And Fair Housing"
            && existing.State == "Florida");
        Assert.Equal(4, assistanceAnimalsCourse.DurationHours);
        Assert.Equal(CourseIndustries.RealEstate, assistanceAnimalsCourse.Industry);
        Assert.Equal(4, assistanceAnimalsCourse.CreditHours);
        Assert.Equal(35m, assistanceAnimalsCourse.Price);
        Assert.Equal(240, assistanceAnimalsCourse.RequiredInstructionalMinutes);
        Assert.Equal(4, await dbContext.Units.CountAsync(unit => unit.CourseId == assistanceAnimalsCourse.Id));
        Assert.Equal(
            23,
            await dbContext.Lessons.CountAsync(lesson =>
                dbContext.Units.Any(unit => unit.Id == lesson.UnitId && unit.CourseId == assistanceAnimalsCourse.Id)));
        var brokerageCourse = Assert.Single(courses, existing =>
            existing.Title == "FL 30-Hour Real Estate Brokerage"
            && existing.State == "Florida");
        Assert.Equal(CourseComplianceTypes.Postlicensing, brokerageCourse.ComplianceType);
        Assert.Equal(CourseIndustries.RealEstate, brokerageCourse.Industry);
        Assert.Equal(30, brokerageCourse.DurationHours);
        Assert.Equal(30, brokerageCourse.CreditHours);
        Assert.Equal(1800, brokerageCourse.RequiredInstructionalMinutes);
        Assert.Equal(75, brokerageCourse.MinimumPassingPercent);
        Assert.True(brokerageCourse.RequiresProctoredExam);
        Assert.Null(RegulatoryCoursePolicy.ValidateConfiguration(brokerageCourse));
        Assert.Equal(180, brokerageCourse.CompletionWindowDays);
        Assert.Equal(4, await dbContext.Units.CountAsync(unit => unit.CourseId == brokerageCourse.Id));
        Assert.Equal(
            18,
            await dbContext.Lessons.CountAsync(lesson =>
                dbContext.Units.Any(unit => unit.Id == lesson.UnitId && unit.CourseId == brokerageCourse.Id)));
        var brokerageLessons = await dbContext.Lessons
            .Where(lesson => dbContext.Units.Any(unit =>
                unit.Id == lesson.UnitId && unit.CourseId == brokerageCourse.Id))
            .ToListAsync();
        Assert.All(brokerageLessons, lesson => Assert.False(string.IsNullOrWhiteSpace(lesson.TextContent)));
        var practiceFinalLesson = Assert.Single(brokerageLessons, lesson => lesson.Title == "Practice Final Exam");
        Assert.Contains("Question 1", practiceFinalLesson.TextContent);
        Assert.Equal(brokerageCourse.Id, (await dbContext.CourseExams.SingleAsync(exam =>
            exam.CourseId == brokerageCourse.Id && exam.UnitId == null)).CourseId);
        var financeCourse = Assert.Single(courses, existing =>
            existing.Title == "Florida Post-Licensing: Real Estate Finance"
            && existing.State == "Florida");
        Assert.Equal(CourseComplianceTypes.Postlicensing, financeCourse.ComplianceType);
        Assert.Equal(CourseIndustries.Mortgage, financeCourse.Industry);
        Assert.Equal(30, financeCourse.DurationHours);
        Assert.Equal(30, financeCourse.CreditHours);
        Assert.Equal(1800, financeCourse.RequiredInstructionalMinutes);
        Assert.Equal(75, financeCourse.MinimumPassingPercent);
        Assert.True(financeCourse.RequiresProctoredExam);
        Assert.Null(RegulatoryCoursePolicy.ValidateConfiguration(financeCourse));
        Assert.Equal(180, financeCourse.CompletionWindowDays);
        Assert.Equal(4, await dbContext.Units.CountAsync(unit => unit.CourseId == financeCourse.Id));
        Assert.Equal(
            243,
            await dbContext.Lessons.CountAsync(lesson =>
                dbContext.Units.Any(unit => unit.Id == lesson.UnitId && unit.CourseId == financeCourse.Id)));
        var financeLessons = await dbContext.Lessons
            .Where(lesson => dbContext.Units.Any(unit =>
                unit.Id == lesson.UnitId && unit.CourseId == financeCourse.Id))
            .ToListAsync();
        Assert.All(financeLessons, lesson => Assert.False(string.IsNullOrWhiteSpace(lesson.TextContent)));
        Assert.Contains(financeLessons, lesson => lesson.Title == "Reserve Requirements");
        Assert.Contains(financeLessons, lesson => lesson.Title == "Practice Exam Review");
        Assert.Contains(financeLessons, lesson => lesson.Title == "Mid-point Exam Review");
        foreach (var importedCourse in courses.Where(existing => existing.Id != FloridaCourseId))
        {
            Assert.False(importedCourse.IsPublished);
            Assert.Equal(CourseReviewStatuses.Draft, importedCourse.ReviewStatus);
            Assert.NotEmpty(await dbContext.Units.Where(unit => unit.CourseId == importedCourse.Id).ToListAsync());
            var finalExam = Assert.Single(exams, exam =>
                exam.CourseId == importedCourse.Id && exam.UnitId is null);
            var expectedQuestionCount = importedCourse.Id == ncCourse.Id
                ? 10
                : importedCourse.Id == brokerageCourse.Id
                    ? 50
                    : importedCourse.Id == financeCourse.Id
                        ? 50
                    : importedCourse.Id == propertyManagementCourse.Id || importedCourse.Id == assistanceAnimalsCourse.Id
                    ? 20
                    : 15;
            Assert.Equal(
                expectedQuestionCount,
                finalExam.Questions.Count);
            Assert.Equal(
                importedCourse.Id == ncCourse.Id || importedCourse.Id == brokerageCourse.Id || importedCourse.Id == financeCourse.Id ? 75m : 80m,
                finalExam.PassPercent);
            Assert.True(finalExam.Questions.Select(question => question.CorrectOption).Distinct().Count() >= 3);
            if (importedCourse.Id == ncCourse.Id)
            {
                Assert.Equal(
                    new[] { "B", "C", "D", "A", "C", "B", "A", "C", "D", "C" },
                    finalExam.Questions.OrderBy(question => question.OrderIndex).Select(question => question.CorrectOption));
            }
            if (importedCourse.Id == brokerageCourse.Id)
            {
                var orderedQuestions = finalExam.Questions.OrderBy(question => question.OrderIndex).ToList();
                Assert.Equal("C", orderedQuestions[0].CorrectOption);
                Assert.Equal("Risk is an inherent part of real estate activities.", orderedQuestions[0].OptionC);
                Assert.Equal("Tenant improvement allowances", orderedQuestions[49].OptionD);
            }
        }

        Assert.Empty(await dbContext.Enrollments.ToListAsync());
    }
}
