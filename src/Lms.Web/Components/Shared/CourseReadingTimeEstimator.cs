using System.Globalization;
using System.Text.RegularExpressions;
using Lms.Domain.Entities;

namespace Lms.Web.Components.Shared;

public static partial class CourseReadingTimeEstimator
{
    public const int WordsPerMinute = 255;

    public sealed record LessonEstimate(string UnitTitle, string LessonTitle, int WordCount);

    public sealed record CourseAnalysis(IReadOnlyList<LessonEstimate> Lessons, int TotalWordCount);

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    public static int CountWords(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? 0 : WordPattern().Matches(text).Count;
    }

    public static CourseAnalysis Analyze(
        Course course,
        Lesson? editedLesson = null,
        Unit? newLessonUnit = null,
        string? newLessonTitle = null,
        string? newLessonText = null)
    {
        var lessons = course.Units
            .OrderBy(unit => unit.OrderIndex)
            .SelectMany(unit => unit.Lessons
                .OrderBy(lesson => lesson.OrderIndex)
                .Select(lesson =>
                {
                    var isEditedLesson = editedLesson?.Id == lesson.Id;
                    var title = isEditedLesson ? editedLesson!.Title : lesson.Title;
                    var text = isEditedLesson ? editedLesson!.TextContent : lesson.TextContent;
                    return new LessonEstimate(unit.Title, title, CountWords(text));
                }))
            .ToList();

        if (newLessonUnit is not null && (!string.IsNullOrWhiteSpace(newLessonTitle) || !string.IsNullOrWhiteSpace(newLessonText)))
        {
            lessons.Add(new LessonEstimate(
                newLessonUnit.Title,
                string.IsNullOrWhiteSpace(newLessonTitle) ? "Untitled lesson (draft)" : $"{newLessonTitle.Trim()} (draft)",
                CountWords(newLessonText)));
        }

        return new CourseAnalysis(lessons, lessons.Sum(lesson => lesson.WordCount));
    }

    public static string FormatEstimate(int wordCount)
    {
        if (wordCount <= 0)
        {
            return "0 min";
        }

        var totalSeconds = wordCount * 60d / WordsPerMinute;
        if (totalSeconds < 60)
        {
            return $"~{Math.Ceiling(totalSeconds).ToString(CultureInfo.InvariantCulture)} sec";
        }

        var totalMinutes = (int)Math.Ceiling(totalSeconds / 60);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0
            ? minutes > 0 ? $"~{hours}h {minutes}m" : $"~{hours}h"
            : $"~{totalMinutes} min";
    }
}
