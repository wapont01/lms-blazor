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
    Task<Unit> AddUnitAsync(Guid courseId, Unit unit);
    Task UpdateUnitAsync(Unit unit);
    Task<Lesson> AddLessonAsync(Guid unitId, Lesson lesson);
    Task UpdateLessonAsync(Lesson lesson);
    Task ImportCourseStructureAsync(Guid courseId, string importText);
    Task<List<CourseUnitExamDefinition>> GetUnitExamDefinitionsForEditingAsync(Guid courseId);
    Task<CourseUnitExamDefinition> SaveUnitExamDefinitionAsync(UnitExamDefinitionEditorModel editor);
    Task DeleteUnitExamDefinitionAsync(Guid unitExamDefinitionId);
    Task<List<Course>> GetCoursesForInstructorAsync(Guid instructorId);
    Task<bool> SubmitForReviewAsync(Guid courseId, Guid instructorId);
    Task<bool> ApproveCourseAsync(Guid courseId, Guid adminUserId);
    Task<bool> RequestChangesAsync(Guid courseId, Guid adminUserId, string note);
    Task<bool> RejectCourseAsync(Guid courseId, Guid adminUserId, string note);
}

public sealed class UnitExamDefinitionEditorModel
{
    public Guid? Id { get; set; }
    public Guid CourseId { get; set; }
    public Guid? UnitId { get; set; }
    public bool IsFinal { get; set; }
    public string Title { get; set; } = "Unit Exam";
    public string Prompt { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<UnitExamOptionEditorItem> Options { get; set; } = new();
}

public sealed class UnitExamOptionEditorItem
{
    public Guid? Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

public class CourseService : ICourseService
{
    private const string GettingStartedUnitTitle = "Getting Started";
    private const string WelcomeLessonTitle = "Welcome";
    private const string WelcomeLessonText = """
        Welcome!

        This course is organized into units that contain lessons.

        Use the course menu to your right to jump directly into units and lessons,

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
            .Include(course => course.Units)
                .ThenInclude(unit => unit.Lessons)
            .OrderBy(c => c.Title)
            .ToListAsync();
    }

    public async Task<Course?> GetByIdAsync(Guid id)
    {
        return await _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.Units)
                .ThenInclude(unit => unit.Lessons)
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

    public async Task<List<Course>> GetCoursesForInstructorAsync(Guid instructorId)
    {
        return await _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.Units)
                .ThenInclude(unit => unit.Lessons)
            .Where(course => course.OwnerInstructorId == instructorId)
            .OrderBy(course => course.Title)
            .ToListAsync();
    }

    public async Task<bool> SubmitForReviewAsync(Guid courseId, Guid instructorId)
    {
        var course = await GetCourseForPublishingAsync(courseId);
        if (course is null || course.OwnerInstructorId != instructorId)
        {
            return false;
        }

        if (course.ReviewStatus != CourseReviewStatuses.Draft && course.ReviewStatus != CourseReviewStatuses.ChangesRequested && course.ReviewStatus != CourseReviewStatuses.Rejected)
        {
            return false;
        }

        if (!DistanceLearningComplianceValidator.Validate(course).IsValid)
        {
            return false;
        }

        course.ReviewStatus = CourseReviewStatuses.PendingReview;
        course.ReviewNote = null;
        course.ReviewedByUserId = null;
        course.ReviewedAt = null;
        course.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ApproveCourseAsync(Guid courseId, Guid adminUserId)
    {
        var course = await GetCourseForPublishingAsync(courseId);
        if (course is null)
        {
            return false;
        }

        if (!DistanceLearningComplianceValidator.Validate(course).IsValid)
        {
            return false;
        }

        course.ReviewStatus = CourseReviewStatuses.Approved;
        course.ReviewNote = null;
        course.ReviewedByUserId = adminUserId;
        course.ReviewedAt = DateTime.UtcNow;
        course.IsPublished = true;
        course.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    private Task<Course?> GetCourseForPublishingAsync(Guid courseId)
    {
        return _dbContext.Courses
            .Include(course => course.Units)
                .ThenInclude(unit => unit.Lessons)
            .FirstOrDefaultAsync(course => course.Id == courseId);
    }

    public async Task<bool> RequestChangesAsync(Guid courseId, Guid adminUserId, string note)
    {
        var course = await _dbContext.Courses.FirstOrDefaultAsync(course => course.Id == courseId);
        if (course is null)
        {
            return false;
        }

        course.ReviewStatus = CourseReviewStatuses.ChangesRequested;
        course.ReviewNote = note;
        course.ReviewedByUserId = adminUserId;
        course.ReviewedAt = DateTime.UtcNow;
        course.IsPublished = false;
        course.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RejectCourseAsync(Guid courseId, Guid adminUserId, string note)
    {
        var course = await _dbContext.Courses.FirstOrDefaultAsync(course => course.Id == courseId);
        if (course is null)
        {
            return false;
        }

        course.ReviewStatus = CourseReviewStatuses.Rejected;
        course.ReviewNote = note;
        course.ReviewedByUserId = adminUserId;
        course.ReviewedAt = DateTime.UtcNow;
        course.IsPublished = false;
        course.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<Unit> AddUnitAsync(Guid courseId, Unit unit)
    {
        var nextOrderIndex = await _dbContext.Units
            .Where(existing => existing.CourseId == courseId)
            .Select(existing => (int?)existing.OrderIndex)
            .MaxAsync() ?? 0;

        unit.CourseId = courseId;
        unit.OrderIndex = nextOrderIndex + 1;

        _dbContext.Units.Add(unit);
        await _dbContext.SaveChangesAsync();

        await EnsureCourseIntroStructureAsync(courseId);
        return unit;
    }

    public async Task UpdateUnitAsync(Unit unit)
    {
        var existingUnit = await _dbContext.Units.FirstOrDefaultAsync(existing => existing.Id == unit.Id);
        if (existingUnit is null)
        {
            return;
        }

        _dbContext.Entry(existingUnit).CurrentValues.SetValues(unit);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<Lesson> AddLessonAsync(Guid unitId, Lesson lesson)
    {
        var nextOrderIndex = await _dbContext.Lessons
            .Where(existing => existing.UnitId == unitId)
            .Select(existing => (int?)existing.OrderIndex)
            .MaxAsync() ?? 0;

        lesson.UnitId = unitId;
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

    public async Task ImportCourseStructureAsync(Guid courseId, string importText)
    {
        if (string.IsNullOrWhiteSpace(importText))
        {
            return;
        }

        var course = await _dbContext.Courses
            .Include(course => course.Units)
                .ThenInclude(unit => unit.Lessons)
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (course is null)
        {
            return;
        }

        var parsedImport = ParseImportText(importText);
        if (parsedImport.Units.Count == 0 && parsedImport.Exams.Count == 0 && parsedImport.UnitExams.Count == 0)
        {
            return;
        }

        foreach (var unit in course.Units.ToList())
        {
            _dbContext.Units.Remove(unit);
        }

        var existingExams = await _dbContext.CourseExams
            .Where(exam => exam.CourseId == courseId)
            .ToListAsync();
        if (existingExams.Count > 0)
        {
            _dbContext.CourseExams.RemoveRange(existingExams);
        }

        var existingUnitExamDefinitions = await _dbContext.CourseUnitExamDefinitions
            .Where(definition => definition.CourseId == courseId)
            .ToListAsync();
        if (existingUnitExamDefinitions.Count > 0)
        {
            _dbContext.CourseUnitExamDefinitions.RemoveRange(existingUnitExamDefinitions);
        }

        await _dbContext.SaveChangesAsync();

        var createdUnits = new List<Unit>();
        foreach (var parsedUnit in parsedImport.Units)
        {
            var unitEntity = new Unit
            {
                CourseId = courseId,
                Title = parsedUnit.Title,
                OrderIndex = parsedUnit.OrderIndex
            };

            _dbContext.Units.Add(unitEntity);
            await _dbContext.SaveChangesAsync();

            createdUnits.Add(unitEntity);

            foreach (var parsedLesson in parsedUnit.Lessons)
            {
                _dbContext.Lessons.Add(new Lesson
                {
                    UnitId = unitEntity.Id,
                    Title = parsedLesson.Title,
                    TextContent = parsedLesson.TextContent,
                    DurationMinutes = parsedLesson.DurationMinutes,
                    IsRequired = parsedLesson.IsRequired,
                    OrderIndex = parsedLesson.OrderIndex,
                    ContentType = "Text"
                });
            }
        }

        if (parsedImport.UnitExams.Count > 0)
        {
            foreach (var parsedUnitExam in parsedImport.UnitExams)
            {
                var unitId = parsedUnitExam.UnitTitle is null
                    ? null
                    : createdUnits.FirstOrDefault(unit => string.Equals(unit.Title, parsedUnitExam.UnitTitle, StringComparison.OrdinalIgnoreCase))?.Id;

                var definitionEntity = new CourseUnitExamDefinition
                {
                    CourseId = courseId,
                    Key = parsedUnitExam.IsFinal ? UnitExamService.FinalUnitExamKey : (unitId is null ? ToUnitExamKey(parsedUnitExam.Title) : UnitExamService.BuildUnitExamKey(unitId.Value)),
                    Title = parsedUnitExam.Title,
                    Prompt = parsedUnitExam.Prompt,
                    Description = parsedUnitExam.Description,
                    UnitId = unitId,
                    IsFinal = parsedUnitExam.IsFinal
                };

                _dbContext.CourseUnitExamDefinitions.Add(definitionEntity);

                for (var index = 0; index < parsedUnitExam.Options.Count; index++)
                {
                    var option = parsedUnitExam.Options[index];
                    definitionEntity.Options.Add(new CourseUnitExamOption
                    {
                        Key = string.IsNullOrWhiteSpace(option.Key) ? ToUnitExamOptionKey(option.Label, index) : option.Key,
                        Label = option.Label,
                        IsCorrect = option.IsCorrect,
                        OrderIndex = index + 1
                    });
                }
            }
        }

        foreach (var parsedExam in parsedImport.Exams)
        {
            var unit = parsedExam.UnitTitle is null
                ? null
                : createdUnits.FirstOrDefault(candidate => string.Equals(candidate.Title, parsedExam.UnitTitle, StringComparison.OrdinalIgnoreCase));

            if (parsedExam.UnitTitle is not null && unit is null)
            {
                throw new InvalidOperationException($"Exam '{parsedExam.Title}' references unknown unit '{parsedExam.UnitTitle}'.");
            }

            var examEntity = new CourseExam
            {
                CourseId = courseId,
                UnitId = unit?.Id,
                Title = parsedExam.Title,
                PassPercent = parsedExam.PassPercent,
                IsRequired = true
            };

            _dbContext.CourseExams.Add(examEntity);

            foreach (var parsedQuestion in parsedExam.Questions)
            {
                examEntity.Questions.Add(new ExamQuestion
                {
                    Prompt = parsedQuestion.Prompt,
                    OptionA = parsedQuestion.OptionA,
                    OptionB = parsedQuestion.OptionB,
                    OptionC = parsedQuestion.OptionC,
                    OptionD = parsedQuestion.OptionD,
                    CorrectOption = NormalizeOption(parsedQuestion.CorrectOption),
                    FeedbackText = string.IsNullOrWhiteSpace(parsedQuestion.FeedbackText) ? null : parsedQuestion.FeedbackText,
                    OrderIndex = parsedQuestion.OrderIndex
                });
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<CourseUnitExamDefinition>> GetUnitExamDefinitionsForEditingAsync(Guid courseId)
    {
        return await _dbContext.CourseUnitExamDefinitions
            .AsNoTracking()
            .Include(definition => definition.Options)
            .Where(definition => definition.CourseId == courseId)
            .OrderBy(definition => definition.UnitId == null ? 1 : 0)
            .ThenBy(definition => definition.Title)
            .ToListAsync();
    }

    public async Task<CourseUnitExamDefinition> SaveUnitExamDefinitionAsync(UnitExamDefinitionEditorModel editor)
    {
        var entity = editor.Id.HasValue
            ? await _dbContext.CourseUnitExamDefinitions
                .Include(definition => definition.Options)
                .FirstOrDefaultAsync(definition => definition.Id == editor.Id.Value)
            : null;

        if (entity is null)
        {
            entity = new CourseUnitExamDefinition
            {
                CourseId = editor.CourseId
            };
            _dbContext.CourseUnitExamDefinitions.Add(entity);
        }

        entity.Title = editor.Title.Trim();
        entity.Prompt = editor.Prompt.Trim();
        entity.Description = editor.Description?.Trim() ?? string.Empty;
        entity.IsFinal = editor.IsFinal;
        entity.UnitId = editor.IsFinal ? null : editor.UnitId;
        entity.Key = editor.IsFinal
            ? UnitExamService.FinalUnitExamKey
            : (entity.UnitId is null ? ToUnitExamKey(entity.Title) : UnitExamService.BuildUnitExamKey(entity.UnitId.Value));

        var keepOptionIds = new HashSet<Guid>();
        for (var index = 0; index < editor.Options.Count; index++)
        {
            var optionInput = editor.Options[index];
            var optionEntity = optionInput.Id.HasValue
                ? entity.Options.FirstOrDefault(option => option.Id == optionInput.Id.Value)
                : null;

            if (optionEntity is null)
            {
                optionEntity = new CourseUnitExamOption();
                entity.Options.Add(optionEntity);
            }

            optionEntity.Label = optionInput.Label.Trim();
            optionEntity.IsCorrect = optionInput.IsCorrect;
            optionEntity.OrderIndex = index + 1;
            if (string.IsNullOrWhiteSpace(optionEntity.Key))
            {
                optionEntity.Key = ToUnitExamOptionKey(optionEntity.Label, index);
            }

            keepOptionIds.Add(optionEntity.Id);
        }

        foreach (var staleOption in entity.Options.Where(option => !keepOptionIds.Contains(option.Id)).ToList())
        {
            entity.Options.Remove(staleOption);
            _dbContext.CourseUnitExamOptions.Remove(staleOption);
        }

        await _dbContext.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteUnitExamDefinitionAsync(Guid unitExamDefinitionId)
    {
        var entity = await _dbContext.CourseUnitExamDefinitions.FindAsync(unitExamDefinitionId);
        if (entity is null)
        {
            return;
        }

        _dbContext.CourseUnitExamDefinitions.Remove(entity);
        await _dbContext.SaveChangesAsync();
    }

    private static ParsedImportDocument ParseImportText(string importText)
    {
        var document = new ParsedImportDocument();
        ParsedUnit? currentUnit = null;
        var currentLessonText = new List<string>();
        ParsedLesson? currentLesson = null;
        ParsedUnitExamDefinition? currentUnitExam = null;
        ParsedExamDefinition? currentExam = null;

        foreach (var rawLine in importText.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentLesson is not null)
                {
                    currentLessonText.Add(string.Empty);
                }
                continue;
            }

            if (line.StartsWith("[unit]", StringComparison.OrdinalIgnoreCase))
            {
                var markerLength = "[unit]".Length;
                if (currentLesson is not null && currentUnit is not null)
                {
                    currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
                    currentLessonText.Clear();
                    currentUnit.Lessons.Add(currentLesson);
                }

                currentLesson = null;
                currentLessonText.Clear();
                currentUnitExam = null;
                currentExam = null;

                currentUnit = new ParsedUnit
                {
                    Title = line[markerLength..].Trim(),
                    OrderIndex = document.Units.Count + 1,
                    Lessons = new List<ParsedLesson>()
                };
                document.Units.Add(currentUnit);
                continue;
            }

            if (line.StartsWith("[lesson]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentLesson is not null && currentUnit is not null)
                {
                    currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
                    currentLessonText.Clear();
                    currentUnit.Lessons.Add(currentLesson);
                }

                currentLesson = ParseLessonDefinition(line);
                currentUnitExam = null;
                currentExam = null;
                if (currentUnit is null)
                {
                    currentUnit = new ParsedUnit
                    {
                        Title = "Unit 1",
                        OrderIndex = document.Units.Count + 1,
                        Lessons = new List<ParsedLesson>()
                    };
                    document.Units.Add(currentUnit);
                }

                currentLessonText.Clear();
                continue;
            }

            if (line.StartsWith("[unit exam]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentLesson is not null && currentUnit is not null)
                {
                    currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
                    currentLessonText.Clear();
                    currentUnit.Lessons.Add(currentLesson);
                }

                currentLesson = null;
                currentLessonText.Clear();
                currentExam = null;
                currentUnitExam = new ParsedUnitExamDefinition
                {
                    Title = "Unit Exam",
                    Prompt = "Select every correct answer.",
                    Description = string.Empty,
                    UnitTitle = line["[unit exam]".Length..].Trim(),
                    IsFinal = string.Equals(line["[unit exam]".Length..].Trim(), "final", StringComparison.OrdinalIgnoreCase)
                };
                document.UnitExams.Add(currentUnitExam);
                continue;
            }

            if (line.StartsWith("[option]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentUnitExam is null)
                {
                    currentUnitExam = new ParsedUnitExamDefinition { Title = "Unit Exam", Prompt = "Select every correct answer.", Description = string.Empty };
                    document.UnitExams.Add(currentUnitExam);
                }

                var optionPayload = line["[option]".Length..].Trim();
                var parts = optionPayload.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var label = parts.FirstOrDefault() ?? "Option";
                var isCorrect = false;
                foreach (var part in parts.Skip(1))
                {
                    var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (keyValue.Length == 2 && string.Equals(keyValue[0], "correct", StringComparison.OrdinalIgnoreCase))
                    {
                        bool.TryParse(keyValue[1], out isCorrect);
                    }
                }

                currentUnitExam.Options.Add(new ParsedUnitExamOption
                {
                    Label = label,
                    IsCorrect = isCorrect,
                    Key = string.Empty
                });
                continue;
            }

            if (line.StartsWith("[exam]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentLesson is not null && currentUnit is not null)
                {
                    currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
                    currentLessonText.Clear();
                    currentUnit.Lessons.Add(currentLesson);
                }

                currentLesson = null;
                currentLessonText.Clear();
                currentUnitExam = null;
                var examParts = line["[exam]".Length..].Trim()
                    .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                currentExam = new ParsedExamDefinition
                {
                    Title = examParts.FirstOrDefault() ?? "Final Exam"
                };
                foreach (var part in examParts.Skip(1))
                {
                    var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (keyValue.Length == 2 && (string.Equals(keyValue[0], "unit", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(keyValue[0], "unit", StringComparison.OrdinalIgnoreCase)))
                    {
                        currentExam.UnitTitle = keyValue[1];
                    }
                    else if (keyValue.Length == 2 && string.Equals(keyValue[0], "passpercent", StringComparison.OrdinalIgnoreCase)
                        && decimal.TryParse(keyValue[1], out var passPercent))
                    {
                        currentExam.PassPercent = passPercent;
                    }
                }
                document.Exams.Add(currentExam);
                continue;
            }

            if (line.StartsWith("[question]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentExam is null)
                {
                    currentExam = new ParsedExamDefinition();
                    document.Exams.Add(currentExam);
                }

                var parsedQuestion = ParseExamQuestionDefinition(line);
                parsedQuestion.OrderIndex = currentExam.Questions.Count + 1;
                currentExam.Questions.Add(parsedQuestion);
                continue;
            }

            if (currentUnitExam is not null)
            {
                ParseUnitExamMetadata(line, currentUnitExam);
                continue;
            }

            if (currentExam is not null)
            {
                ParseExamMetadata(line, currentExam);
                continue;
            }

            if (currentLesson is not null)
            {
                currentLessonText.Add(line);
            }
        }

        if (currentLesson is not null)
        {
            currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
            currentUnit?.Lessons.Add(currentLesson);
        }

        return document;
    }

    private static ParsedLesson ParseLessonDefinition(string line)
    {
        var payload = line["[lesson]".Length..].Trim();
        var parts = payload.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var title = parts.FirstOrDefault() ?? "Untitled Lesson";
        var durationMinutes = 0;
        var isRequired = true;

        foreach (var part in parts.Skip(1))
        {
            var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length != 2)
            {
                continue;
            }

            switch (keyValue[0].ToLowerInvariant())
            {
                case "duration":
                    int.TryParse(keyValue[1], out durationMinutes);
                    break;
                case "required":
                    bool.TryParse(keyValue[1], out isRequired);
                    break;
            }
        }

        return new ParsedLesson
        {
            Title = title,
            DurationMinutes = durationMinutes,
            IsRequired = isRequired,
            TextContent = string.Empty,
            OrderIndex = 0
        };
    }

    private static void ParseUnitExamMetadata(string line, ParsedUnitExamDefinition unitExam)
    {
        var keyValue = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (keyValue.Length != 2)
        {
            return;
        }

        switch (keyValue[0].ToLowerInvariant())
        {
            case "title":
                unitExam.Title = keyValue[1];
                break;
            case "prompt":
                unitExam.Prompt = keyValue[1];
                break;
            case "description":
                unitExam.Description = keyValue[1];
                break;
            case "unit":
                unitExam.UnitTitle = keyValue[1];
                break;
            case "isfinal":
                bool.TryParse(keyValue[1], out var isFinal);
                unitExam.IsFinal = isFinal;
                break;
        }
    }

    private static void ParseExamMetadata(string line, ParsedExamDefinition exam)
    {
        var keyValue = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (keyValue.Length != 2)
        {
            return;
        }

        switch (keyValue[0].ToLowerInvariant())
        {
            case "title":
                exam.Title = keyValue[1];
                break;
            case "passpercent":
                if (decimal.TryParse(keyValue[1], out var passPercent))
                {
                    exam.PassPercent = passPercent;
                }
                break;
            case "unit":
                exam.UnitTitle = keyValue[1];
                break;
        }
    }

    private static ParsedExamQuestion ParseExamQuestionDefinition(string line)
    {
        var payload = line["[question]".Length..].Trim();
        var parts = payload.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var prompt = parts.FirstOrDefault() ?? "Untitled Question";
        var optionA = string.Empty;
        var optionB = string.Empty;
        var optionC = string.Empty;
        var optionD = string.Empty;
        var correctOption = "A";
        var feedbackText = string.Empty;

        foreach (var part in parts.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = part[..separatorIndex].Trim();
            var value = part[(separatorIndex + 1)..].Trim();

            switch (key.ToLowerInvariant())
            {
                case "optiona":
                    optionA = value;
                    break;
                case "optionb":
                    optionB = value;
                    break;
                case "optionc":
                    optionC = value;
                    break;
                case "optiond":
                    optionD = value;
                    break;
                case "correct":
                case "correctoption":
                    correctOption = NormalizeOption(value);
                    break;
                case "feedback":
                    feedbackText = value;
                    break;
            }
        }

        return new ParsedExamQuestion
        {
            Prompt = prompt,
            OptionA = optionA,
            OptionB = optionB,
            OptionC = optionC,
            OptionD = optionD,
            CorrectOption = correctOption,
            FeedbackText = feedbackText,
            OrderIndex = 0
        };
    }

    private static string NormalizeOption(string? option)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return "A";
        }

        var normalized = option.Trim().ToUpperInvariant();
        return normalized is "A" or "B" or "C" or "D" ? normalized : "A";
    }

    private static string ToUnitExamKey(string label)
    {
        var cleaned = new string(label.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "unit exam" : cleaned.ToLowerInvariant();
    }

    private static string ToUnitExamOptionKey(string label, int index)
    {
        var cleaned = new string(label.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = $"option{index}";
        }

        return $"option-{cleaned.ToLowerInvariant()}-{index}";
    }

    private async Task EnsureCourseIntroStructureAsync(Guid courseId)
    {
        var units = await _dbContext.Units
            .Where(existing => existing.CourseId == courseId)
            .OrderBy(existing => existing.OrderIndex)
            .ToListAsync();

        var introUnit = units.FirstOrDefault(existing => existing.Title == GettingStartedUnitTitle);
        if (introUnit is null)
        {
            foreach (var existingUnit in units)
            {
                existingUnit.OrderIndex += 1;
            }

            introUnit = new Unit
            {
                Id = Guid.NewGuid(),
                CourseId = courseId,
                Title = GettingStartedUnitTitle,
                OrderIndex = 1
            };

            _dbContext.Units.Add(introUnit);
            await _dbContext.SaveChangesAsync();
        }

        var introLessons = await _dbContext.Lessons
            .Where(existing => existing.UnitId == introUnit.Id)
            .OrderBy(existing => existing.OrderIndex)
            .ToListAsync();

        var hasWelcome = introLessons.Any(existing => existing.Title == WelcomeLessonTitle);
        if (!hasWelcome)
        {
            _dbContext.Lessons.Add(BuildWelcomeLesson(introUnit.Id));
        }
        else
        {
            var welcomeLesson = introLessons.First(existing => existing.Title == WelcomeLessonTitle);
            welcomeLesson.TextContent = WelcomeLessonText;
        }

        var deprecatedNavigationLessons = await _dbContext.Lessons
            .Where(existing =>
                DeprecatedNavigationTitles.Contains(existing.Title)
                && _dbContext.Units.Any(unit => unit.Id == existing.UnitId && unit.CourseId == courseId))
            .ToListAsync();

        if (deprecatedNavigationLessons.Count > 0)
        {
            _dbContext.Lessons.RemoveRange(deprecatedNavigationLessons);
        }

        await _dbContext.SaveChangesAsync();
    }

    private static Lesson BuildWelcomeLesson(Guid unitId)
    {
        return new Lesson
        {
            UnitId = unitId,
            Title = WelcomeLessonTitle,
            ContentType = "Text",
            TextContent = """
                Welcome!

