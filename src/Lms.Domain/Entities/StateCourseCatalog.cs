namespace Lms.Domain.Entities;

public sealed class StateCourseDefinition
{
    public string State { get; init; } = string.Empty;
    public string CourseType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public decimal RequiredHours { get; init; }
    public bool HasFirstYearDeadline { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public static class StateCourseCatalog
{
    private static readonly IReadOnlyList<StateCourseDefinition> Definitions =
    [
        new() { State = "Arkansas", CourseType = CourseComplianceTypes.Prelicensing, Title = "Arkansas Salesperson Prelicense", RequiredHours = 60, Notes = "Approved course-list entry; verify current course approval before use." },
        new() { State = "Arkansas", CourseType = CourseComplianceTypes.Postlicensing, Title = "Arkansas Salesperson Postlicense", RequiredHours = 18, Notes = "Approved course-list entry; verify current course approval before use." },
        new() { State = "Connecticut", CourseType = CourseComplianceTypes.Prelicensing, Title = "Connecticut Principles and Practices of Real Estate", RequiredHours = 60, Notes = "Provider guidance describes the principles and practices course and required broker components." },
        new() { State = "Connecticut", CourseType = CourseComplianceTypes.ContinuingEducation, Title = "Connecticut Continuing Education", RequiredHours = 3, Notes = "Three hours is the minimum duration for an individual CE course, not a renewal total." },
        new() { State = "Hawaii", CourseType = CourseComplianceTypes.ContinuingEducation, Title = "Hawaii Core Continuing Education", RequiredHours = 6, Notes = "Part of the 20-hour biennial requirement." },
        new() { State = "Hawaii", CourseType = CourseComplianceTypes.ContinuingEducation, Title = "Hawaii Elective Continuing Education", RequiredHours = 14, Notes = "Part of the 20-hour biennial requirement." },
        new() { State = "Illinois", CourseType = CourseComplianceTypes.Prelicensing, Title = "Illinois Broker Prelicense", RequiredHours = 75, Notes = "Current curriculum source identifies the broker prelicense course." },
        new() { State = "Illinois", CourseType = CourseComplianceTypes.Postlicensing, Title = "Illinois Broker Postlicense", RequiredHours = 45, Notes = "Three 15-hour courses for qualifying first broker renewals." },
        new() { State = "Illinois", CourseType = CourseComplianceTypes.ContinuingEducation, Title = "Illinois Managing Broker CE", RequiredHours = 12, Notes = "Current source includes a 12-hour Broker Management CE course; effective-date rules vary by initial licensure." },
        new() { State = "New York", CourseType = CourseComplianceTypes.Prelicensing, Title = "New York Sales 30-Hour Supplemental Course", RequiredHours = 30, Notes = "Separate course approval application for the supplemental course." },
        new() { State = "Texas", CourseType = CourseComplianceTypes.Prelicensing, Title = "Texas Sales Agent Qualifying Education", RequiredHours = 180, Notes = "Current rules describe 180 hours of qualifying education." },
        new() { State = "Texas", CourseType = CourseComplianceTypes.ContinuingEducation, Title = "Texas Sales Agent Continuing Education", RequiredHours = 18, Notes = "Includes Legal Update I and II plus contract-related and elective coursework." },
        new() { State = "Texas", CourseType = CourseComplianceTypes.ContinuingEducation, Title = "Texas Broker Continuing Education", RequiredHours = 18, Notes = "Includes Legal Update I and II, Broker Responsibility, contract-related, and elective coursework." },
        new() { State = "Virginia", CourseType = CourseComplianceTypes.Postlicensing, Title = "Virginia Salesperson Postlicense", RequiredHours = 30, HasFirstYearDeadline = true, Notes = "Required within the first year of licensure." },
        new() { State = "West Virginia", CourseType = CourseComplianceTypes.Prelicensing, Title = "West Virginia Salesperson Prelicense", RequiredHours = 90, Notes = "Subject areas include principles, law, finance, and appraisal." },
        new() { State = "West Virginia", CourseType = CourseComplianceTypes.Prelicensing, Title = "West Virginia Broker Additional Education", RequiredHours = 90, Notes = "Additional broker education includes operations, human resources, and West Virginia-specific material." },
        new() { State = "West Virginia", CourseType = CourseComplianceTypes.ContinuingEducation, Title = "West Virginia Continuing Education", RequiredHours = 3, Notes = "Three hours is the minimum length of an individual CE course; renewal total was not established by the supplied excerpt." }
    ];

    public static IReadOnlyList<StateCourseDefinition> All => Definitions;

    public static IReadOnlyList<StateCourseDefinition> ForState(string state)
    {
        return Definitions
            .Where(definition => string.Equals(definition.State, state, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
