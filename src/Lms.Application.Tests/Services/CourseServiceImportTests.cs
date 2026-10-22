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
    public async Task ImportCourseStructureAsync_CreatesUnitsAndLessonsFromMarkers()
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
            [unit] Welcome
            [lesson] Intro Lesson | duration=5 | required=true
            This is the intro lesson.

            [lesson] Practice Lesson | duration=8 | required=false
            This is the practice lesson.

            [unit] Advanced
            [lesson] Final Lesson | duration=12 | required=true
            This is the final lesson.
            """;

        await fixture.CourseService.ImportCourseStructureAsync(course.Id, importText);

        var refreshed = await fixture.DbContext.Courses
            .AsNoTracking()
            .Include(course => course.Units)
                .ThenInclude(unit => unit.Lessons)
            .SingleAsync(course => course.Id == course.Id);

        Assert.Equal(2, refreshed.Units.Count);

        var welcomeUnit = Assert.Single(refreshed.Units.Where(unit => unit.Title == "Welcome"));
        Assert.Equal(2, welcomeUnit.Lessons.Count);

        var introLesson = welcomeUnit.Lessons.Single(lesson => lesson.Title == "Intro Lesson");
        Assert.Equal(5, introLesson.DurationMinutes);
        Assert.True(introLesson.IsRequired);
        Assert.Equal("This is the intro lesson.", introLesson.TextContent);

        var practiceLesson = welcomeUnit.Lessons.Single(lesson => lesson.Title == "Practice Lesson");
        Assert.Equal(8, practiceLesson.DurationMinutes);
        Assert.False(practiceLesson.IsRequired);
        Assert.Equal("This is the practice lesson.", practiceLesson.TextContent);

        var advancedUnit = Assert.Single(refreshed.Units.Where(unit => unit.Title == "Advanced"));
        var finalLesson = Assert.Single(advancedUnit.Lessons);
        Assert.Equal("Final Lesson", finalLesson.Title);
        Assert.Equal(12, finalLesson.DurationMinutes);
        Assert.True(finalLesson.IsRequired);
        Assert.Equal("This is the final lesson.", finalLesson.TextContent);
    }

    [Fact]
    public async Task GetCoursesForInstructorAsync_LoadsUnitsAndLessonsForOwnedCourses()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var instructor = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "instructor@wlr.com",
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

        var unit = new Unit
        {
            Id = Guid.NewGuid(),
            CourseId = course.Id,
            Title = "Unit 1",
            OrderIndex = 1
        };

        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            UnitId = unit.Id,
            Title = "Lesson 1",
            TextContent = "Lesson content",
            DurationMinutes = 5,
            OrderIndex = 1,
            IsRequired = true
        };

        fixture.DbContext.UserAccounts.Add(instructor);
        fixture.DbContext.Courses.Add(course);
        fixture.DbContext.Units.Add(unit);
        fixture.DbContext.Lessons.Add(lesson);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CourseService.GetCoursesForInstructorAsync(instructor.Id);

        var ownedCourse = Assert.Single(result);
        var loadedUnit = Assert.Single(ownedCourse.Units);
        Assert.Equal("Unit 1", loadedUnit.Title);

        var loadedLesson = Assert.Single(loadedUnit.Lessons);
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
            [unit] Intro
            [lesson] Welcome
            This is the welcome lesson.

            [unit] Advanced
            [lesson] Next Steps
            This is the next step lesson.
            """;

        await fixture.CourseService.ImportCourseStructureAsync(course.Id, importText);

        var refreshed = await fixture.DbContext.Courses
            .AsNoTracking()
            .Include(course => course.Units)
                .ThenInclude(unit => unit.Lessons)
            .SingleAsync(course => course.Id == course.Id);

        var introUnit = Assert.Single(refreshed.Units.Where(unit => unit.Title == "Intro"));
        var welcomeLesson = Assert.Single(introUnit.Lessons);

        Assert.Equal("Welcome", welcomeLesson.Title);
        Assert.Equal(0, welcomeLesson.DurationMinutes);
        Assert.True(welcomeLesson.IsRequired);
        Assert.Equal("This is the welcome lesson.", welcomeLesson.TextContent);
    }

    [Fact]
    public async Task ImportCourseStructureAsync_ImportsExamsAndUnitExamsWithCorrectAnswers()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "Imported Exam Course",
            Level = "Beginner",
            Description = "Imported with exams and unit exams",
            Price = 0
        };

        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        const string importText = """
            [unit] Intro
            [lesson] Welcome
            Welcome lesson content.

            [unit exam] Intro
            title=Choose the lessons that belong to this unit
            prompt=Select every correct lesson.
            description=This unit exam verifies the unit content.
            [option] Welcome | correct=true
            [option] Extra lesson | correct=false

            [exam]
            title=Imported Final Exam
            passpercent=90
            [question] What should you review first? | optionA=The syllabus | optionB=The office color | optionC=The parking map | optionD=The snack menu | correct=A | feedback=Start with the plan.
            [question] Which response is best? | optionA=Ignore it | optionB=Document it | optionC=Wait | optionD=Escalate later | correct=B | feedback=Document the update.
            """;

        await fixture.CourseService.ImportCourseStructureAsync(course.Id, importText);

        var unitExams = await fixture.DbContext.CourseUnitExamDefinitions
            .AsNoTracking()
            .Include(definition => definition.Options)
            .Where(definition => definition.CourseId == course.Id)
            .ToListAsync();

        var unitExam = Assert.Single(unitExams.Where(definition => !definition.IsFinal));
        Assert.Equal("Choose the lessons that belong to this unit", unitExam.Title);
        Assert.Contains(unitExam.Options, option => option.IsCorrect && option.Label == "Welcome");
        Assert.Contains(unitExam.Options, option => !option.IsCorrect && option.Label == "Extra lesson");

        var exam = await fixture.DbContext.CourseExams
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .SingleAsync(existing => existing.CourseId == course.Id && existing.IsRequired);

        Assert.Equal("Imported Final Exam", exam.Title);
        Assert.Equal(90m, exam.PassPercent);
        Assert.Equal(2, exam.Questions.Count);
        Assert.Equal("A", exam.Questions.OrderBy(question => question.OrderIndex).First().CorrectOption);
    }

    [Fact]
    public async Task ImportCourseStructureAsync_ImportsMultipleUnitExams()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "Unit Exam Course",
            Level = "Beginner",
            Description = "Imports unit and final exams",
            Price = 0
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        const string importText = """
            [unit] Unit 2
            [lesson] Licensing
            Licensing lesson content.
            [exam] Unit 2 Exam | unit=Unit 2
            passpercent=80
            [question] Which rule applies? | optionA=Rule A | optionB=Rule B | optionC=Rule C | optionD=Rule D | correct=B

            [unit] Unit 3
            [lesson] Escrow
            Escrow lesson content.
            [exam] Unit 3 Exam | unit=Unit 3
            [question] Which record is required? | optionA=Record A | optionB=Record B | optionC=Record C | optionD=Record D | correct=C

            [exam]
            title=Final Exam
            [question] What is the final answer? | optionA=A | optionB=B | optionC=C | optionD=D | correct=D
            """;

        await fixture.CourseService.ImportCourseStructureAsync(course.Id, importText);

        var exams = await fixture.DbContext.CourseExams
            .AsNoTracking()
            .Include(exam => exam.Questions)
            .Where(exam => exam.CourseId == course.Id)
            .ToListAsync();

        Assert.Equal(3, exams.Count);
        var unit2 = Assert.Single(exams.Where(exam => exam.Title == "Unit 2 Exam"));
        var unit3 = Assert.Single(exams.Where(exam => exam.Title == "Unit 3 Exam"));
        var final = Assert.Single(exams.Where(exam => exam.Title == "Final Exam"));
        Assert.Equal("Unit 2", await fixture.DbContext.Units.Where(unit => unit.Id == unit2.UnitId).Select(unit => unit.Title).SingleAsync());
        Assert.Equal("Unit 3", await fixture.DbContext.Units.Where(unit => unit.Id == unit3.UnitId).Select(unit => unit.Title).SingleAsync());
        Assert.Null(final.UnitId);
        Assert.Equal("B", Assert.Single(unit2.Questions).CorrectOption);
        Assert.Equal("C", Assert.Single(unit3.Questions).CorrectOption);
        Assert.Equal("D", Assert.Single(final.Questions).CorrectOption);
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
