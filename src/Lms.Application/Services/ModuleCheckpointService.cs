using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IModuleCheckpointService
{
    Task<List<ModuleCheckpointDefinition>> GetCheckpointDefinitionsAsync(Guid courseId);
    Task<HashSet<string>> GetPassedCheckpointKeysAsync(Guid userId, Guid courseId);
    Task<ModuleCheckpointSubmitResult> SubmitCheckpointAsync(Guid userId, Guid courseId, string checkpointKey, IReadOnlyCollection<string> selectedOptionKeys);
}

public sealed record ModuleCheckpointOption(string Key, string Label, bool IsCorrect);
public sealed record ModuleCheckpointDefinition(
    string Key,
    string Title,
    string Prompt,
    string Description,
    Guid? ModuleId,
    Guid? LessonId,
    bool GatesProgression,
    List<ModuleCheckpointOption> Options);
public sealed record ModuleCheckpointSubmitResult(bool IsPassed, string Message, HashSet<string> CorrectOptionKeys);

public sealed class ModuleCheckpointService : IModuleCheckpointService
{
    private static readonly string[] GenericDistractors =
    [
        "Ignore policy updates for one quarter",
        "Skip documentation when deadlines are tight",
        "Treat compliance reviews as optional",
        "Complete incidents without escalation"
    ];

    private readonly ApplicationDbContext _dbContext;

    public ModuleCheckpointService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ModuleCheckpointDefinition>> GetCheckpointDefinitionsAsync(Guid courseId)
    {
        var importedDefinitions = await _dbContext.CourseCheckpointDefinitions
            .AsNoTracking()
            .Include(definition => definition.Options)
            .Where(definition => definition.CourseId == courseId)
            .OrderBy(definition => definition.Title)
            .ToListAsync();

        if (importedDefinitions.Count > 0)
        {
            return importedDefinitions
                .Select(definition => new ModuleCheckpointDefinition(
                    definition.Key,
                    definition.Title,
                    definition.Prompt,
                    definition.Description,
                    definition.ModuleId,
                    definition.LessonId,
                    definition.GatesProgression,
                    definition.Options
                        .OrderBy(option => option.OrderIndex)
                        .Select(option => new ModuleCheckpointOption(option.Key, option.Label, option.IsCorrect))
                        .ToList()))
                .ToList();
        }

        var modules = await _dbContext.Modules
            .AsNoTracking()
            .Include(module => module.Lessons)
            .Where(module => module.CourseId == courseId)
            .OrderBy(module => module.OrderIndex)
            .ToListAsync();

        var trackedModules = modules
            .Where(module => !IsIntroModule(module, modules))
            .ToList();

        var definitions = new List<ModuleCheckpointDefinition>();

        foreach (var module in trackedModules)
        {
            var options = BuildModuleCheckpointOptions(module, trackedModules);
            definitions.Add(new ModuleCheckpointDefinition(
                BuildModuleCheckpointKey(module.Id),
                "Checkpoint",
                $"Select all lessons that belong to '{module.Title}'.",
                "Choose every correct answer. You must match the full set to continue.",
                module.Id,
                null,
                true,
                options));
        }

        return definitions;
    }

