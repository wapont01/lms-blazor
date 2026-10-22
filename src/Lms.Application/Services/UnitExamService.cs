using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IUnitExamService
{
    Task<List<UnitExamDefinition>> GetUnitExamDefinitionsAsync(Guid courseId);
    Task<HashSet<string>> GetPassedUnitExamKeysAsync(Guid userId, Guid courseId);
    Task<UnitExamSubmitResult> SubmitUnitExamAsync(Guid userId, Guid courseId, string unitExamKey, IReadOnlyCollection<string> selectedOptionKeys);
}

public sealed record UnitExamOption(string Key, string Label, bool IsCorrect);
public sealed record UnitExamDefinition(
    string Key,
    string Title,
    string Prompt,
    string Description,
    Guid? UnitId,
    bool IsFinal,
    List<UnitExamOption> Options);
public sealed record UnitExamSubmitResult(bool IsPassed, string Message, HashSet<string> CorrectOptionKeys);

public sealed class UnitExamService : IUnitExamService
{
    private static readonly string[] GenericDistractors =
    [
        "Ignore policy updates for one quarter",
        "Skip documentation when deadlines are tight",
        "Treat compliance reviews as optional",
        "Complete incidents without escalation"
    ];

    private readonly ApplicationDbContext _dbContext;

    public UnitExamService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<UnitExamDefinition>> GetUnitExamDefinitionsAsync(Guid courseId)
    {
        var importedDefinitions = await _dbContext.CourseUnitExamDefinitions
            .AsNoTracking()
            .Include(definition => definition.Options)
            .Where(definition => definition.CourseId == courseId)
            .OrderBy(definition => definition.UnitId == null ? 1 : 0)
            .ThenBy(definition => definition.Title)
            .ToListAsync();

        if (importedDefinitions.Count > 0)
        {
            return importedDefinitions
                .Select(definition => new UnitExamDefinition(
                    definition.Key,
                    definition.Title,
                    definition.Prompt,
                    definition.Description,
                    definition.UnitId,
                    definition.IsFinal,
                    definition.Options
                        .OrderBy(option => option.OrderIndex)
                        .Select(option => new UnitExamOption(option.Key, option.Label, option.IsCorrect))
                        .ToList()))
                .ToList();
        }

        var units = await _dbContext.Units
            .AsNoTracking()
            .Include(unit => unit.Lessons)
            .Where(unit => unit.CourseId == courseId)
            .OrderBy(unit => unit.OrderIndex)
            .ToListAsync();

        var trackedUnits = units
            .Where(unit => !IsIntroUnit(unit, units))
            .ToList();

        var definitions = new List<UnitExamDefinition>();

        foreach (var unit in trackedUnits)
        {
            var options = BuildUnitExamOptions(unit, trackedUnits);
            definitions.Add(new UnitExamDefinition(
                BuildUnitExamKey(unit.Id),
                "Unit Exam",
                $"Select all lessons that belong to '{unit.Title}'.",
                "Choose every correct answer. You must match the full set to continue.",
                unit.Id,
                false,
                options));
        }

        if (trackedUnits.Count > 0)
        {
            var finalOptions = BuildFinalUnitExamOptions(trackedUnits);
            definitions.Add(new UnitExamDefinition(
                FinalUnitExamKey,
                "Unit Exam",
                "Select all units that are part of this course.",
                "Choose every correct answer.",
                null,
                true,
                finalOptions));
        }

        return definitions;
    }

