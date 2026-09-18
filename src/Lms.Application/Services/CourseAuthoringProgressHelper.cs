using Lms.Domain.Entities;

namespace Lms.Application.Services;

public sealed record CourseAuthoringProgressItem(string Label, bool IsComplete);

public sealed record CourseAuthoringProgressSnapshot(
    int PercentComplete,
    IReadOnlyList<CourseAuthoringProgressItem> Items);

public static class CourseAuthoringProgressHelper
{
    public static CourseAuthoringProgressSnapshot Evaluate(Course course)
    {
        var items = new List<CourseAuthoringProgressItem>
        {
            new("Course title and description are defined", !string.IsNullOrWhiteSpace(course.Title) && !string.IsNullOrWhiteSpace(course.Description)),
            new("Course level and pricing are set", !string.IsNullOrWhiteSpace(course.Level) && course.Price >= 0m),
            new("At least one module exists", course.Modules?.Count > 0),
            new("At least one lesson exists", course.Modules?.Any(module => module.Lessons?.Count > 0) == true)
        };

        var completed = items.Count(item => item.IsComplete);
        var percentComplete = items.Count == 0 ? 0 : (completed * 100) / items.Count;

        return new CourseAuthoringProgressSnapshot(percentComplete, items);
    }
}
