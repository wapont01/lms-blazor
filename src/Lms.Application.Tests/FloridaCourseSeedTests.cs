using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public sealed class FloridaCourseSeedTests
{
    private static readonly Guid FloridaCourseId = Guid.Parse("f1a6e7d8-c9b0-4a12-8e34-56789abcdef0");

    [Fact]
    public async Task SeedAsync_CreatesFloridaCourseAndAuthoredFinalAssessment_Idempotently()
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

        var course = await dbContext.Courses
            .Include(existing => existing.Modules)
                .ThenInclude(module => module.Lessons)
            .SingleAsync(existing => existing.Id == FloridaCourseId);
        var assessment = await dbContext.CourseAssessments
            .Include(existing => existing.Questions)
            .SingleAsync(existing => existing.CourseId == FloridaCourseId && existing.IsRequired);

        Assert.Equal("Florida", course.State);
        Assert.Equal("DistanceEducation", course.DeliveryMethod);
        Assert.Equal("william.aponte@wlr.com", await dbContext.UserAccounts
            .Where(user => user.Id == course.OwnerInstructorId)
            .Select(user => user.Email)
            .SingleAsync());
        Assert.Equal(6, course.Modules
            .Where(module => !string.Equals(module.Title, "Getting Started", StringComparison.OrdinalIgnoreCase))
            .SelectMany(module => module.Lessons)
            .Count());
        Assert.Equal(7, course.Modules.SelectMany(module => module.Lessons).Count());
        Assert.Equal("Florida Distance Learning Fundamentals Final Assessment", assessment.Title);
        Assert.Equal(75m, assessment.PassPercent);
        Assert.Equal(6, assessment.Questions.Count);
        Assert.Equal(1, await dbContext.Courses.CountAsync(existing => existing.Id == FloridaCourseId));
        Assert.Equal(1, await dbContext.CourseAssessments.CountAsync(existing => existing.CourseId == FloridaCourseId && existing.IsRequired));
        Assert.Equal("North Carolina", await dbContext.Courses.Where(existing => existing.Id == Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f")).Select(existing => existing.State).SingleAsync());
        Assert.Equal("Florida", await dbContext.Courses.Where(existing => existing.Id == Guid.Parse("c2a11379-8fb4-479a-9209-45441a56d812")).Select(existing => existing.State).SingleAsync());
        Assert.Equal("North Carolina", await dbContext.Courses.Where(existing => existing.Id == Guid.Parse("f5e5df5f-5b7a-4d8c-aafd-0cd1745311e4")).Select(existing => existing.State).SingleAsync());
        Assert.Equal("Florida", await dbContext.Courses.Where(existing => existing.Id == Guid.Parse("9f4f6d8c-2f7a-4b6c-9a53-5b7a2d4e1c90")).Select(existing => existing.State).SingleAsync());
        Assert.Equal("North Carolina", await dbContext.Courses.Where(existing => existing.Id == Guid.Parse("b9c7d0ef-3b89-4c1d-9e5e-6de7ce5585a1")).Select(existing => existing.State).SingleAsync());
        Assert.Equal("North Carolina", await dbContext.Courses.Where(existing => existing.Id == Guid.Parse("5bc4b74b-71ac-40fe-8bc9-1daf5a922782")).Select(existing => existing.State).SingleAsync());

        var floridaAssignments = await dbContext.BrokerLearnerAssignments
            .Join(dbContext.UserAccounts, assignment => assignment.BrokerUserId, user => user.Id, (assignment, user) => new { assignment, BrokerEmail = user.Email })
            .Join(dbContext.UserAccounts, row => row.assignment.LearnerUserId, user => user.Id, (row, user) => new { row.BrokerEmail, LearnerEmail = user.Email })
            .Where(row => row.BrokerEmail.EndsWith(".fl@wlr.com") && row.LearnerEmail.EndsWith(".fl@wlr.com"))
            .ToListAsync();

        Assert.Contains(floridaAssignments, row => row.BrokerEmail == "marcus.reed.fl@wlr.com" && row.LearnerEmail == "sofia.martinez.fl@wlr.com");
        Assert.Contains(floridaAssignments, row => row.BrokerEmail == "marcus.reed.fl@wlr.com" && row.LearnerEmail == "noah.williams.fl@wlr.com");
        Assert.Contains(floridaAssignments, row => row.BrokerEmail == "elena.rossi.fl@wlr.com" && row.LearnerEmail == "ava.johnson.fl@wlr.com");
    }
}