    public async Task<HashSet<string>> GetPassedUnitExamKeysAsync(Guid userId, Guid courseId)
    {
        var keys = await _dbContext.UnitExamProgresses
            .AsNoTracking()
            .Where(progress => progress.UserAccountId == userId && progress.CourseId == courseId && progress.Passed)
            .Select(progress => progress.UnitExamKey)
            .ToListAsync();

        return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<UnitExamSubmitResult> SubmitUnitExamAsync(Guid userId, Guid courseId, string unitExamKey, IReadOnlyCollection<string> selectedOptionKeys)
    {
        if (string.IsNullOrWhiteSpace(unitExamKey))
        {
            return new UnitExamSubmitResult(false, "Unit Exam is invalid.", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var definitions = await GetUnitExamDefinitionsAsync(courseId);
        var definition = definitions.FirstOrDefault(item => string.Equals(item.Key, unitExamKey, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            return new UnitExamSubmitResult(false, "Unit Exam was not found.", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
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
            return new UnitExamSubmitResult(false, "Not passed yet. Review the retake hint and try again.", correct);
        }

        var progress = await _dbContext.UnitExamProgresses
            .FirstOrDefaultAsync(item => item.UserAccountId == userId && item.CourseId == courseId && item.UnitExamKey == unitExamKey);

        if (progress is null)
        {
            progress = new UnitExamProgress
            {
                UserAccountId = userId,
                CourseId = courseId,
                UnitExamKey = unitExamKey,
                Passed = true,
                PassedAt = DateTime.UtcNow
            };
            _dbContext.UnitExamProgresses.Add(progress);
        }
        else
        {
            progress.Passed = true;
            progress.PassedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
        return new UnitExamSubmitResult(true, "Pass. Unit Exam requirements are satisfied.", correct);
    }

    public static string BuildUnitExamKey(Guid unitId)
    {
        return $"unit:{unitId:D}";
    }

    public const string FinalUnitExamKey = "course:final";

    private static List<UnitExamOption> BuildUnitExamOptions(Unit unit, List<Unit> trackedUnits)
    {
        var options = new List<UnitExamOption>();

        var unitLessonTitles = unit.Lessons
            .OrderBy(lesson => lesson.OrderIndex)
            .Select(lesson => lesson.Title)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        if (unitLessonTitles.Count == 1)
        {
            unitLessonTitles.Add($"{unit.Title} quick recap");
        }

        if (unitLessonTitles.Count == 0)
        {
            unitLessonTitles.Add(unit.Title);
            unitLessonTitles.Add($"{unit.Title} key controls");
        }

        foreach (var lessonTitle in unitLessonTitles.Take(2))
        {
            options.Add(new UnitExamOption(ToOptionKey(lessonTitle, true, options.Count), lessonTitle, true));
        }

        var distractors = trackedUnits
            .Where(other => other.Id != unit.Id)
            .SelectMany(other => other.Lessons.OrderBy(lesson => lesson.OrderIndex).Select(lesson => lesson.Title))
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        foreach (var distractor in distractors)
        {
            options.Add(new UnitExamOption(ToOptionKey(distractor, false, options.Count), distractor, false));
        }

        foreach (var fallback in GenericDistractors)
        {
            if (options.Count >= 4)
            {
                break;
            }

            options.Add(new UnitExamOption(ToOptionKey(fallback, false, options.Count), fallback, false));
        }

        return options.Take(4).ToList();
    }

    private static List<UnitExamOption> BuildFinalUnitExamOptions(List<Unit> trackedUnits)
    {
        var options = new List<UnitExamOption>();

        var correctUnits = trackedUnits
            .Select(unit => unit.Title)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        if (correctUnits.Count == 1)
        {
            correctUnits.Add($"{correctUnits[0]} (Advanced)");
        }

        foreach (var unitTitle in correctUnits)
        {
            options.Add(new UnitExamOption(ToOptionKey(unitTitle, true, options.Count), unitTitle, true));
        }

        var distractors = GenericDistractors.Take(2).ToList();
        foreach (var distractor in distractors)
        {
            options.Add(new UnitExamOption(ToOptionKey(distractor, false, options.Count), distractor, false));
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

    private static bool IsIntroUnit(Unit unit, IReadOnlyList<Unit> orderedUnits)
    {
        if (string.Equals(unit.Title, "Getting Started", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var firstUnit = orderedUnits
            .OrderBy(existing => existing.OrderIndex)
            .FirstOrDefault();

        if (firstUnit is null || firstUnit.Id != unit.Id)
        {
            return false;
        }

        var firstLesson = firstUnit.Lessons
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
