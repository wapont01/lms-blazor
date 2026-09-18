using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class CourseServiceImportTests
{
    [Fact]
    public async Task ImportCourseStructureAsync_CreatesModulesAndLessonsFromMarkers()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "Imported Course",
            Level = "Beginner",
            Description = "Imported from a text file",
            Price = 0
        };

        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        const string importText = """
            [module] Welcome
            [lesson] Intro Lesson | duration=5 | required=true
            This is the intro lesson.

            [lesson] Practice Lesson | duration=8 | required=false
            This is the practice lesson.

            [module] Advanced
            [lesson] Final Lesson | duration=12 | required=true
            This is the final lesson.
            """;

        await fixture.CourseService.ImportCourseStructureAsync(course.Id, importText);

        var refreshed = await fixture.DbContext.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
            .SingleAsync(course => course.Id == course.Id);

        Assert.Equal(2, refreshed.Modules.Count);

        var welcomeModule = Assert.Single(refreshed.Modules.Where(module => module.Title == "Welcome"));
        Assert.Equal(2, welcomeModule.Lessons.Count);

        var introLesson = welcomeModule.Lessons.Single(lesson => lesson.Title == "Intro Lesson");
        Assert.Equal(5, introLesson.DurationMinutes);
        Assert.True(introLesson.IsRequired);
        Assert.Equal("This is the intro lesson.", introLesson.TextContent);

        var practiceLesson = welcomeModule.Lessons.Single(lesson => lesson.Title == "Practice Lesson");
        Assert.Equal(8, practiceLesson.DurationMinutes);
        Assert.False(practiceLesson.IsRequired);
        Assert.Equal("This is the practice lesson.", practiceLesson.TextContent);

        var advancedModule = Assert.Single(refreshed.Modules.Where(module => module.Title == "Advanced"));
        var finalLesson = Assert.Single(advancedModule.Lessons);
        Assert.Equal("Final Lesson", finalLesson.Title);
        Assert.Equal(12, finalLesson.DurationMinutes);
        Assert.True(finalLesson.IsRequired);
        Assert.Equal("This is the final lesson.", finalLesson.TextContent);
    }

    [Fact]
    public async Task GetCoursesForInstructorAsync_LoadsModulesAndLessonsForOwnedCourses()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var instructor = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "instructor@lms.com",
            DisplayName = "Instructor",
            Role = "Instructor",
            IsActive = true
        };

        var course = new Course
        {
            Id = Guid.NewGuid(),
            Title = "Owned Course",
            Level = "Beginner",
            Description = "Owned by the instructor",
            Price = 0,
            OwnerInstructorId = instructor.Id
        };

        var module = new Module
        {
            Id = Guid.NewGuid(),
            CourseId = course.Id,
            Title = "Module 1",
            OrderIndex = 1
        };

        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            Title = "Lesson 1",
            TextContent = "Lesson content",
            DurationMinutes = 5,
            OrderIndex = 1,
            IsRequired = true
        };

        fixture.DbContext.UserAccounts.Add(instructor);
        fixture.DbContext.Courses.Add(course);
        fixture.DbContext.Modules.Add(module);
        fixture.DbContext.Lessons.Add(lesson);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CourseService.GetCoursesForInstructorAsync(instructor.Id);

        var ownedCourse = Assert.Single(result);
        var loadedModule = Assert.Single(ownedCourse.Modules);
        Assert.Equal("Module 1", loadedModule.Title);

        var loadedLesson = Assert.Single(loadedModule.Lessons);
        Assert.Equal("Lesson 1", loadedLesson.Title);
    }

    [Fact]
    public async Task ImportCourseStructureAsync_UsesDefaultsWhenLessonMetadataIsOmitted()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "Imported Course",
            Level = "Beginner",
            Description = "Imported without metadata",
            Price = 0
        };

        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        const string importText = """
            [module] Intro
            [lesson] Welcome
            This is the welcome lesson.

            [module] Advanced
            [lesson] Next Steps
            This is the next step lesson.
            """;

        await fixture.CourseService.ImportCourseStructureAsync(course.Id, importText);

        var refreshed = await fixture.DbContext.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
            .SingleAsync(course => course.Id == course.Id);

        var introModule = Assert.Single(refreshed.Modules.Where(module => module.Title == "Intro"));
        var welcomeLesson = Assert.Single(introModule.Lessons);

        Assert.Equal("Welcome", welcomeLesson.Title);
        Assert.Equal(0, welcomeLesson.DurationMinutes);
        Assert.True(welcomeLesson.IsRequired);
        Assert.Equal("This is the welcome lesson.", welcomeLesson.TextContent);
    }

    [Fact]
    public async Task ImportCourseStructureAsync_ImportsAssessmentsAndCheckpointsWithCorrectAnswers()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "Imported Assessment Course",
            Level = "Beginner",
            Description = "Imported with assessments and checkpoints",
            Price = 0
        };

        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        const string importText = """
            [module] Intro
            [lesson] Welcome
            Welcome lesson content.

            [checkpoint] Intro
            title=Choose the lessons that belong to this module
            prompt=Select every correct lesson.
            description=This checkpoint verifies the module content.
            [option] Welcome | correct=true
            [option] Extra lesson | correct=false

            [assessment]
            title=Imported Final Assessment
            passpercent=90
            [question] What should you review first? | optionA=The syllabus | optionB=The office color | optionC=The parking map | optionD=The snack menu | correct=A | feedback=Start with the plan.
            [question] Which response is best? | optionA=Ignore it | optionB=Document it | optionC=Wait | optionD=Escalate later | correct=B | feedback=Document the update.
            """;

        await fixture.CourseService.ImportCourseStructureAsync(course.Id, importText);

        var checkpoints = await fixture.DbContext.CourseCheckpointDefinitions
            .AsNoTracking()
            .Include(definition => definition.Options)
            .Where(definition => definition.CourseId == course.Id)
            .ToListAsync();

        var moduleCheckpoint = Assert.Single(checkpoints.Where(definition => !definition.IsFinal));
        Assert.Equal("Choose the lessons that belong to this module", moduleCheckpoint.Title);
        Assert.Contains(moduleCheckpoint.Options, option => option.IsCorrect && option.Label == "Welcome");
        Assert.Contains(moduleCheckpoint.Options, option => !option.IsCorrect && option.Label == "Extra lesson");

        var assessment = await fixture.DbContext.CourseAssessments
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .SingleAsync(existing => existing.CourseId == course.Id && existing.IsRequired);

        Assert.Equal("Imported Final Assessment", assessment.Title);
        Assert.Equal(90m, assessment.PassPercent);
        Assert.Equal(2, assessment.Questions.Count);
        Assert.Equal("A", assessment.Questions.OrderBy(question => question.OrderIndex).First().CorrectOption);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public CourseService CourseService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, CourseService courseService)
        {
            _connection = connection;
            DbContext = dbContext;
            CourseService = courseService;
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

            var courseService = new CourseService(dbContext);
            return new TestFixture(connection, dbContext, courseService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
