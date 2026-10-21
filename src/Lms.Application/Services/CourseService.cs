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
    Task ImportCourseStructureAsync(Guid courseId, string importText);
    Task<List<CourseCheckpointDefinition>> GetCheckpointDefinitionsForEditingAsync(Guid courseId);
    Task<CourseCheckpointDefinition> SaveCheckpointDefinitionAsync(CheckpointDefinitionEditorModel editor);
    Task DeleteCheckpointDefinitionAsync(Guid checkpointDefinitionId);
    Task<List<Course>> GetCoursesForInstructorAsync(Guid instructorId);
    Task<bool> SubmitForReviewAsync(Guid courseId, Guid instructorId);
    Task<bool> ApproveCourseAsync(Guid courseId, Guid adminUserId);
    Task<bool> RequestChangesAsync(Guid courseId, Guid adminUserId, string note);
    Task<bool> RejectCourseAsync(Guid courseId, Guid adminUserId, string note);
}

public sealed class CheckpointDefinitionEditorModel
{
    public Guid? Id { get; set; }
    public Guid CourseId { get; set; }
    public Guid? ModuleId { get; set; }
    public bool IsFinal { get; set; }
    public string Title { get; set; } = "Checkpoint";
    public string Prompt { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<CheckpointOptionEditorItem> Options { get; set; } = new();
}

public sealed class CheckpointOptionEditorItem
{
    public Guid? Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
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
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
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

