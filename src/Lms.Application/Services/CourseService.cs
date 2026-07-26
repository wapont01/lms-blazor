using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface ICourseService
{
    Task<List<Course>> GetAllAsync();
    Task<List<Course>> GetPublishedCoursesAsync();
    Task<Course?> GetByIdAsync(Guid id);
    Task<Course> CreateAsync(Course course);
    Task UpdateAsync(Course course);
    Task<Module> AddModuleAsync(Guid courseId, Module module);
    Task UpdateModuleAsync(Module module);
    Task<Lesson> AddLessonAsync(Guid moduleId, Lesson lesson);
    Task UpdateLessonAsync(Lesson lesson);
}

public class CourseService : ICourseService
{
    private const string GettingStartedModuleTitle = "Getting Started";
    private const string WelcomeLessonTitle = "Welcome";
    private const string WelcomeLessonText = """
        Welcome!

        This course is organized into modules that contain lessons.

        Use the course menu to your right to jump directly into modules and lessons,

        or use the arrow buttons for step-by-step progression.
        """;
    private static readonly string[] DeprecatedNavigationTitles = ["Navigation", "How to navigate this course"];

    private readonly ApplicationDbContext _dbContext;

    public CourseService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Course>> GetPublishedCoursesAsync()
    {
        return await _dbContext.Courses
            .AsNoTracking()
            .Where(c => c.IsPublished && !c.IsArchived)
            .OrderBy(c => c.Title)
            .ToListAsync();
    }

    public async Task<List<Course>> GetAllAsync()
    {
        return await _dbContext.Courses
            .AsNoTracking()
            .OrderBy(c => c.Title)
            .ToListAsync();
    }

    public async Task<Course?> GetByIdAsync(Guid id)
    {
        return await _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
            .FirstOrDefaultAsync(course => course.Id == id);
    }

    public async Task<Course> CreateAsync(Course course)
    {
        course.CreatedAt = DateTime.UtcNow;
        course.UpdatedAt = null;
        _dbContext.Courses.Add(course);
        await _dbContext.SaveChangesAsync();

        await EnsureCourseIntroStructureAsync(course.Id);

        return course;
    }

    public async Task UpdateAsync(Course course)
    {
        var existingCourse = await _dbContext.Courses.FirstOrDefaultAsync(existing => existing.Id == course.Id);
        if (existingCourse is null)
        {
            return;
        }

        _dbContext.Entry(existingCourse).CurrentValues.SetValues(course);
        existingCourse.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
    }

    public async Task<Module> AddModuleAsync(Guid courseId, Module module)
    {
        var nextOrderIndex = await _dbContext.Modules
            .Where(existing => existing.CourseId == courseId)
            .Select(existing => (int?)existing.OrderIndex)
            .MaxAsync() ?? 0;

        module.CourseId = courseId;
        module.OrderIndex = nextOrderIndex + 1;

        _dbContext.Modules.Add(module);
        await _dbContext.SaveChangesAsync();

        await EnsureCourseIntroStructureAsync(courseId);
        return module;
    }

    public async Task UpdateModuleAsync(Module module)
    {
        var existingModule = await _dbContext.Modules.FirstOrDefaultAsync(existing => existing.Id == module.Id);
        if (existingModule is null)
        {
            return;
        }

        _dbContext.Entry(existingModule).CurrentValues.SetValues(module);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Lesson> AddLessonAsync(Guid moduleId, Lesson lesson)
    {
        var nextOrderIndex = await _dbContext.Lessons
            .Where(existing => existing.ModuleId == moduleId)
            .Select(existing => (int?)existing.OrderIndex)
            .MaxAsync() ?? 0;

        lesson.ModuleId = moduleId;
        lesson.OrderIndex = nextOrderIndex + 1;

        _dbContext.Lessons.Add(lesson);
        await _dbContext.SaveChangesAsync();
        return lesson;
    }

    public async Task UpdateLessonAsync(Lesson lesson)
    {
        var existingLesson = await _dbContext.Lessons.FirstOrDefaultAsync(existing => existing.Id == lesson.Id);
        if (existingLesson is null)
        {
            return;
        }

        _dbContext.Entry(existingLesson).CurrentValues.SetValues(lesson);
        await _dbContext.SaveChangesAsync();
    }

    private async Task EnsureCourseIntroStructureAsync(Guid courseId)
    {
        var modules = await _dbContext.Modules
            .Where(existing => existing.CourseId == courseId)
            .OrderBy(existing => existing.OrderIndex)
            .ToListAsync();

        var introModule = modules.FirstOrDefault(existing => existing.Title == GettingStartedModuleTitle);
        if (introModule is null)
        {
            foreach (var existingModule in modules)
            {
                existingModule.OrderIndex += 1;
            }

            introModule = new Module
            {
                Id = Guid.NewGuid(),
                CourseId = courseId,
                Title = GettingStartedModuleTitle,
                OrderIndex = 1
            };

            _dbContext.Modules.Add(introModule);
            await _dbContext.SaveChangesAsync();
        }

        var introLessons = await _dbContext.Lessons
            .Where(existing => existing.ModuleId == introModule.Id)
            .OrderBy(existing => existing.OrderIndex)
            .ToListAsync();

        var hasWelcome = introLessons.Any(existing => existing.Title == WelcomeLessonTitle);
        if (!hasWelcome)
        {
            _dbContext.Lessons.Add(BuildWelcomeLesson(introModule.Id));
        }
        else
        {
            var welcomeLesson = introLessons.First(existing => existing.Title == WelcomeLessonTitle);
            welcomeLesson.TextContent = WelcomeLessonText;
        }

        var deprecatedNavigationLessons = await _dbContext.Lessons
            .Where(existing =>
                DeprecatedNavigationTitles.Contains(existing.Title)
                && _dbContext.Modules.Any(module => module.Id == existing.ModuleId && module.CourseId == courseId))
            .ToListAsync();

        if (deprecatedNavigationLessons.Count > 0)
        {
            _dbContext.Lessons.RemoveRange(deprecatedNavigationLessons);
        }

        await _dbContext.SaveChangesAsync();
    }

    private static Lesson BuildWelcomeLesson(Guid moduleId)
    {
        return new Lesson
        {
            ModuleId = moduleId,
            Title = WelcomeLessonTitle,
            ContentType = "Text",
            TextContent = """
                Welcome!

                This course is organized into modules that contains lessons.

                Use the course menu to your right to jump directly to topics,

                or use the arrow buttons for step-by-step progression.
                """,
            DurationMinutes = 8,
            IsRequired = true,
            OrderIndex = 1
        };
    }

}
