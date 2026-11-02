using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public sealed class DevelopmentStateCourseSeedTests
{
    [Fact]
    public async Task Seed_CopiesStructureAcrossFiftyStatesWithoutProgressAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var source = new Course
        {
            Title = "Sample source", State = "Florida", Slug = "source", IsPublished = true,
            IsArchived = true, Price = 25, CommissionCourseNumber = "FL-123",
            ReviewStatus = CourseReviewStatuses.Approved,
            Units = [new Unit { Title = "Unit", Lessons = [new Lesson { Title = "Lesson", TextContent = "Content", DurationMinutes = 5 }] }]
        };
        var unit = source.Units.Single();
        source.Exams.Add(new CourseExam
        {
            UnitId = unit.Id, Title = "Exam", ContentVersion = 3,
            Questions = [new ExamQuestion { Prompt = "Question", OptionA = "A", OptionB = "B", OptionC = "C", OptionD = "D", CorrectOption = "B" }]
        });
        db.Courses.Add(source);
        db.CourseUnitExamDefinitions.Add(new CourseUnitExamDefinition
        {
            CourseId = source.Id, UnitId = unit.Id, LessonId = unit.Lessons.Single().Id,
            Key = "gate", Prompt = "Gate", Options = [new CourseUnitExamOption { Key = "yes", Label = "Yes", IsCorrect = true }]
        });
        await db.SaveChangesAsync();

        Assert.Equal(49, await DevelopmentStateCourseSeed.SeedAsync(db));
        Assert.Equal(0, await DevelopmentStateCourseSeed.SeedAsync(db));
        var copies = await db.Courses.AsNoTracking().Where(c => c.Id != source.Id)
            .Include(c => c.Units).ThenInclude(u => u.Lessons)
            .Include(c => c.Exams).ThenInclude(e => e.Questions).AsSplitQuery().ToListAsync();
        Assert.Equal(50, copies.Select(c => c.State).Append(source.State).Distinct().Count());
        Assert.All(copies, copy =>
        {
            Assert.True(copy.IsDevelopmentSample);
            Assert.False(copy.IsPublished);
            Assert.False(copy.IsArchived);
            Assert.Equal(CourseReviewStatuses.Draft, copy.ReviewStatus);
            Assert.Null(copy.CommissionCourseNumber);
            Assert.Equal(0, copy.Price);
            var copiedUnit = Assert.Single(copy.Units);
            Assert.NotEqual(unit.Id, copiedUnit.Id);
            Assert.Equal("Content", Assert.Single(copiedUnit.Lessons).TextContent);
            var exam = Assert.Single(copy.Exams);
            Assert.Equal(copiedUnit.Id, exam.UnitId);
            Assert.Equal(3, exam.ContentVersion);
            Assert.Equal("B", Assert.Single(exam.Questions).CorrectOption);
        });
        Assert.Equal(50, await db.CourseUnitExamDefinitions.CountAsync());
        Assert.Empty(await db.ExamAttempts.ToListAsync());
        Assert.Empty(await db.Enrollments.ToListAsync());
        Assert.Empty(await db.CompletionCertificates.ToListAsync());
        var original = await db.Courses.AsNoTracking().SingleAsync(c => c.Id == source.Id);
        Assert.True(original.IsArchived);
        Assert.Equal("FL-123", original.CommissionCourseNumber);
        Assert.Empty(await new CourseService(db).GetPublishedCoursesAsync());
        Assert.Equal(49, (await new CourseService(db, new DevelopmentCatalogOptions { Enabled = true }).GetPublishedCoursesAsync()).Count);
        Assert.Null(await new CourseService(db).GetByIdAsync(copies[0].Id));
        Assert.False(await new CourseService(db).ApproveCourseAsync(copies[0].Id, Guid.NewGuid()));
    }
}