    public async Task<List<Course>> GetCoursesForInstructorAsync(Guid instructorId)
    {
        return await _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
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
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
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

    public async Task ImportCourseStructureAsync(Guid courseId, string importText)
    {
        if (string.IsNullOrWhiteSpace(importText))
        {
            return;
        }

        var course = await _dbContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (course is null)
        {
            return;
        }

        var parsedImport = ParseImportText(importText);
        if (parsedImport.Modules.Count == 0 && parsedImport.Assessments.Count == 0 && parsedImport.Checkpoints.Count == 0)
        {
            return;
        }

        foreach (var module in course.Modules.ToList())
        {
            _dbContext.Modules.Remove(module);
        }

        var existingAssessments = await _dbContext.CourseAssessments
            .Where(assessment => assessment.CourseId == courseId)
            .ToListAsync();
        if (existingAssessments.Count > 0)
        {
            _dbContext.CourseAssessments.RemoveRange(existingAssessments);
        }

        var existingCheckpointDefinitions = await _dbContext.CourseCheckpointDefinitions
            .Where(definition => definition.CourseId == courseId)
            .ToListAsync();
        if (existingCheckpointDefinitions.Count > 0)
        {
            _dbContext.CourseCheckpointDefinitions.RemoveRange(existingCheckpointDefinitions);
        }

        await _dbContext.SaveChangesAsync();

        var createdModules = new List<Module>();
        foreach (var parsedModule in parsedImport.Modules)
        {
            var moduleEntity = new Module
            {
                CourseId = courseId,
                Title = parsedModule.Title,
                OrderIndex = parsedModule.OrderIndex
            };

            _dbContext.Modules.Add(moduleEntity);
            await _dbContext.SaveChangesAsync();

            createdModules.Add(moduleEntity);

            foreach (var parsedLesson in parsedModule.Lessons)
            {
                _dbContext.Lessons.Add(new Lesson
                {
                    ModuleId = moduleEntity.Id,
                    Title = parsedLesson.Title,
                    TextContent = parsedLesson.TextContent,
                    DurationMinutes = parsedLesson.DurationMinutes,
                    IsRequired = parsedLesson.IsRequired,
                    OrderIndex = parsedLesson.OrderIndex,
                    ContentType = "Text"
                });
            }
        }

        if (parsedImport.Checkpoints.Count > 0)
        {
            foreach (var parsedCheckpoint in parsedImport.Checkpoints)
            {
                var moduleId = parsedCheckpoint.ModuleTitle is null
                    ? null
                    : createdModules.FirstOrDefault(module => string.Equals(module.Title, parsedCheckpoint.ModuleTitle, StringComparison.OrdinalIgnoreCase))?.Id;

                var definitionEntity = new CourseCheckpointDefinition
                {
                    CourseId = courseId,
                    Key = parsedCheckpoint.IsFinal ? ModuleCheckpointService.FinalCheckpointKey : (moduleId is null ? ToCheckpointKey(parsedCheckpoint.Title) : ModuleCheckpointService.BuildModuleCheckpointKey(moduleId.Value)),
                    Title = parsedCheckpoint.Title,
                    Prompt = parsedCheckpoint.Prompt,
                    Description = parsedCheckpoint.Description,
                    ModuleId = moduleId,
                    IsFinal = parsedCheckpoint.IsFinal
                };

                _dbContext.CourseCheckpointDefinitions.Add(definitionEntity);

                for (var index = 0; index < parsedCheckpoint.Options.Count; index++)
                {
                    var option = parsedCheckpoint.Options[index];
                    definitionEntity.Options.Add(new CourseCheckpointOption
                    {
                        Key = string.IsNullOrWhiteSpace(option.Key) ? ToCheckpointOptionKey(option.Label, index) : option.Key,
                        Label = option.Label,
                        IsCorrect = option.IsCorrect,
                        OrderIndex = index + 1
                    });
                }
            }
        }

        foreach (var parsedAssessment in parsedImport.Assessments)
        {
            var module = parsedAssessment.ModuleTitle is null
                ? null
                : createdModules.FirstOrDefault(candidate => string.Equals(candidate.Title, parsedAssessment.ModuleTitle, StringComparison.OrdinalIgnoreCase));

            if (parsedAssessment.ModuleTitle is not null && module is null)
            {
                throw new InvalidOperationException($"Assessment '{parsedAssessment.Title}' references unknown unit '{parsedAssessment.ModuleTitle}'.");
            }

            var assessmentEntity = new CourseAssessment
            {
                CourseId = courseId,
                ModuleId = module?.Id,
                Title = parsedAssessment.Title,
                PassPercent = parsedAssessment.PassPercent,
                IsRequired = true
            };

            _dbContext.CourseAssessments.Add(assessmentEntity);

            foreach (var parsedQuestion in parsedAssessment.Questions)
            {
                assessmentEntity.Questions.Add(new AssessmentQuestion
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

    public async Task<List<CourseCheckpointDefinition>> GetCheckpointDefinitionsForEditingAsync(Guid courseId)
    {
        return await _dbContext.CourseCheckpointDefinitions
            .AsNoTracking()
            .Include(definition => definition.Options)
            .Where(definition => definition.CourseId == courseId)
            .OrderBy(definition => definition.ModuleId == null ? 1 : 0)
            .ThenBy(definition => definition.Title)
            .ToListAsync();
    }

    public async Task<CourseCheckpointDefinition> SaveCheckpointDefinitionAsync(CheckpointDefinitionEditorModel editor)
    {
        var entity = editor.Id.HasValue
            ? await _dbContext.CourseCheckpointDefinitions
                .Include(definition => definition.Options)
                .FirstOrDefaultAsync(definition => definition.Id == editor.Id.Value)
            : null;

        if (entity is null)
        {
            entity = new CourseCheckpointDefinition
            {
                CourseId = editor.CourseId
            };
            _dbContext.CourseCheckpointDefinitions.Add(entity);
        }

        entity.Title = editor.Title.Trim();
        entity.Prompt = editor.Prompt.Trim();
        entity.Description = editor.Description?.Trim() ?? string.Empty;
        entity.IsFinal = editor.IsFinal;
        entity.ModuleId = editor.IsFinal ? null : editor.ModuleId;
        entity.Key = editor.IsFinal
            ? ModuleCheckpointService.FinalCheckpointKey
            : (entity.ModuleId is null ? ToCheckpointKey(entity.Title) : ModuleCheckpointService.BuildModuleCheckpointKey(entity.ModuleId.Value));

        var keepOptionIds = new HashSet<Guid>();
        for (var index = 0; index < editor.Options.Count; index++)
        {
            var optionInput = editor.Options[index];
            var optionEntity = optionInput.Id.HasValue
                ? entity.Options.FirstOrDefault(option => option.Id == optionInput.Id.Value)
                : null;

            if (optionEntity is null)
            {
                optionEntity = new CourseCheckpointOption();
                entity.Options.Add(optionEntity);
            }

            optionEntity.Label = optionInput.Label.Trim();
            optionEntity.IsCorrect = optionInput.IsCorrect;
            optionEntity.OrderIndex = index + 1;
            if (string.IsNullOrWhiteSpace(optionEntity.Key))
            {
                optionEntity.Key = ToCheckpointOptionKey(optionEntity.Label, index);
            }

            keepOptionIds.Add(optionEntity.Id);
        }

        foreach (var staleOption in entity.Options.Where(option => !keepOptionIds.Contains(option.Id)).ToList())
        {
            entity.Options.Remove(staleOption);
            _dbContext.CourseCheckpointOptions.Remove(staleOption);
        }

        await _dbContext.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteCheckpointDefinitionAsync(Guid checkpointDefinitionId)
    {
        var entity = await _dbContext.CourseCheckpointDefinitions.FindAsync(checkpointDefinitionId);
        if (entity is null)
        {
            return;
        }

        _dbContext.CourseCheckpointDefinitions.Remove(entity);
        await _dbContext.SaveChangesAsync();
    }

    private static ParsedImportDocument ParseImportText(string importText)
    {
        var document = new ParsedImportDocument();
        ParsedModule? currentModule = null;
        var currentLessonText = new List<string>();
        ParsedLesson? currentLesson = null;
        ParsedCheckpointDefinition? currentCheckpoint = null;
        ParsedAssessmentDefinition? currentAssessment = null;

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

            if (line.StartsWith("[unit]", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("[module]", StringComparison.OrdinalIgnoreCase))
            {
                var markerLength = line.StartsWith("[unit]", StringComparison.OrdinalIgnoreCase) ? "[unit]".Length : "[module]".Length;
                if (currentLesson is not null && currentModule is not null)
                {
                    currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
                    currentLessonText.Clear();
                    currentModule.Lessons.Add(currentLesson);
                }

                currentLesson = null;
                currentLessonText.Clear();
                currentCheckpoint = null;
                currentAssessment = null;

                currentModule = new ParsedModule
                {
                    Title = line[markerLength..].Trim(),
                    OrderIndex = document.Modules.Count + 1,
                    Lessons = new List<ParsedLesson>()
                };
                document.Modules.Add(currentModule);
                continue;
            }

            if (line.StartsWith("[lesson]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentLesson is not null && currentModule is not null)
                {
                    currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
                    currentLessonText.Clear();
                    currentModule.Lessons.Add(currentLesson);
                }

                currentLesson = ParseLessonDefinition(line);
                currentCheckpoint = null;
                currentAssessment = null;
                if (currentModule is null)
                {
                    currentModule = new ParsedModule
                    {
                        Title = "Module 1",
                        OrderIndex = document.Modules.Count + 1,
                        Lessons = new List<ParsedLesson>()
                    };
                    document.Modules.Add(currentModule);
                }

                currentLessonText.Clear();
                continue;
            }

            if (line.StartsWith("[checkpoint]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentLesson is not null && currentModule is not null)
                {
                    currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
                    currentLessonText.Clear();
                    currentModule.Lessons.Add(currentLesson);
                }

                currentLesson = null;
                currentLessonText.Clear();
                currentAssessment = null;
                currentCheckpoint = new ParsedCheckpointDefinition
                {
                    Title = "Checkpoint",
                    Prompt = "Select every correct answer.",
                    Description = string.Empty,
                    ModuleTitle = line["[checkpoint]".Length..].Trim(),
                    IsFinal = string.Equals(line["[checkpoint]".Length..].Trim(), "final", StringComparison.OrdinalIgnoreCase)
                };
                document.Checkpoints.Add(currentCheckpoint);
                continue;
            }

            if (line.StartsWith("[option]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentCheckpoint is null)
                {
                    currentCheckpoint = new ParsedCheckpointDefinition { Title = "Checkpoint", Prompt = "Select every correct answer.", Description = string.Empty };
                    document.Checkpoints.Add(currentCheckpoint);
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

                currentCheckpoint.Options.Add(new ParsedCheckpointOption
                {
                    Label = label,
                    IsCorrect = isCorrect,
                    Key = string.Empty
                });
                continue;
            }

            if (line.StartsWith("[assessment]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentLesson is not null && currentModule is not null)
                {
                    currentLesson.TextContent = string.Join("\n", currentLessonText).Trim();
                    currentLessonText.Clear();
                    currentModule.Lessons.Add(currentLesson);
                }

                currentLesson = null;
                currentLessonText.Clear();
                currentCheckpoint = null;
                var assessmentParts = line["[assessment]".Length..].Trim()
                    .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                currentAssessment = new ParsedAssessmentDefinition
                {
                    Title = assessmentParts.FirstOrDefault() ?? "Final Assessment"
                };
                foreach (var part in assessmentParts.Skip(1))
                {
                    var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (keyValue.Length == 2 && (string.Equals(keyValue[0], "unit", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(keyValue[0], "module", StringComparison.OrdinalIgnoreCase)))
                    {
                        currentAssessment.ModuleTitle = keyValue[1];
                    }
                    else if (keyValue.Length == 2 && string.Equals(keyValue[0], "passpercent", StringComparison.OrdinalIgnoreCase)
                        && decimal.TryParse(keyValue[1], out var passPercent))
                    {
                        currentAssessment.PassPercent = passPercent;
                    }
                }
                document.Assessments.Add(currentAssessment);
                continue;
            }

            if (line.StartsWith("[question]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentAssessment is null)
                {
                    currentAssessment = new ParsedAssessmentDefinition();
                    document.Assessments.Add(currentAssessment);
                }

                var parsedQuestion = ParseAssessmentQuestionDefinition(line);
                parsedQuestion.OrderIndex = currentAssessment.Questions.Count + 1;
                currentAssessment.Questions.Add(parsedQuestion);
                continue;
            }

            if (currentCheckpoint is not null)
            {
                ParseCheckpointMetadata(line, currentCheckpoint);
                continue;
            }

            if (currentAssessment is not null)
            {
                ParseAssessmentMetadata(line, currentAssessment);
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
            currentModule?.Lessons.Add(currentLesson);
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

    private static void ParseCheckpointMetadata(string line, ParsedCheckpointDefinition checkpoint)
    {
        var keyValue = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (keyValue.Length != 2)
        {
            return;
        }

        switch (keyValue[0].ToLowerInvariant())
        {
            case "title":
                checkpoint.Title = keyValue[1];
                break;
            case "prompt":
                checkpoint.Prompt = keyValue[1];
                break;
            case "description":
                checkpoint.Description = keyValue[1];
                break;
            case "unit":
            case "module":
                checkpoint.ModuleTitle = keyValue[1];
                break;
            case "isfinal":
                bool.TryParse(keyValue[1], out var isFinal);
                checkpoint.IsFinal = isFinal;
                break;
        }
    }

    private static void ParseAssessmentMetadata(string line, ParsedAssessmentDefinition assessment)
    {
        var keyValue = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (keyValue.Length != 2)
        {
            return;
        }

        switch (keyValue[0].ToLowerInvariant())
        {
            case "title":
                assessment.Title = keyValue[1];
                break;
            case "passpercent":
                if (decimal.TryParse(keyValue[1], out var passPercent))
                {
                    assessment.PassPercent = passPercent;
                }
                break;
            case "unit":
            case "module":
                assessment.ModuleTitle = keyValue[1];
                break;
        }
    }

    private static ParsedAssessmentQuestion ParseAssessmentQuestionDefinition(string line)
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

        return new ParsedAssessmentQuestion
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

    private static string ToCheckpointKey(string label)
    {
        var cleaned = new string(label.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "checkpoint" : cleaned.ToLowerInvariant();
    }

    private static string ToCheckpointOptionKey(string label, int index)
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

    private sealed class ParsedImportDocument
    {
        public List<ParsedModule> Modules { get; } = new();
        public List<ParsedCheckpointDefinition> Checkpoints { get; } = new();
        public List<ParsedAssessmentDefinition> Assessments { get; } = new();
    }

    private sealed class ParsedModule
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

    private sealed class ParsedCheckpointDefinition
    {
        public string Title { get; set; } = "Checkpoint";
        public string Prompt { get; set; } = "Select every correct answer.";
        public string Description { get; set; } = string.Empty;
        public string? ModuleTitle { get; set; }
        public bool IsFinal { get; set; }
        public List<ParsedCheckpointOption> Options { get; } = new();
    }

    private sealed class ParsedCheckpointOption
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    private sealed class ParsedAssessmentDefinition
    {
        public string Title { get; set; } = "Final Assessment";
        public decimal PassPercent { get; set; } = 80m;
        public string? ModuleTitle { get; set; }
        public List<ParsedAssessmentQuestion> Questions { get; } = new();
    }

    private sealed class ParsedAssessmentQuestion
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
