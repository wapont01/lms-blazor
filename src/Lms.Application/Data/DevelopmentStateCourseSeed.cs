using System.Security.Cryptography;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class DevelopmentStateCourseSeed
{
    public static async Task<int> SeedAsync(ApplicationDbContext db)
    {
        var sources = await db.Courses.AsNoTracking()
            .Where(course => !course.Slug.StartsWith(DevelopmentCourseCatalog.SlugPrefix))
            .Include(course => course.Units).ThenInclude(unit => unit.Lessons)
            .Include(course => course.Exams).ThenInclude(exam => exam.Questions)
            .AsSplitQuery().ToListAsync();
        var definitions = await db.CourseUnitExamDefinitions.AsNoTracking()
            .Include(exam => exam.Options).ToListAsync();
        var existingIds = (await db.Courses.Select(course => course.Id).ToListAsync()).ToHashSet();
        var created = 0;

        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var source in sources)
        {
            foreach (var state in DevelopmentCourseCatalog.States)
            {
                if (string.Equals(source.State?.Trim(), state, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var slug = $"{DevelopmentCourseCatalog.SlugPrefix}{source.Id:N}-{state.ToLowerInvariant().Replace(' ', '-')}";
                var id = StableId(slug);
                if (existingIds.Contains(id))
                {
                    continue;
                }

                var copy = CopyScalars(db, source);
                copy.Id = id;
                copy.Slug = slug;
                copy.State = state;
                copy.Description = $"Development sample for {state}. Content copied from {source.State ?? "an existing course"}; not state-approved or eligible for credit. {source.Description}";
                copy.IsPublished = false;
                copy.IsArchived = false;
                copy.Price = 0;
                copy.CommissionCourseNumber = null;
                copy.ReviewStatus = CourseReviewStatuses.Draft;
                copy.ReviewNote = null;
                copy.ReviewedAt = null;
                copy.ReviewedByUserId = null;
                copy.StartsAtUtc = null;
                copy.EnrollmentClosesAtUtc = null;
                copy.CreatedAt = DateTime.UtcNow;
                copy.UpdatedAt = null;
                var unitIds = new Dictionary<Guid, Guid>();
                var lessonIds = new Dictionary<Guid, Guid>();
                foreach (var unit in source.Units)
                {
                    var unitCopy = CopyScalars(db, unit);
                    unitCopy.Id = StableId($"{slug}:unit:{unit.Id}");
                    unitCopy.CourseId = id;
                    unitIds.Add(unit.Id, unitCopy.Id);
                    foreach (var lesson in unit.Lessons)
                    {
                        var lessonCopy = CopyScalars(db, lesson);
                        lessonCopy.Id = StableId($"{slug}:lesson:{lesson.Id}");
                        lessonCopy.UnitId = unitCopy.Id;
                        lessonIds.Add(lesson.Id, lessonCopy.Id);
                        unitCopy.Lessons.Add(lessonCopy);
                    }
                    copy.Units.Add(unitCopy);
                }
                foreach (var exam in source.Exams)
                {
                    var examCopy = CopyScalars(db, exam);
                    examCopy.Id = StableId($"{slug}:exam:{exam.Id}");
                    examCopy.CourseId = id;
                    examCopy.UnitId = exam.UnitId.HasValue ? unitIds[exam.UnitId.Value] : null;
                    foreach (var question in exam.Questions)
                    {
                        var questionCopy = CopyScalars(db, question);
                        questionCopy.Id = StableId($"{slug}:question:{question.Id}");
                        questionCopy.CourseExamId = examCopy.Id;
                        examCopy.Questions.Add(questionCopy);
                    }
                    copy.Exams.Add(examCopy);
                }
                db.Courses.Add(copy);
                foreach (var definition in definitions.Where(exam => exam.CourseId == source.Id))
                {
                    var definitionCopy = CopyScalars(db, definition);
                    definitionCopy.Id = StableId($"{slug}:unit-exam:{definition.Id}");
                    definitionCopy.CourseId = id;
                    definitionCopy.UnitId = definition.UnitId.HasValue ? unitIds[definition.UnitId.Value] : null;
                    definitionCopy.LessonId = definition.LessonId switch
                    {
                        null => null,
                        var lessonId when lessonId == Guid.Empty => Guid.Empty,
                        var lessonId => lessonIds[lessonId.Value]
                    };
                    foreach (var option in definition.Options)
                    {
                        var optionCopy = CopyScalars(db, option);
                        optionCopy.Id = StableId($"{slug}:option:{option.Id}");
                        optionCopy.CourseUnitExamDefinitionId = definitionCopy.Id;
                        definitionCopy.Options.Add(optionCopy);
                    }
                    db.CourseUnitExamDefinitions.Add(definitionCopy);
                }
                existingIds.Add(id);
                created++;
            }
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return created;
    }

    private static T CopyScalars<T>(ApplicationDbContext db, T source) where T : class =>
        (T)db.Entry(source).CurrentValues.ToObject();

    private static Guid StableId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
