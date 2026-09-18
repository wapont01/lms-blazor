using Lms.Domain.Entities;

namespace Lms.Application.Services;

public sealed record DistanceLearningValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class DistanceLearningComplianceValidator
{
    public static DistanceLearningValidationResult Validate(Course course)
    {
        if (!CourseComplianceTypes.IsRegulated(course.ComplianceType))
        {
            return new DistanceLearningValidationResult([]);
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(course.State) || !DistanceLearningProfileCatalog.TryGet(course.State, out var profile))
        {
            errors.Add("A supported distance-learning state is required.");
        }
        else if (!profile!.AllowedDeliveryMethods.Contains(course.DeliveryMethod, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"The delivery method '{course.DeliveryMethod}' is not allowed for {profile!.State}.");
        }

        if (course.RequiredInstructionalMinutes <= 0)
        {
            errors.Add("Required instructional minutes must be greater than zero.");
        }

        if (course.CompletionWindowDays is null or <= 0)
        {
            errors.Add("A positive completion window is required.");
        }

        if (course.MinimumPassingPercent <= 0)
        {
            errors.Add("A positive minimum passing score is required.");
        }

        var lessons = course.Modules.SelectMany(module => module.Lessons).ToList();
        if (lessons.Count == 0)
        {
            errors.Add("At least one required lesson is needed before the course can be published.");
        }
        else if (lessons.Any(lesson => lesson.DurationMinutes <= 0 || string.IsNullOrWhiteSpace(lesson.TextContent)))
        {
            errors.Add("Every lesson must have content and a positive duration before publication.");
        }

        return new DistanceLearningValidationResult(errors);
    }
}