    public async Task<HashSet<string>> GetPassedCheckpointKeysAsync(Guid userId, Guid courseId)
    {
        var keys = await _dbContext.ModuleCheckpointProgresses
            .AsNoTracking()
            .Where(progress => progress.UserAccountId == userId && progress.CourseId == courseId && progress.Passed)
            .Select(progress => progress.CheckpointKey)
            .ToListAsync();

        return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ModuleCheckpointSubmitResult> SubmitCheckpointAsync(Guid userId, Guid courseId, string checkpointKey, IReadOnlyCollection<string> selectedOptionKeys)
    {
        if (string.IsNullOrWhiteSpace(checkpointKey))
        {
            return new ModuleCheckpointSubmitResult(false, "Checkpoint is invalid.", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var definitions = await GetCheckpointDefinitionsAsync(courseId);
        var definition = definitions.FirstOrDefault(item => string.Equals(item.Key, checkpointKey, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            return new ModuleCheckpointSubmitResult(false, "Checkpoint was not found.", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var normalizedSelected = selectedOptionKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var correct = definition.Options
            .Where(option => option.IsCorrect)
            .Select(option => option.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isPassed = normalizedSelected.SetEquals(correct);
        if (!isPassed)
        {
            return new ModuleCheckpointSubmitResult(false, "Not passed yet. Review the retake hint and try again.", correct);
        }

        var progress = await _dbContext.ModuleCheckpointProgresses
            .FirstOrDefaultAsync(item => item.UserAccountId == userId && item.CourseId == courseId && item.CheckpointKey == checkpointKey);

        if (progress is null)
        {
            progress = new ModuleCheckpointProgress
            {
                UserAccountId = userId,
                CourseId = courseId,
                CheckpointKey = checkpointKey,
                Passed = true,
                PassedAt = DateTime.UtcNow
            };
            _dbContext.ModuleCheckpointProgresses.Add(progress);
        }
        else
        {
            progress.Passed = true;
            progress.PassedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
        return new ModuleCheckpointSubmitResult(true, "Pass. Checkpoint requirements are satisfied.", correct);
    }

    public static string BuildModuleCheckpointKey(Guid moduleId)
    {
        return $"module:{moduleId:D}";
    }

    private static List<ModuleCheckpointOption> BuildModuleCheckpointOptions(Module module, List<Module> trackedModules)
    {
        var options = new List<ModuleCheckpointOption>();

        var moduleLessonTitles = module.Lessons
            .OrderBy(lesson => lesson.OrderIndex)
            .Select(lesson => lesson.Title)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        if (moduleLessonTitles.Count == 1)
        {
            moduleLessonTitles.Add($"{module.Title} quick recap");
        }

        if (moduleLessonTitles.Count == 0)
        {
            moduleLessonTitles.Add(module.Title);
            moduleLessonTitles.Add($"{module.Title} key controls");
        }

        foreach (var lessonTitle in moduleLessonTitles.Take(2))
        {
            options.Add(new ModuleCheckpointOption(ToOptionKey(lessonTitle, true, options.Count), lessonTitle, true));
        }

        var distractors = trackedModules
            .Where(other => other.Id != module.Id)
            .SelectMany(other => other.Lessons.OrderBy(lesson => lesson.OrderIndex).Select(lesson => lesson.Title))
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        foreach (var distractor in distractors)
        {
            options.Add(new ModuleCheckpointOption(ToOptionKey(distractor, false, options.Count), distractor, false));
        }

        foreach (var fallback in GenericDistractors)
        {
            if (options.Count >= 4)
            {
                break;
            }

            options.Add(new ModuleCheckpointOption(ToOptionKey(fallback, false, options.Count), fallback, false));
        }

        return options.Take(4).ToList();
    }

    private static string ToOptionKey(string label, bool isCorrect, int index)
    {
        var cleaned = new string(label.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "option";
        }

        var prefix = isCorrect ? "c" : "i";
        return $"{prefix}-{cleaned.ToLowerInvariant()}-{index}";
    }

    private static bool IsIntroModule(Module module, IReadOnlyList<Module> orderedModules)
    {
        if (string.Equals(module.Title, "Getting Started", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var firstModule = orderedModules
            .OrderBy(existing => existing.OrderIndex)
            .FirstOrDefault();

        if (firstModule is null || firstModule.Id != module.Id)
        {
            return false;
        }

        var firstLesson = firstModule.Lessons
            .OrderBy(lesson => lesson.OrderIndex)
            .FirstOrDefault();

        return firstLesson is null || IsOrientationLesson(firstLesson);
    }

    private static bool IsOrientationLesson(Lesson lesson)
    {
        if (string.IsNullOrWhiteSpace(lesson.Title))
        {
            return false;
        }

        return lesson.Title.Contains("welcome", StringComparison.OrdinalIgnoreCase)
            || lesson.Title.Contains("instructor", StringComparison.OrdinalIgnoreCase);
    }
}
