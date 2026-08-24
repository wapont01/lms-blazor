using System.ComponentModel.DataAnnotations;

namespace Lms.Domain.Entities;

public class SchoolProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, StringLength(200)]
    public string LegalName { get; set; } = "WILLIAMS LAND REALTY LLC";

    [Required, StringLength(200)]
    public string AdvertisedName { get; set; } = "WILLIAMS LAND REALTY LLC";

    [Required, StringLength(200)]
    public string StreetAddress { get; set; } = "17214 Belmont Stakes Lane";

    [Required, StringLength(100)]
    public string City { get; set; } = "Charlotte";

    [Required, StringLength(2)]
    public string State { get; set; } = "NC";

    [Required, StringLength(20)]
    public string PostalCode { get; set; } = "28278-8147";

    [Required, StringLength(160)]
    public string EducationDirectorName { get; set; } = "William A. Aponte";

    [Required, StringLength(160)]
    public string CorporateOfficerName { get; set; } = "William A. Aponte";

    [Required, StringLength(160)]
    public string PrimaryInstructorName { get; set; } = "William A. Aponte";

    [Required, EmailAddress, StringLength(160)]
    public string PrimaryInstructorEmail { get; set; } = "wapont01@hotmail.com";

    [Required, Phone, StringLength(40)]
    public string PrimaryInstructorTelephone { get; set; } = "7865530222";

    [StringLength(80)]
    public string? ProviderLicenseNumber { get; set; }

    [StringLength(80)]
    public string? InstructorLicenseNumber { get; set; }

    [Required, EmailAddress, StringLength(160)]
    public string SupportEmail { get; set; } = "wapont01@hotmail.com";

    [Required, Phone, StringLength(40)]
    public string SupportTelephone { get; set; } = "7865530222";

    [StringLength(1200)]
    public string? SupportHours { get; set; } = "Monday-Friday: 7:00 AM-9:00 PM ET, Technical and examination support often available until 10:00 PM; Saturday: 9:00 AM-4:30 PM ET, Technical and examination support often available until 7:00 PM; Sunday: Limited or scheduled examination-proctoring support until 7:00 PM; general telephone support queues otherwise closed ET";

    [Required, StringLength(80)]
    public string SupportTimeZoneId { get; set; } = "America/New_York";

    [Required, StringLength(100)]
    public string WeekdaySupportHours { get; set; } = "7:00 AM-9:00 PM";

    [StringLength(200)]
    public string? WeekdayExtendedSupportHours { get; set; } = "Technical and examination support often available until 10:00 PM";

    [Required, StringLength(100)]
    public string SaturdaySupportHours { get; set; } = "9:00 AM-4:30 PM";

    [StringLength(200)]
    public string? SaturdayExtendedSupportHours { get; set; } = "Technical and examination support often available until 7:00 PM";

    [Required, StringLength(300)]
    public string SundaySupportHours { get; set; } = "Limited or scheduled examination-proctoring support until 7:00 PM; general telephone support queues otherwise closed";

    [StringLength(500)]
    public string? SupportScheduleExceptions { get; set; }

    [StringLength(300)]
    public string? SupportResponseTarget { get; set; }

    public DateOnly? SupportScheduleEffectiveDate { get; set; }

    [StringLength(300)]
    public string? WebsiteUrl { get; set; } = "https://www.WilliamsLandRealty.com";

    [Required, StringLength(1000)]
    public string LicenseExaminationPerformanceRecord { get; set; } = "No License Examination Performance Record has yet been published by the North Carolina Real Estate Commission for WILLIAMS LAND REALTY LLC.";

    [Required, StringLength(1000)]
    public string AnnualSummaryReportData { get; set; } = "No Annual Summary Report data has yet been published by the North Carolina Real Estate Commission for WILLIAMS LAND REALTY LLC.";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }

    public ICollection<SchoolStaffMember> StaffMembers { get; set; } = new List<SchoolStaffMember>();

    public string SupportTimeZoneLabel => SupportTimeZoneId switch
    {
        "America/Los_Angeles" => "PT",
        "America/Denver" => "MT",
        "America/Chicago" => "CT",
        "America/New_York" => "ET",
        _ => SupportTimeZoneId
    };
}

public static class SchoolStaffRoles
{
    public const string Instructor = "Instructor";
    public const string Officer = "Officer";

    public static readonly IReadOnlyList<string> All = [Instructor, Officer];
}

public class SchoolStaffMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SchoolProfileId { get; set; }
    public SchoolProfile? SchoolProfile { get; set; }

    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string Role { get; set; } = SchoolStaffRoles.Instructor;

    [StringLength(120)]
    public string? Title { get; set; }

    [StringLength(80)]
    public string? LicenseNumber { get; set; }

    [EmailAddress, StringLength(160)]
    public string? Email { get; set; }

    [Phone, StringLength(40)]
    public string? Telephone { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}