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
    public void Course_UsesUnitTerminologyForPrelicensingAndModuleTerminologyWhenUnspecified()
    {
        var generalCourse = new Course
        {
            Title = "General Course",
            Level = "Beginner",
            Price = 0,
            ComplianceType = CourseComplianceTypes.Unspecified
        };

        var prelicensingCourse = new Course
        {
            Title = "NC Prelicensing Course",
            Level = "Beginner",
            Price = 0,
            ComplianceType = CourseComplianceTypes.Prelicensing,
            RequiredInstructionalMinutes = 4500,
            MinimumPassingPercent = 75,
            MinimumAttendancePercent = 80
        };

        Assert.False(generalCourse.UsesUnitBasedStructure);
        Assert.Equal("Module", generalCourse.SectionLabel);

        Assert.True(prelicensingCourse.UsesUnitBasedStructure);
        Assert.Equal("Unit", prelicensingCourse.SectionLabel);
        Assert.Equal(4500, prelicensingCourse.RequiredInstructionalMinutes);
        Assert.Equal(75, prelicensingCourse.MinimumPassingPercent);
        Assert.Equal(80, prelicensingCourse.MinimumAttendancePercent);
    }

    [Fact]
    public void Course_ComplianceTypes_KeepContinuingEducationSeparateFromPrelicensing()
    {
        var ceCourse = new Course
        {
            Title = "Continuing Education Course",
            Level = "Advanced",
            Price = 0,
            ComplianceType = CourseComplianceTypes.ContinuingEducation
        };

        Assert.False(ceCourse.UsesUnitBasedStructure);
        Assert.Equal("Module", ceCourse.SectionLabel);
        Assert.Equal(CourseComplianceTypes.ContinuingEducation, ceCourse.ComplianceType);
    }

    [Fact]
    public async Task NormalizeLegacyComplianceTypesAsync_ConvertsStateRegulatedCoursesToSpecificCategories()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var courses = new[]
        {
            new Course { Title = "NC Pre-licensing", Slug = "legacy-pre", Level = "Beginner", Price = 0, ComplianceType = "StateRegulated", CommissionCourseNumber = "NC-PRE-75" },
            new Course { Title = "NC Post-licensing 301", Slug = "legacy-post", Level = "Beginner", Price = 0, ComplianceType = "StateRegulated", CommissionCourseNumber = "NC-POST-301" },
            new Course { Title = "NC Update", Slug = "legacy-ce", Level = "Beginner", Price = 0, ComplianceType = "StateRegulated", ContinuingEducationType = ContinuingEducationTypes.GeneralUpdate },
            new Course { Title = "Broker Operations", Slug = "legacy-general", Level = "Beginner", Price = 0, ComplianceType = "StateRegulated" }
        };
        fixture.DbContext.Courses.AddRange(courses);
        await fixture.DbContext.SaveChangesAsync();

        await CourseSeed.NormalizeLegacyComplianceTypesAsync(fixture.DbContext);

        Assert.Equal(CourseComplianceTypes.Prelicensing, courses[0].ComplianceType);
        Assert.Equal(CourseComplianceTypes.Postlicensing, courses[1].ComplianceType);
        Assert.Equal(CourseComplianceTypes.ContinuingEducation, courses[2].ComplianceType);
        Assert.Equal(CourseComplianceTypes.Unspecified, courses[3].ComplianceType);

        var legacyGeneral = new Course { Title = "Legacy General", Slug = "legacy-general-value", Level = "Beginner", Price = 0, ComplianceType = "General" };
        fixture.DbContext.Courses.Add(legacyGeneral);
        await fixture.DbContext.SaveChangesAsync();
        await CourseSeed.NormalizeLegacyComplianceTypesAsync(fixture.DbContext);
        Assert.Equal(CourseComplianceTypes.Unspecified, legacyGeneral.ComplianceType);
    }

    [Fact]
    public async Task SeedAsync_SeedsNCPrelicensingCourse_WithRegulatedMetadataAndSyllabusUnits()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await CourseSeed.SeedAsync(fixture.DbContext);

        var course = await fixture.DbContext.Courses
            .Include(course => course.Modules)
            .SingleAsync(course => course.Title == "NC Real Estate 75-Hour Pre-Licensing Broker");

        Assert.Equal(CourseComplianceTypes.Prelicensing, course.ComplianceType);
        Assert.Equal(4500, course.RequiredInstructionalMinutes);
        Assert.Equal(75, course.MinimumPassingPercent);
        Assert.Equal(80, course.MinimumAttendancePercent);
        Assert.True(course.UsesUnitBasedStructure);
        Assert.Equal("Unit", course.SectionLabel);
        Assert.Contains(course.Modules, module => module.Title.Contains("Licensing Requirements"));
        Assert.Contains(course.Modules, module => module.Title.Contains("Agency"));
        Assert.Contains(course.Modules, module => module.Title.Contains("Exam Readiness"));

        var otherCourses = await fixture.DbContext.Courses
            .Where(existing => existing.Id != course.Id)
            .ToListAsync();
        Assert.NotEmpty(otherCourses);
        Assert.All(otherCourses, existing =>
        {
            Assert.Equal(CourseComplianceTypes.ContinuingEducation, existing.ComplianceType);
            Assert.Equal(ContinuingEducationTypes.Elective, existing.ContinuingEducationType);
        });
    }

    [Fact]
    public async Task SeedAsync_ExpandsPrelicensingCourse_WithSubHourRegulatoryUnits()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await CourseSeed.SeedAsync(fixture.DbContext);

        var course = await fixture.DbContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
            .SingleAsync(course => course.Title == "NC Real Estate 75-Hour Pre-Licensing Broker");

        var contentModules = course.Modules
            .Where(module => !module.Title.Equals("Getting Started", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(7, contentModules.Count);
        Assert.True(contentModules.All(module => module.Lessons.Count >= 1));
        Assert.True(contentModules.Sum(module => module.Lessons.Count) >= 18);
    }

    [Fact]
    public async Task SeedAsync_AddsRuleCompliantOrientationAndMandatoryAssessmentForEveryPrelicensingUnit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await CourseSeed.SeedAsync(fixture.DbContext);

        var course = await fixture.DbContext.Courses
            .Include(existing => existing.Modules)
                .ThenInclude(module => module.Lessons)
            .SingleAsync(existing => existing.Id == Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f"));
        var assessments = await fixture.DbContext.CourseCheckpointDefinitions
            .Include(definition => definition.Options)
            .Where(definition => definition.CourseId == course.Id)
            .ToListAsync();

        var orientation = course.Modules
            .Single(module => module.Title == "Getting Started")
            .Lessons.Single(lesson => lesson.Title == "Welcome");
        Assert.Contains("Course syllabus and schedule", orientation.TextContent);
        Assert.Contains("Required materials and resources", orientation.TextContent);
        Assert.Contains("180 days after enrollment", orientation.TextContent);
        Assert.Contains("Course navigation", orientation.TextContent);

        var regulatoryUnits = course.Modules.Where(module => module.Title != "Getting Started").ToList();
        Assert.Equal(7, regulatoryUnits.Count);
        Assert.All(regulatoryUnits, unit =>
        {
            Assert.InRange(unit.Lessons.Sum(lesson => lesson.DurationMinutes), 1, 60);
            var assessment = Assert.Single(assessments.Where(definition => definition.ModuleId == unit.Id && definition.LessonId is null));
            Assert.True(assessment.GatesProgression);
            Assert.Single(assessment.Options.Where(option => option.IsCorrect));
            Assert.True(assessment.Options.Count(option => !option.IsCorrect) >= 2);
        });
    }

    [Fact]
    public async Task SeedAsync_RepairsExistingNCPrelicensingCourse_ToSubHourRegulatoryUnits()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var existingCourse = new Course
        {
            Id = Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f"),
            Title = "Legacy NC Broker Course",
            Description = "Old structure",
            Level = "Beginner",
            Jurisdiction = "General",
            Price = 0m,
            ComplianceType = CourseComplianceTypes.Prelicensing,
            DurationHours = 12,
            CreditHours = 3,
            RequiredInstructionalMinutes = 0,
            MinimumPassingPercent = 0,
            MinimumAttendancePercent = 0,
            IsPublished = true,
            CreatedAt = DateTime.UtcNow
        };

        fixture.DbContext.Courses.Add(existingCourse);
        fixture.DbContext.Modules.AddRange(
            new Module { Id = Guid.NewGuid(), CourseId = existingCourse.Id, Title = "Property Characteristics, Ownership, and Disclosures", OrderIndex = 1 },
            new Module { Id = Guid.NewGuid(), CourseId = existingCourse.Id, Title = "Agency, Disclosure, and Contracts", OrderIndex = 2 },
            new Module { Id = Guid.NewGuid(), CourseId = existingCourse.Id, Title = "Financing, Settlement, and Valuation", OrderIndex = 3 },
            new Module { Id = Guid.NewGuid(), CourseId = existingCourse.Id, Title = "Landlord/Tenant, Fair Housing, and NC Practice", OrderIndex = 4 },
            new Module { Id = Guid.NewGuid(), CourseId = existingCourse.Id, Title = "Exam Readiness and First Transactions", OrderIndex = 5 },
            new Module { Id = Guid.NewGuid(), CourseId = existingCourse.Id, Title = "Legacy Extra Module", OrderIndex = 6 }
        );
        await fixture.DbContext.SaveChangesAsync();

        await CourseSeed.SeedAsync(fixture.DbContext);

        var refreshed = await fixture.DbContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
            .SingleAsync(course => course.Id == existingCourse.Id);

        Assert.Equal(CourseComplianceTypes.Prelicensing, refreshed.ComplianceType);
        Assert.Equal("NC Real Estate 75-Hour Pre-Licensing Broker", refreshed.Title);
        Assert.Equal(75, refreshed.DurationHours);
        Assert.Equal(75, refreshed.CreditHours);
        Assert.Equal(4500, refreshed.RequiredInstructionalMinutes);
        Assert.Equal(75, refreshed.MinimumPassingPercent);
        Assert.Equal(80, refreshed.MinimumAttendancePercent);
        Assert.Equal(8, refreshed.Modules.Count);
        Assert.Equal(7, refreshed.Modules.Count(module => !module.Title.Equals("Getting Started", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(refreshed.Modules, module => module.Title == "Licensing Requirements and Eligibility");
        Assert.Contains(refreshed.Modules, module => module.Title == "Agency, Disclosure, and Contracts");
        Assert.Contains(refreshed.Modules, module => module.Title == "Exam Readiness and First Transactions I");
        Assert.Contains(refreshed.Modules, module => module.Title == "Exam Readiness and First Transactions II");
        Assert.DoesNotContain(refreshed.Modules, module => module.Title == "Legacy Extra Module");
        Assert.True(refreshed.Modules
            .Where(module => !module.Title.Equals("Getting Started", StringComparison.OrdinalIgnoreCase))
            .All(module => module.Lessons.Count >= 1));
    }

    [Fact]
    public void CourseAuthoringProgressHelper_RequiresPrelicensingComplianceSettingsWhenComplianceTypeIsSet()
    {
        var prelicensingCourse = new Course
        {
            Title = "NC Prelicensing Course",
            Description = "State-regulated prep course",
            Level = "Beginner",
            Price = 0m,
            ComplianceType = CourseComplianceTypes.Prelicensing,
            RequiredInstructionalMinutes = 4500,
            MinimumPassingPercent = 75,
            MinimumAttendancePercent = 80,
            Modules = new List<Module>
            {
                new()
                {
                    Title = "Unit 1",
                    Lessons = new List<Lesson>
                    {
                        new() { Title = "Welcome", TextContent = "Lesson" }
                    }
                }
            }
        };

        var snapshot = CourseAuthoringProgressHelper.Evaluate(prelicensingCourse);

        Assert.Contains(snapshot.Items, item => item.Label == "Prelicensing compliance settings are defined" && item.IsComplete);
    }

    [Fact]
    public void CourseComplianceTypes_ToDisplayText_UsesPrelicensingLabel()
    {
        Assert.Equal("Pre-licensing", CourseComplianceTypes.ToDisplayText(CourseComplianceTypes.Prelicensing));
        Assert.Equal("Continuing education", CourseComplianceTypes.ToDisplayText(CourseComplianceTypes.ContinuingEducation));
    }

    [Fact]
    public void ContinuingEducationTypes_ExposeSupportedTypesWithRegulatoryLabels()
    {
        Assert.Equal(
            [ContinuingEducationTypes.GeneralUpdate, ContinuingEducationTypes.BrokerInChargeUpdate, ContinuingEducationTypes.Elective],
            ContinuingEducationTypes.All);
        Assert.Equal("General Update (GENUP)", ContinuingEducationTypes.ToDisplayText(ContinuingEducationTypes.GeneralUpdate));
        Assert.Equal("Broker-in-Charge Update (BICUP)", ContinuingEducationTypes.ToDisplayText(ContinuingEducationTypes.BrokerInChargeUpdate));
        Assert.Equal("Elective", ContinuingEducationTypes.ToDisplayText(ContinuingEducationTypes.Elective));
    }

    [Theory]
    [InlineData(ContinuingEducationTypes.BrokerInChargeUpdate, CourseComplianceTypes.ContinuingEducation, ContinuingEducationTypes.BrokerInChargeUpdate)]
    [InlineData("Unsupported", CourseComplianceTypes.ContinuingEducation, ContinuingEducationTypes.Elective)]
    [InlineData(ContinuingEducationTypes.GeneralUpdate, CourseComplianceTypes.Prelicensing, null)]
    public async Task CreateAsync_NormalizesContinuingEducationType(
        string? requestedType,
        string complianceType,
        string? expectedType)
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = $"{complianceType} subtype course",
            Level = "Beginner",
            Description = "Validates continuing education subtype normalization.",
            Price = 0m,
            ComplianceType = complianceType,
            ContinuingEducationType = requestedType,
            DeliveryMethod = CourseDeliveryMethods.InPerson
        };

        var created = await fixture.CourseService.CreateAsync(course);

        Assert.Equal(expectedType, created.ContinuingEducationType);
    }

    [Fact]
    public async Task CreateAsync_AppliesPrelicensingDefaults_WhenComplianceTypeIsSelected()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var course = new Course
        {
            Title = "Prelicensing Defaults Course",
            Level = "Beginner",
            Description = "Default compliance values should be applied.",
            Price = 0m,
            ComplianceType = CourseComplianceTypes.Prelicensing
        };

        var created = await fixture.CourseService.CreateAsync(course);

        Assert.Equal(4500, created.RequiredInstructionalMinutes);
        Assert.Equal(75, created.MinimumPassingPercent);
        Assert.Equal(80, created.MinimumAttendancePercent);
    }

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

        var moduleCheckpoint = Assert.Single(checkpoints.Where(definition => !definition.GatesProgression));
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
