using Lms.Domain.Entities;

namespace Lms.Application.Services;

public static class RegulatoryCoursePolicy
{
    public static int? GetCompletionWindowDays(Course course)
    {
        if (course.CompletionWindowDays is > 0)
        {
            return course.CompletionWindowDays;
        }

        if (course.IsPrelicensingOrPostlicensing)
        {
            return 180;
        }

        if (string.Equals(course.ComplianceType, CourseComplianceTypes.ContinuingEducation, StringComparison.OrdinalIgnoreCase)
            && string.Equals(course.DeliveryMethod, CourseDeliveryMethods.DistanceEducation, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        return null;
    }

    public static DateTime? GetEnrollmentDeadlineUtc(Course course, DateTime accessGrantedAtUtc)
    {
        var completionWindowDays = GetCompletionWindowDays(course);
        return completionWindowDays.HasValue
            ? accessGrantedAtUtc.AddDays(completionWindowDays.Value)
            : null;
    }

    public static bool IsContinuingEducationBlackout(DateTime utcNow)
    {
        return utcNow.Month == 6 && utcNow.Day is >= 11 and <= 30;
    }

    public static string? ValidateConfiguration(Course course)
    {
        if (string.Equals(course.ComplianceType, CourseComplianceTypes.ContinuingEducation, StringComparison.OrdinalIgnoreCase)
            && ContinuingEducationTypes.IsUpdateCourse(course.ContinuingEducationType)
            && string.Equals(course.DeliveryMethod, CourseDeliveryMethods.DistanceEducation, StringComparison.OrdinalIgnoreCase))
        {
            return "Continuing Education Update courses cannot use self-paced distance education.";
        }

        if (course.IsPrelicensingOrPostlicensing && course.MinimumPassingPercent < 75)
        {
            return "Prelicensing and Postlicensing courses require a minimum passing score of 75 percent.";
        }

        if (course.IsPrelicensingOrPostlicensing && !course.RequiresProctoredExam)
        {
            return "Prelicensing and Postlicensing courses require a proctored end-of-course examination.";
        }

        return null;
    }
}