                This course is organized into units that contains lessons.

                Use the course menu to your right to jump directly to topics,

                or use the arrow buttons for step-by-step progression.
                """,
            DurationMinutes = 8,
            IsRequired = true,
            OrderIndex = 1
        };
    }

    private sealed class ParsedImportDocument
    {
        public List<ParsedUnit> Units { get; } = new();
        public List<ParsedUnitExamDefinition> UnitExams { get; } = new();
        public List<ParsedExamDefinition> Exams { get; } = new();
    }

    private sealed class ParsedUnit
    {
        public string Title { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public List<ParsedLesson> Lessons { get; set; } = new();
    }

    private sealed class ParsedLesson
    {
        public string Title { get; set; } = string.Empty;
        public string TextContent { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public bool IsRequired { get; set; } = true;
        public int OrderIndex { get; set; }
    }

    private sealed class ParsedUnitExamDefinition
    {
        public string Title { get; set; } = "Unit Exam";
        public string Prompt { get; set; } = "Select every correct answer.";
        public string Description { get; set; } = string.Empty;
        public string? UnitTitle { get; set; }
        public bool IsFinal { get; set; }
        public List<ParsedUnitExamOption> Options { get; } = new();
    }

    private sealed class ParsedUnitExamOption
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    private sealed class ParsedExamDefinition
    {
        public string Title { get; set; } = "Final Exam";
        public decimal PassPercent { get; set; } = 80m;
        public string? UnitTitle { get; set; }
        public List<ParsedExamQuestion> Questions { get; } = new();
    }

    private sealed class ParsedExamQuestion
    {
        public string Prompt { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = "A";
        public string? FeedbackText { get; set; }
        public int OrderIndex { get; set; }
    }
}
