namespace Lms.Domain.Entities;

public sealed class GeorgiaCourseDefinition
{
    public string CourseType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int RequiredHours { get; init; }
    public string? ContinuingEducationType { get; init; }
    public bool HasFirstYearDeadline { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public static class GeorgiaCourseCatalog
{
    private static readonly IReadOnlyList<GeorgiaCourseDefinition> Definitions =
    [
        new()
        {
            CourseType = CourseComplianceTypes.Prelicensing,
            Title = "Georgia Salesperson Prelicense",
            RequiredHours = 75,
            Notes = "GREC-approved salesperson prelicense education."
        },
        new()
        {
            CourseType = CourseComplianceTypes.Postlicensing,
            Title = "Georgia Salesperson Postlicense",
            RequiredHours = 25,
            HasFirstYearDeadline = true,
            Notes = "Must be completed during the first year of salesperson licensure."
        },
        new()
        {
            CourseType = CourseComplianceTypes.Prelicensing,
            Title = "Georgia Broker Prelicense",
            RequiredHours = 60,
            Notes = "Broker applicants also need the required active-license experience."
        },
        new()
        {
            CourseType = CourseComplianceTypes.Prelicensing,
            Title = "Georgia Community Association Manager Prelicense",
            RequiredHours = 25,
            Notes = "GREC-approved CAM prelicense education."
        },
        new()
        {
            CourseType = CourseComplianceTypes.ContinuingEducation,
            Title = "Georgia Continuing Education",
            RequiredHours = 36,
            ContinuingEducationType = ContinuingEducationTypes.Elective,
            Notes = "Four-year renewal period; includes 3 License Law hours, with 18 Broker CE hours for brokers."
        }
    ];

    public static IReadOnlyList<GeorgiaCourseDefinition> All => Definitions;
}