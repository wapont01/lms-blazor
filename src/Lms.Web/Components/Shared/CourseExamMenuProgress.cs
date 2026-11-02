using Lms.Application.Services;
using Lms.Domain.Entities;

namespace Lms.Web.Components.Shared;

public static class CourseExamMenuProgress
{
    public static Guid? GetCurrentRequiredExamId(
        Course course,
        IReadOnlyCollection<CourseExamMenuInfo> exams,
        IReadOnlyCollection<ExamAttemptHistoryItem> attempts,
        IReadOnlyDictionary<Guid, bool> lessonCompletion,
        bool menuEnabled)
    {
        if (!menuEnabled)
        {
            return null;
        }

        var currentVersions = exams.ToDictionary(exam => exam.Id, exam => exam.ContentVersion);
        var passedExamIds = attempts
            .Where(attempt => attempt.Passed
                && currentVersions.TryGetValue(attempt.ExamId, out var version)
                && attempt.ExamVersion == version)
            .Select(attempt => attempt.ExamId)
            .ToHashSet();

        foreach (var unit in course.Units.OrderBy(unit => unit.OrderIndex))
        {
            var unitExams = exams
                .Where(exam => exam.UnitId == unit.Id
                    && exam.Kind != CourseExamKinds.Practice
                    && (exam.Kind != CourseExamKinds.Standard || exam.IsRequired))
                .OrderBy(exam => exam.Kind == CourseExamKinds.Standard ? 0 : 1)
                .ThenBy(exam => exam.OrderIndex)
                .ToList();

            foreach (var exam in unitExams)
            {
                if (passedExamIds.Contains(exam.Id))
                {
                    continue;
                }

                var lessons = unit.Lessons.ToList();
                return lessons.Count > 0 && lessons.All(lesson =>
                    lessonCompletion.TryGetValue(lesson.Id, out var completed) && completed)
                    ? exam.Id
                    : null;
            }
        }

        var finalExam = exams
            .Where(exam => exam.UnitId is null
                && exam.Kind == CourseExamKinds.Standard
                && exam.IsRequired)
            .OrderBy(exam => exam.OrderIndex)
            .FirstOrDefault(exam => !passedExamIds.Contains(exam.Id));

        if (finalExam is null)
        {
            return null;
        }

        var courseLessons = course.Units.SelectMany(unit => unit.Lessons).ToList();
        return courseLessons.Count > 0 && courseLessons.All(lesson =>
            lessonCompletion.TryGetValue(lesson.Id, out var completed) && completed)
            ? finalExam.Id
            : null;
    }
}
