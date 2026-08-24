using Lms.Domain.Entities;

namespace Lms.Application.Services;

public sealed record CourseAuthoringProgressItem(string Label, bool IsComplete);

public sealed record CourseAuthoringProgressSnapshot(
    int PercentComplete,
    IReadOnlyList<CourseAuthoringProgressItem> Items);

public static class CourseAuthoringProgressHelper
{
    // Full "ready for review" definition spanning course fields, module/lesson structure, checkpoints,
    // and the final assessment. `checkpoints` must include every CourseCheckpointDefinition for this
    // course (Module isn't a navigation target of checkpoints, so callers load/pass them separately).
    public static CourseAuthoringProgressSnapshot Evaluate(Course course, IReadOnlyCollection<CourseCheckpointDefinition>? checkpoints = null)
    {
        var modules = course.Modules ?? new List<Module>();
        var allCheckpoints = checkpoints ?? Array.Empty<CourseCheckpointDefinition>();
        var hasModules = modules.Count > 0;
        var hasLesson = modules.Any(module => module.Lessons?.Count > 0);
        var everyModuleHasLesson = hasModules && modules.All(module => module.Lessons?.Count > 0);
        var everyModuleHasCheckpoint = hasModules && allCheckpoints.Count > 0 && modules.All(module => allCheckpoints.Any(checkpoint => checkpoint.ModuleId == module.Id));
        var everyUnitHasProgressionCheckpoint = course.UsesUnitBasedStructure
            && hasModules
            && modules.All(module => allCheckpoints.Any(checkpoint => checkpoint.ModuleId == module.Id && checkpoint.GatesProgression));
        var everyCheckpointComplete = allCheckpoints.Count > 0 && allCheckpoints.All(checkpoint =>
            !string.IsNullOrWhiteSpace(checkpoint.Title)
            && !string.IsNullOrWhiteSpace(checkpoint.Prompt)
            && checkpoint.Options.Count >= 2
            && checkpoint.Options.Any(option => option.IsCorrect));

        var requiredAssessment = course.Assessments?.FirstOrDefault(assessment => assessment.IsRequired);
        var assessmentIsCustomized = requiredAssessment is not null
            && requiredAssessment.Questions.Count > 0
            && !AssessmentService.MatchesDefaultAssessmentContent(requiredAssessment);
        var regulatedComplianceDefined = course.UsesUnitBasedStructure
            && course.RequiredInstructionalMinutes > 0
            && course.MinimumPassingPercent > 0
            && course.MinimumAttendancePercent > 0;

        var items = new List<CourseAuthoringProgressItem>
        {
            new("Course title and description are defined", !string.IsNullOrWhiteSpace(course.Title) && !string.IsNullOrWhiteSpace(course.Description)),
            new("Course level, jurisdiction, duration, and pricing are set", !string.IsNullOrWhiteSpace(course.Level) && course.Price >= 0m),
            new("At least one module exists", hasModules),
            new("At least one lesson exists", hasLesson),
            new("Every module has at least one lesson", everyModuleHasLesson),
            new("Every module has at least one checkpoint", everyModuleHasCheckpoint),
            new("All checkpoints have a prompt, options, and a correct answer marked", everyCheckpointComplete),
            new("Final assessment has been customized with your own questions", assessmentIsCustomized)
        };

        if (course.UsesUnitBasedStructure)
        {
            items.Add(new CourseAuthoringProgressItem("Every unit has a progression checkpoint", everyUnitHasProgressionCheckpoint));
            items.Add(new CourseAuthoringProgressItem("Prelicensing compliance settings are defined", regulatedComplianceDefined));
        }

        var completed = items.Count(item => item.IsComplete);
        var percentComplete = items.Count == 0 ? 0 : (completed * 100) / items.Count;

        return new CourseAuthoringProgressSnapshot(percentComplete, items);
    }
}

