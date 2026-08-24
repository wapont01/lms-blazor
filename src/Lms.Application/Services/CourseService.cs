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
    Task<bool> DeleteModuleAsync(Guid moduleId);
    Task<Lesson> AddLessonAsync(Guid moduleId, Lesson lesson);
    Task UpdateLessonAsync(Lesson lesson);
    Task DeleteLessonAsync(Guid lessonId);
    Task MoveModuleAsync(Guid moduleId, bool moveUp);
    Task MoveModuleToPositionAsync(Guid moduleId, int targetPosition);
    Task MoveLessonAsync(Guid lessonId, bool moveUp);
    Task MoveLessonToPositionAsync(Guid lessonId, int targetPosition);
    Task MoveCheckpointAsync(Guid checkpointId, bool moveUp);
    // Jumps a checkpoint to an absolute position among ALL of a module's lessons + checkpoints, re-anchoring it as needed.
    Task MoveCheckpointToPositionAsync(Guid checkpointId, int targetPosition);
    Task ImportCourseStructureAsync(Guid courseId, string importText);
    Task<List<CourseCheckpointDefinition>> GetCheckpointDefinitionsForEditingAsync(Guid courseId);
    Task<CourseCheckpointDefinition> SaveCheckpointDefinitionAsync(CheckpointDefinitionEditorModel editor);
    Task<bool> SetCheckpointGatesProgressionAsync(Guid checkpointDefinitionId, bool gatesProgression);
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
    public Guid? LessonId { get; set; }
    public bool GatesProgression { get; set; }
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
        ApplyComplianceDefaults(course);
        ValidateComplianceConfiguration(course);
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

        ApplyComplianceDefaults(course);
        ValidateComplianceConfiguration(course);
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
        var course = await _dbContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
            .Include(course => course.Assessments)
                .ThenInclude(assessment => assessment.Questions)
            .FirstOrDefaultAsync(course => course.Id == courseId);
        if (course is null || course.OwnerInstructorId != instructorId)
        {
            return false;
        }

        if (course.ReviewStatus != CourseReviewStatuses.Draft && course.ReviewStatus != CourseReviewStatuses.ChangesRequested && course.ReviewStatus != CourseReviewStatuses.Rejected)
        {
            return false;
        }

        if (course.ReviewStatus == CourseReviewStatuses.Rejected && !CourseReviewStatuses.CanResubmitAfterRejection(course.ReviewedAt, DateTime.UtcNow))
        {
            return false;
        }

        var checkpoints = await _dbContext.CourseCheckpointDefinitions
            .Include(checkpoint => checkpoint.Options)
            .Where(checkpoint => checkpoint.CourseId == courseId)
            .ToListAsync();

        if (!CourseAuthoringProgressHelper.Evaluate(course, checkpoints).Items.All(item => item.IsComplete))
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
        var course = await _dbContext.Courses.FirstOrDefaultAsync(course => course.Id == courseId);
        if (course is null)
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

    public async Task<bool> DeleteModuleAsync(Guid moduleId)
    {
        var module = await _dbContext.Modules.FirstOrDefaultAsync(existing => existing.Id == moduleId);
        if (module is null)
        {
            return true;
        }

        // Guard: only allow deleting an empty module so lessons/checkpoints are never silently orphaned.
        var hasLessons = await _dbContext.Lessons.AnyAsync(lesson => lesson.ModuleId == moduleId);
        var hasCheckpoints = await _dbContext.CourseCheckpointDefinitions.AnyAsync(checkpoint => checkpoint.ModuleId == moduleId);
        if (hasLessons || hasCheckpoints)
        {
            return false;
        }

        var courseId = module.CourseId;
        _dbContext.Modules.Remove(module);
        await _dbContext.SaveChangesAsync();
        await RenumberModulesAsync(courseId);
        return true;
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

    public async Task DeleteLessonAsync(Guid lessonId)
    {
        var lesson = await _dbContext.Lessons.FirstOrDefaultAsync(existing => existing.Id == lessonId);
        if (lesson is null)
        {
            return;
        }

        // LessonId is a plain scalar anchor (no FK), so checkpoints pointing here would otherwise
        // dangle; fall them back to "end of module" instead of leaving them inert.
        var anchoredCheckpoints = await _dbContext.CourseCheckpointDefinitions
            .Where(checkpoint => checkpoint.LessonId == lessonId)
            .ToListAsync();
        foreach (var checkpoint in anchoredCheckpoints)
        {
            checkpoint.LessonId = null;
        }

        var moduleId = lesson.ModuleId;
        _dbContext.Lessons.Remove(lesson);
        await _dbContext.SaveChangesAsync();
        await RenumberLessonsAsync(moduleId);
    }

    public async Task MoveModuleAsync(Guid moduleId, bool moveUp)
    {
        var module = await _dbContext.Modules.FirstOrDefaultAsync(existing => existing.Id == moduleId);
        if (module is null)
        {
            return;
        }

        var siblings = await _dbContext.Modules
            .Where(existing => existing.CourseId == module.CourseId)
            .OrderBy(existing => existing.OrderIndex)
            .ToListAsync();

        var index = siblings.FindIndex(existing => existing.Id == moduleId);
        var swapIndex = moveUp ? index - 1 : index + 1;
        if (index < 0 || swapIndex < 0 || swapIndex >= siblings.Count)
        {
            return;
        }

        (siblings[index].OrderIndex, siblings[swapIndex].OrderIndex) = (siblings[swapIndex].OrderIndex, siblings[index].OrderIndex);
        await _dbContext.SaveChangesAsync();
    }

    // Jumps a module directly to a 1-based target position (rather than swapping one step at a time),
    // re-sequencing every sibling's OrderIndex to a clean 1..N run in the new order.
    public async Task MoveModuleToPositionAsync(Guid moduleId, int targetPosition)
    {
        var module = await _dbContext.Modules.FirstOrDefaultAsync(existing => existing.Id == moduleId);
        if (module is null)
        {
            return;
        }

        var siblings = await _dbContext.Modules
            .Where(existing => existing.CourseId == module.CourseId)
            .OrderBy(existing => existing.OrderIndex)
            .ToListAsync();

        var currentIndex = siblings.FindIndex(existing => existing.Id == moduleId);
        var targetIndex = targetPosition - 1;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= siblings.Count || targetIndex == currentIndex)
        {
            return;
        }

        siblings.RemoveAt(currentIndex);
        siblings.Insert(targetIndex, module);
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].OrderIndex = i + 1;
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task MoveLessonAsync(Guid lessonId, bool moveUp)
    {
        var lesson = await _dbContext.Lessons.FirstOrDefaultAsync(existing => existing.Id == lessonId);
        if (lesson is null)
        {
            return;
        }

        var siblings = await _dbContext.Lessons
            .Where(existing => existing.ModuleId == lesson.ModuleId)
            .OrderBy(existing => existing.OrderIndex)
            .ToListAsync();

        var index = siblings.FindIndex(existing => existing.Id == lessonId);
        var swapIndex = moveUp ? index - 1 : index + 1;
        if (index < 0 || swapIndex < 0 || swapIndex >= siblings.Count)
        {
            return;
        }

        // Swapping OrderIndex only (lessons keep their own identity/id): any checkpoints anchored to
        // either lesson stay anchored to the same LessonId, so they naturally travel with their lesson.
        (siblings[index].OrderIndex, siblings[swapIndex].OrderIndex) = (siblings[swapIndex].OrderIndex, siblings[index].OrderIndex);
        await _dbContext.SaveChangesAsync();
    }

    // Jumps a lesson directly to a 1-based target position within its module (rather than swapping one
    // step at a time), re-sequencing every sibling's OrderIndex to a clean 1..N run in the new order.
    // Any checkpoints anchored to a lesson stay anchored by LessonId, so they travel with it automatically.
    // targetPosition is a whole-module position (same numbering as the module page's "Move to position"
    // dropdown, over lessons + checkpoints combined), not a lesson-only index — checkpoints keep anchoring
    // to their referenced lesson's Id, so they don't need any adjustment when lessons are reordered.
    public async Task MoveLessonToPositionAsync(Guid lessonId, int targetPosition)
    {
        var lesson = await _dbContext.Lessons.FirstOrDefaultAsync(existing => existing.Id == lessonId);
        if (lesson is null)
        {
            return;
        }

        var lessons = await _dbContext.Lessons
            .Where(existing => existing.ModuleId == lesson.ModuleId)
            .OrderBy(existing => existing.OrderIndex)
            .ToListAsync();

        var allModuleCheckpoints = await _dbContext.CourseCheckpointDefinitions
            .Where(existing => existing.ModuleId == lesson.ModuleId)
            .ToListAsync();

        var sequence = BuildModuleSequence(lessons, allModuleCheckpoints);
        var positionableCount = sequence.Count(item => item is not ModuleEndMarker);
        var currentSequenceIndex = sequence.FindIndex(item => item is Lesson existingLesson && existingLesson.Id == lessonId);
        var currentPositionableIndex = sequence.Take(currentSequenceIndex).Count(item => item is not ModuleEndMarker);
        var targetPositionableIndex = targetPosition - 1;

        if (currentSequenceIndex < 0 || targetPositionableIndex < 0 || targetPositionableIndex >= positionableCount || targetPositionableIndex == currentPositionableIndex)
        {
            return;
        }

        sequence.RemoveAt(currentSequenceIndex);
        var insertSequenceIndex = PositionableIndexToSequenceIndex(sequence, targetPositionableIndex);
        sequence.Insert(Math.Min(insertSequenceIndex, sequence.Count), lesson);

        var orderIndex = 0;
        foreach (var item in sequence)
        {
            if (item is Lesson sequencedLesson)
            {
                orderIndex++;
                sequencedLesson.OrderIndex = orderIndex;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    // Checkpoints don't have a single global OrderIndex like modules/lessons: their display position is
    // "anchor lesson (or end-of-module) + OrderIndex among siblings sharing that same anchor". Moving a
    // checkpoint past the edge of its sibling group re-anchors it to the neighboring lesson slot instead,
    // which is how it crosses a lesson boundary in the flattened, learner-facing sequence.
    public async Task MoveCheckpointAsync(Guid checkpointId, bool moveUp)
    {
        var checkpoint = await _dbContext.CourseCheckpointDefinitions.FirstOrDefaultAsync(existing => existing.Id == checkpointId);
        if (checkpoint is null || checkpoint.ModuleId is null)
        {
            // The final (course-level) checkpoint has no module/lesson anchor to reorder against.
            return;
        }

        var moduleId = checkpoint.ModuleId.Value;

        var lessons = await _dbContext.Lessons
            .Where(lesson => lesson.ModuleId == moduleId)
            .OrderBy(lesson => lesson.OrderIndex)
            .ToListAsync();

        // Anchor slots in flattened order: a virtual "start of module" slot first, then each lesson in
        // turn, then a virtual "end of module" slot last.
        var anchorSlots = new List<Guid?> { CourseCheckpointDefinition.StartOfModuleAnchor };
        anchorSlots.AddRange(lessons.Select(lesson => (Guid?)lesson.Id));
        anchorSlots.Add(null);
        var currentAnchorIndex = anchorSlots.FindIndex(anchor => anchor == checkpoint.LessonId);
        if (currentAnchorIndex < 0)
        {
            return;
        }

        var allModuleCheckpoints = await _dbContext.CourseCheckpointDefinitions
            .Where(existing => existing.ModuleId == moduleId)
            .ToListAsync();

        var group = allModuleCheckpoints
            .Where(existing => existing.LessonId == checkpoint.LessonId)
            .OrderBy(existing => existing.OrderIndex)
            .ThenBy(existing => existing.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var positionInGroup = group.FindIndex(existing => existing.Id == checkpointId);

        if (moveUp)
        {
            if (positionInGroup > 0)
            {
                var sibling = group[positionInGroup - 1];
                (checkpoint.OrderIndex, sibling.OrderIndex) = (sibling.OrderIndex, checkpoint.OrderIndex);
            }
            else if (currentAnchorIndex > 0)
            {
                var targetAnchor = anchorSlots[currentAnchorIndex - 1];
                var targetGroup = allModuleCheckpoints.Where(existing => existing.LessonId == targetAnchor).ToList();
                var maxOrder = targetGroup.Count > 0 ? targetGroup.Max(existing => existing.OrderIndex) : 0;
                checkpoint.LessonId = targetAnchor;
                checkpoint.OrderIndex = maxOrder + 1;
            }
        }
        else
        {
            if (positionInGroup < group.Count - 1)
            {
                var sibling = group[positionInGroup + 1];
                (checkpoint.OrderIndex, sibling.OrderIndex) = (sibling.OrderIndex, checkpoint.OrderIndex);
            }
            else if (currentAnchorIndex < anchorSlots.Count - 1)
            {
                var targetAnchor = anchorSlots[currentAnchorIndex + 1];
                var targetGroup = allModuleCheckpoints.Where(existing => existing.LessonId == targetAnchor).ToList();
                var minOrder = targetGroup.Count > 0 ? targetGroup.Min(existing => existing.OrderIndex) : 0;
                checkpoint.LessonId = targetAnchor;
                checkpoint.OrderIndex = minOrder - 1;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    // Reorders a checkpoint by jumping it to an absolute position among ALL of the module's lessons and
    // checkpoints combined (the same numbering the module page's "Move to position" dropdown shows), then
    // re-derives its anchor (start-of-module, a specific lesson, or end-of-module) and OrderIndex from
    // wherever it lands in that flattened sequence — this is how the dropdown re-anchors a checkpoint
    // across a lesson boundary in a single jump, instead of one step at a time like the ↑/↓ arrows.
    public async Task MoveCheckpointToPositionAsync(Guid checkpointId, int targetPosition)
    {
        var checkpoint = await _dbContext.CourseCheckpointDefinitions.FirstOrDefaultAsync(existing => existing.Id == checkpointId);
        if (checkpoint is null || checkpoint.ModuleId is null)
        {
            return;
        }

        var moduleId = checkpoint.ModuleId.Value;

        var lessons = await _dbContext.Lessons
            .Where(lesson => lesson.ModuleId == moduleId)
            .OrderBy(lesson => lesson.OrderIndex)
            .ToListAsync();

        var allModuleCheckpoints = await _dbContext.CourseCheckpointDefinitions
            .Where(existing => existing.ModuleId == moduleId)
            .ToListAsync();

        var sequence = BuildModuleSequence(lessons, allModuleCheckpoints);

        // ModuleEndMarker isn't a real learner-facing item, so it's excluded from position numbering.
        var positionableCount = sequence.Count(item => item is not ModuleEndMarker);
        var currentSequenceIndex = sequence.FindIndex(item => item is CourseCheckpointDefinition existing && existing.Id == checkpointId);
        var currentPositionableIndex = sequence.Take(currentSequenceIndex).Count(item => item is not ModuleEndMarker);
        var targetPositionableIndex = targetPosition - 1;

        if (currentSequenceIndex < 0 || targetPositionableIndex < 0 || targetPositionableIndex >= positionableCount || targetPositionableIndex == currentPositionableIndex)
        {
            return;
        }

        // Work out where the checkpoint lands by counting positions in the sequence with the checkpoint
        // itself removed first, so the target index isn't thrown off by the checkpoint's own old slot.
        sequence.RemoveAt(currentSequenceIndex);
        var markerIndex = sequence.FindIndex(item => item is ModuleEndMarker);
        var insertSequenceIndex = PositionableIndexToSequenceIndex(sequence, targetPositionableIndex);

        // Landing exactly on the boundary between "anchored to the last lesson" and "end of module" is
        // ambiguous — default to true end-of-module (null anchor), matching what an instructor jumping a
        // checkpoint to the last slot would expect, rather than silently anchoring it to the last lesson.
        if (insertSequenceIndex == markerIndex)
        {
            insertSequenceIndex++;
        }

        sequence.Insert(Math.Min(insertSequenceIndex, sequence.Count), checkpoint);

        // Re-derive every checkpoint's anchor and OrderIndex among its new siblings from its place in the sequence.
        Guid? anchor = CourseCheckpointDefinition.StartOfModuleAnchor;
        var orderInAnchor = 0;
        foreach (var item in sequence)
        {
            if (item is Lesson lesson)
            {
                anchor = lesson.Id;
                orderInAnchor = 0;
                continue;
            }

            if (item is ModuleEndMarker)
            {
                anchor = null;
                orderInAnchor = 0;
                continue;
            }

            var existingCheckpoint = (CourseCheckpointDefinition)item;
            orderInAnchor++;
            existingCheckpoint.LessonId = anchor;
            existingCheckpoint.OrderIndex = orderInAnchor;
        }

        await _dbContext.SaveChangesAsync();
    }

    // Marker separating "anchored to the last lesson" from "end of module (null anchor)" checkpoints in a
    // flattened module sequence — both groups sit at the tail with nothing else between them, so without an
    // explicit boundary that distinction would be lost when re-deriving anchors after a jump-to-position move.
    private sealed class ModuleEndMarker
    {
    }

    // Flattens a module's lessons and checkpoints into the single learner-facing sequence (start-of-module
    // checkpoints, lesson, its anchored checkpoints, next lesson, ..., end-of-module checkpoints last),
    // matching the module page's display/numbering order.
    private static List<object> BuildModuleSequence(List<Lesson> lessons, List<CourseCheckpointDefinition> checkpoints)
    {
        var ordered = checkpoints
            .OrderBy(existing => existing.LessonId == CourseCheckpointDefinition.StartOfModuleAnchor
                ? -1
                : existing.LessonId.HasValue ? lessons.FindIndex(lesson => lesson.Id == existing.LessonId.Value) : int.MaxValue)
            .ThenBy(existing => existing.OrderIndex)
            .ThenBy(existing => existing.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sequence = new List<object>();
        sequence.AddRange(ordered.Where(existing => existing.LessonId == CourseCheckpointDefinition.StartOfModuleAnchor));

        foreach (var lesson in lessons)
        {
            sequence.Add(lesson);
            sequence.AddRange(ordered.Where(existing => existing.LessonId == lesson.Id));
        }

        sequence.Add(new ModuleEndMarker());
        sequence.AddRange(ordered.Where(existing => existing.LessonId is null));

        return sequence;
    }

    // Maps a 1-based-minus-one "learner-facing" position (over lessons + checkpoints only) to its index in
    // a sequence that also contains the invisible ModuleEndMarker.
    private static int PositionableIndexToSequenceIndex(List<object> sequence, int positionableIndex)
    {
        var seen = 0;
        for (var i = 0; i < sequence.Count; i++)
        {
            if (sequence[i] is ModuleEndMarker)
            {
                continue;
            }

            if (seen == positionableIndex)
            {
                return i;
            }

            seen++;
        }

        return sequence.Count;
    }


    // Reassigns 1..N sequential OrderIndex values (preserving relative order) so deletions never leave gaps or stale numbering.
    private async Task RenumberModulesAsync(Guid courseId)
    {
        var modules = await _dbContext.Modules
            .Where(module => module.CourseId == courseId)
            .OrderBy(module => module.OrderIndex)
            .ToListAsync();

        for (var index = 0; index < modules.Count; index++)
        {
            modules[index].OrderIndex = index + 1;
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task RenumberLessonsAsync(Guid moduleId)
    {
        var lessons = await _dbContext.Lessons
            .Where(lesson => lesson.ModuleId == moduleId)
            .OrderBy(lesson => lesson.OrderIndex)
            .ToListAsync();

        for (var index = 0; index < lessons.Count; index++)
        {
            lessons[index].OrderIndex = index + 1;
        }

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
        if (parsedImport.Modules.Count == 0 && parsedImport.Assessment is null && parsedImport.Checkpoints.Count == 0)
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

                var lessonId = moduleId is null || parsedCheckpoint.LessonTitle is null
                    ? (Guid?)null
                    : createdModules.First(module => module.Id == moduleId.Value).Lessons
                        .FirstOrDefault(lesson => string.Equals(lesson.Title, parsedCheckpoint.LessonTitle, StringComparison.OrdinalIgnoreCase))?.Id;

                var definitionEntity = new CourseCheckpointDefinition
                {
                    CourseId = courseId,
                    Key = moduleId is null ? ToCheckpointKey(parsedCheckpoint.Title) : ModuleCheckpointService.BuildModuleCheckpointKey(moduleId.Value),
                    Title = parsedCheckpoint.Title,
                    Prompt = parsedCheckpoint.Prompt,
                    Description = parsedCheckpoint.Description,
                    ModuleId = moduleId,
                    LessonId = lessonId,
                    GatesProgression = parsedCheckpoint.GatesProgression
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

        if (parsedImport.Assessment is not null)
        {
            var assessmentEntity = new CourseAssessment
            {
                CourseId = courseId,
                Title = parsedImport.Assessment.Title,
                PassPercent = parsedImport.Assessment.PassPercent,
                IsRequired = true
            };

            _dbContext.CourseAssessments.Add(assessmentEntity);

            foreach (var parsedQuestion in parsedImport.Assessment.Questions)
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
            .OrderBy(definition => definition.Title)
            .ToListAsync();
    }

    public async Task<CourseCheckpointDefinition> SaveCheckpointDefinitionAsync(CheckpointDefinitionEditorModel editor)
    {
        var entity = editor.Id.HasValue
            ? await _dbContext.CourseCheckpointDefinitions
                .Include(definition => definition.Options)
                .FirstOrDefaultAsync(definition => definition.Id == editor.Id.Value)
            : null;

        var isNewCheckpoint = entity is null;
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
        entity.GatesProgression = editor.GatesProgression;
        entity.ModuleId = editor.ModuleId;
        entity.LessonId = editor.LessonId;

        // Key is only assigned once, at creation: it doubles as the CourseId+Key uniqueness
        // guard, and modules can now have more than one checkpoint, so re-deriving it from
        // ModuleId on every save would collide with a sibling checkpoint's key.
        if (isNewCheckpoint)
        {
            entity.Key = entity.ModuleId is null
                ? ToCheckpointKey(entity.Title)
                : await BuildUniqueModuleCheckpointKeyAsync(entity.ModuleId.Value, entity.CourseId);
        }

        var keepOptionIds = new HashSet<Guid>();
        for (var index = 0; index < editor.Options.Count; index++)
        {
            var optionInput = editor.Options[index];
            var optionEntity = optionInput.Id.HasValue
                ? entity.Options.FirstOrDefault(option => option.Id == optionInput.Id.Value)
                : null;

            if (optionEntity is null)
            {
                // Id defaults to a non-empty Guid, so EF's graph tracking can't infer this is new;
                // add it to the DbSet explicitly or it gets treated as an update to a missing row.
                optionEntity = new CourseCheckpointOption();
                entity.Options.Add(optionEntity);
                _dbContext.CourseCheckpointOptions.Add(optionEntity);
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

    public async Task<bool> SetCheckpointGatesProgressionAsync(Guid checkpointDefinitionId, bool gatesProgression)
    {
        var entity = await _dbContext.CourseCheckpointDefinitions.FindAsync(checkpointDefinitionId);
        if (entity is null)
        {
            return false;
        }

        entity.GatesProgression = gatesProgression;
        await _dbContext.SaveChangesAsync();
        return true;
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

            if (line.StartsWith("[module]", StringComparison.OrdinalIgnoreCase))
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
                currentAssessment = null;

                currentModule = new ParsedModule
                {
                    Title = line["[module]".Length..].Trim(),
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
                    ModuleTitle = line["[checkpoint]".Length..].Trim()
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
                currentAssessment = new ParsedAssessmentDefinition();
                document.Assessment = currentAssessment;
                continue;
            }

            if (line.StartsWith("[question]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentAssessment is null)
                {
                    currentAssessment = new ParsedAssessmentDefinition();
                    document.Assessment = currentAssessment;
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
            case "module":
                checkpoint.ModuleTitle = keyValue[1];
                break;
            case "lesson":
                checkpoint.LessonTitle = keyValue[1];
                break;
            case "gatesprogression":
                bool.TryParse(keyValue[1], out var gatesProgression);
                checkpoint.GatesProgression = gatesProgression;
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

    private async Task<string> BuildUniqueModuleCheckpointKeyAsync(Guid moduleId, Guid courseId)
    {
        var baseKey = ModuleCheckpointService.BuildModuleCheckpointKey(moduleId);
        var existingKeys = await _dbContext.CourseCheckpointDefinitions
            .Where(definition => definition.CourseId == courseId && definition.ModuleId == moduleId)
            .Select(definition => definition.Key)
            .ToListAsync();

        if (!existingKeys.Contains(baseKey, StringComparer.OrdinalIgnoreCase))
        {
            return baseKey;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseKey}-{suffix}";
            suffix++;
        } while (existingKeys.Contains(candidate, StringComparer.OrdinalIgnoreCase));

        return candidate;
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

    private static void ApplyComplianceDefaults(Course course)
    {
        if (course is null)
        {
            return;
        }

        if (string.Equals(course.ComplianceType, CourseComplianceTypes.ContinuingEducation, StringComparison.OrdinalIgnoreCase))
        {
            if (!ContinuingEducationTypes.IsValid(course.ContinuingEducationType))
            {
                course.ContinuingEducationType = ContinuingEducationTypes.Elective;
            }
        }
        else
        {
            course.ContinuingEducationType = null;
        }

        if (course.IsPrelicensingOrPostlicensing)
        {
            course.RequiresProctoredExam = true;
            course.CompletionWindowDays ??= 180;
        }
        else if (string.Equals(course.ComplianceType, CourseComplianceTypes.ContinuingEducation, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(course.DeliveryMethod, CourseDeliveryMethods.DistanceEducation, StringComparison.OrdinalIgnoreCase))
            {
                course.CompletionWindowDays ??= 30;
            }
        }

        if (!course.UsesUnitBasedStructure)
        {
            return;
        }

        if (course.RequiredInstructionalMinutes <= 0)
        {
            course.RequiredInstructionalMinutes = course.ComplianceType switch
            {
                CourseComplianceTypes.Prelicensing => 4500,
                CourseComplianceTypes.Postlicensing => 1800,
                CourseComplianceTypes.ContinuingEducation => Math.Max(course.CreditHours, 4) * 60,
                _ => Math.Max(course.DurationHours, 1) * 60
            };
        }

        if (course.MinimumPassingPercent <= 0)
        {
            course.MinimumPassingPercent = 75;
        }

        if (course.MinimumAttendancePercent <= 0)
        {
            course.MinimumAttendancePercent = 80;
        }
    }

    private static void ValidateComplianceConfiguration(Course course)
    {
        var validationError = RegulatoryCoursePolicy.ValidateConfiguration(course);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            throw new InvalidOperationException(validationError);
        }
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
        public ParsedAssessmentDefinition? Assessment { get; set; }
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
        public string? LessonTitle { get; set; }
        public bool GatesProgression { get; set; }
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
