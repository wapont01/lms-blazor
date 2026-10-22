using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lms.Domain.Entities;

public class Course
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(120)]
    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    [StringLength(40)]
    public string Level { get; set; } = "Beginner";

    [Range(0, 1000)]
    public int DurationHours { get; set; }

    [Range(0, 1000)]
    public int CreditHours { get; set; }

    [Required]
    [StringLength(40)]
    public string ComplianceType { get; set; } = CourseComplianceTypes.Unspecified;

    [Required]
    [StringLength(40)]
    public string DeliveryMethod { get; set; } = CourseDeliveryMethods.DistanceEducation;

    [StringLength(40)]
    public string? ContinuingEducationType { get; set; }

    [Required]
    [StringLength(80)]
    public string Industry { get; set; } = CourseIndustries.RealEstate;

    [StringLength(40)]
    public string? Tier { get; set; }

    [StringLength(80)]
    public string? CommissionCourseNumber { get; set; }

    public int? CompletionWindowDays { get; set; }
    public bool RequiresProctoredExam { get; set; }

    [Range(0, 100000)]
    public int RequiredInstructionalMinutes { get; set; }

    [Range(0, 100)]
    public int MinimumPassingPercent { get; set; } = 75;

    [Range(0, 100)]
    public int MinimumAttendancePercent { get; set; } = 80;

    public bool UsesUnitBasedStructure => string.Equals(ComplianceType, CourseComplianceTypes.Prelicensing, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ComplianceType, CourseComplianceTypes.Postlicensing, StringComparison.OrdinalIgnoreCase);

    public bool IsPrelicensingOrPostlicensing => string.Equals(ComplianceType, CourseComplianceTypes.Prelicensing, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ComplianceType, CourseComplianceTypes.Postlicensing, StringComparison.OrdinalIgnoreCase);

    // NC 21 NCAC 58H .0207(a)-(b): only in-person/synchronous/blended delivery requires time-based attendance; pure distance courses satisfy attendance via full unit/exam completion instead.
    public bool RequiresTimedAttendance => DeliveryMethod is CourseDeliveryMethods.InPerson or CourseDeliveryMethods.SynchronousDistanceLearning or CourseDeliveryMethods.Blended;

    public string SectionLabel => "Unit";

    public string? State { get; set; }

    [Range(0, 100000)]
    public decimal Price { get; set; }

    public bool IsPublished { get; set; }

    public bool IsArchived { get; set; }

    public Guid? OwnerInstructorId { get; set; }

    [Required]
    [StringLength(30)]
    public string ReviewStatus { get; set; } = CourseReviewStatuses.Draft;

    [StringLength(1000)]
    public string? ReviewNote { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EnrollmentClosesAtUtc { get; set; }

    public ICollection<Unit> Units { get; set; } = new List<Unit>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<CompletionCertificate> CompletionCertificates { get; set; } = new List<CompletionCertificate>();
    public ICollection<CourseExam> Exams { get; set; } = new List<CourseExam>();
    public ICollection<UnitExamProgress> UnitExamProgresses { get; set; } = new List<UnitExamProgress>();
    public ICollection<ExamProctoringSession> ExamProctoringSessions { get; set; } = new List<ExamProctoringSession>();
    public ICollection<CourseActivitySession> ActivitySessions { get; set; } = new List<CourseActivitySession>();
}

public static class CourseComplianceTypes
{
    public const string Unspecified = "";
    public const string Prelicensing = "Prelicensing";
    public const string Postlicensing = "Postlicensing";
    public const string ContinuingEducation = "ContinuingEducation";

    public static bool IsRegulated(string? complianceType) => string.Equals(complianceType, Prelicensing, StringComparison.OrdinalIgnoreCase)
        || string.Equals(complianceType, Postlicensing, StringComparison.OrdinalIgnoreCase)
        || string.Equals(complianceType, ContinuingEducation, StringComparison.OrdinalIgnoreCase);

    public static string ToDisplayText(string complianceType) => complianceType switch
    {
        Prelicensing => "Pre-licensing",
        Postlicensing => "Post-licensing",
        ContinuingEducation => "Continuing education",
        _ => "Not selected"
    };
}

public static class CourseIndustries
{
    public const string RealEstate = "Real Estate";
    public const string Mortgage = "Mortgage";
    public const string Appraisal = "Appraisal";
    public const string HomeInspection = "Home Inspection";

    public static readonly IReadOnlyList<string> All =
    [
        RealEstate,
        Mortgage,
        Appraisal,
        HomeInspection
    ];
}

public static class CourseTiers
{
    public const string Sales = "Sales";
    public const string Broker = "Broker";
    public const string SalesReciprocal = "Sales Reciprocal";
    public const string BrokerReciprocal = "Broker Reciprocal";

    public static readonly IReadOnlyList<string> All =
    [
        Sales,
        Broker,
        SalesReciprocal,
        BrokerReciprocal
    ];
}

public static class CourseDeliveryMethods
{
    public const string DistanceEducation = "DistanceEducation";
    public const string SynchronousDistanceLearning = "SynchronousDistanceLearning";
    public const string InPerson = "InPerson";
    public const string Blended = "Blended";

    public static string ToDisplayText(string deliveryMethod) => deliveryMethod switch
    {
        SynchronousDistanceLearning => "Synchronous distance learning",
        InPerson => "In person",
        Blended => "Blended",
        _ => "Distance education"
    };
}

public static class ContinuingEducationTypes
{
    public const string Elective = "Elective";
    public const string GeneralUpdate = "GeneralUpdate";
    public const string BrokerInChargeUpdate = "BrokerInChargeUpdate";
    public const string CoreLaw = "CoreLaw";

    public static readonly IReadOnlyList<string> All = [GeneralUpdate, BrokerInChargeUpdate, CoreLaw, Elective];

    public static bool IsValid(string? courseType) => All.Contains(courseType, StringComparer.OrdinalIgnoreCase);

    public static string ToDisplayText(string courseType) => courseType switch
    {
        GeneralUpdate => "General Update (GENUP)",
        BrokerInChargeUpdate => "Broker-in-Charge Update (BICUP)",
        CoreLaw => "Core Law",
        Elective => "Elective",
        _ => "Not selected"
    };

    public static bool IsUpdateCourse(string? courseType) => string.Equals(courseType, GeneralUpdate, StringComparison.OrdinalIgnoreCase)
        || string.Equals(courseType, BrokerInChargeUpdate, StringComparison.OrdinalIgnoreCase);
}

public static class CourseReviewStatuses
{
    public const string Draft = "Draft";
    public const string PendingReview = "PendingReview";
    public const string ChangesRequested = "ChangesRequested";
    public const string Rejected = "Rejected";
    public const string Approved = "Approved";

    public const int RejectionResubmissionWindowDays = 30;

    public static string ToDisplayText(string reviewStatus) => reviewStatus switch
    {
        PendingReview => "Pending review",
        ChangesRequested => "Changes requested",
        Approved => "Approved",
        Rejected => "Rejected",
        _ => "Draft"
    };

    public static bool CanResubmitAfterRejection(DateTime? reviewedAtUtc, DateTime utcNow)
    {
        return !reviewedAtUtc.HasValue || utcNow <= reviewedAtUtc.Value.AddDays(RejectionResubmissionWindowDays);
    }
}


public class Unit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int OrderIndex { get; set; }

    public Course? Course { get; set; }
    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
}

public class Lesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UnitId { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = string.Empty;
    public string ContentType { get; set; } = "Text";
    public string? ContentUrl { get; set; }

    [Required(ErrorMessage = "Lesson content is required.")]
    public string? TextContent { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Duration Minutes must be greater than 0.")]
    public int DurationMinutes { get; set; }
    public int OrderIndex { get; set; }
    public bool IsRequired { get; set; } = true;

    public Unit? Unit { get; set; }
    public ICollection<LessonProgress> LessonProgresses { get; set; } = new List<LessonProgress>();
}

public class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [EmailAddress]
    [StringLength(160)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(160)]
    public string? LegalName { get; set; }

    [StringLength(40)]
    public string? LicenseNumber { get; set; }

    [Phone]
    [StringLength(40)]
    public string? PhoneNumber { get; set; }

    [StringLength(500)]
    public string? PermanentAddress { get; set; }

    [StringLength(10)]
    public string? LicenseType { get; set; }

    [StringLength(40)]
    public string? State { get; set; }

    [StringLength(40)]
    public string? LicenseStatus { get; set; }

    public DateTime? InitialLicensureDate { get; set; }
    public DateTime? PostlicensingCompletedAt { get; set; }
    public DateTime? BrokerageExperienceStartDate { get; set; }
    public DateTime? BicCourseCompletedAt { get; set; }
    public DateTime? BicEligibleAt { get; set; }
    public DateTime? BicDesignationAt { get; set; }
    public DateTime? ContinuingEducationDueDate { get; set; }
    public DateTime? LicenseRenewalDueDate { get; set; }
    [StringLength(20)]
    public string LicenseRenewalDueDateSource { get; set; } = LicenseDateProvenance.Unknown;
    public DateTime? BicUpdateCourseDueDate { get; set; }
    public bool IsBicEligible { get; set; }

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [StringLength(40)]
    public string Role { get; set; } = "Learner";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PasswordUpdatedAt { get; set; }
    public DateTime? PasswordExpiresAt { get; set; }
    public bool ForcePasswordChange { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    // null = marketing email allowed; a timestamp records when the user opted out (CAN-SPAM suppression)
    public DateTime? MarketingEmailOptOutAtUtc { get; set; }

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<LessonProgress> LessonProgresses { get; set; } = new List<LessonProgress>();
    public ICollection<CompletionCertificate> CompletionCertificates { get; set; } = new List<CompletionCertificate>();
    public ICollection<ExamAttempt> ExamAttempts { get; set; } = new List<ExamAttempt>();
    public ICollection<UnitExamProgress> UnitExamProgresses { get; set; } = new List<UnitExamProgress>();
    public ICollection<BrokerLearnerAssignment> BrokerAssignments { get; set; } = new List<BrokerLearnerAssignment>();
    public ICollection<BrokerLearnerAssignment> LearnerAssignments { get; set; } = new List<BrokerLearnerAssignment>();
    public ICollection<SystemNotification> Notifications { get; set; } = new List<SystemNotification>();
    public ICollection<ExamProctoringSession> ExamProctoringSessions { get; set; } = new List<ExamProctoringSession>();
    public ICollection<CourseActivitySession> CourseActivitySessions { get; set; } = new List<CourseActivitySession>();
    public ICollection<UserRegulatoryLicenseLink> RegulatoryLicenseLinks { get; set; } = new List<UserRegulatoryLicenseLink>();
}

public class BrokerLearnerAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BrokerUserId { get; set; }
    public Guid LearnerUserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid? AssignedByUserId { get; set; }

    public UserAccount? BrokerUser { get; set; }
    public UserAccount? LearnerUser { get; set; }
    public UserAccount? AssignedByUser { get; set; }
}

public class SystemNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientUserId { get; set; }

    [Required]
    [StringLength(80)]
    public string Category { get; set; } = string.Empty;

    [Required]
    [StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(600)]
    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    public UserAccount? RecipientUser { get; set; }
}

public class LicenseComplianceReminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }

    [Required]
    [StringLength(80)]
    public string ReminderType { get; set; } = string.Empty;

    public DateTime DueAtUtc { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public UserAccount? UserAccount { get; set; }
}

public class UnitExamProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public Guid CourseId { get; set; }

    [Required]
    [StringLength(80)]
    public string UnitExamKey { get; set; } = string.Empty;

    public bool Passed { get; set; }
    public DateTime PassedAt { get; set; } = DateTime.UtcNow;

    public UserAccount? UserAccount { get; set; }
    public Course? Course { get; set; }
}

public class CourseUnitExamDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }

    [Required]
    [StringLength(80)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [StringLength(160)]
    public string Title { get; set; } = "Unit Exam";

    [Required]
    [StringLength(500)]
    public string Prompt { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public Guid? UnitId { get; set; }
    public Guid? LessonId { get; set; }
    public bool GatesProgression { get; set; }
    public static readonly Guid StartOfUnitAnchor = Guid.Empty;
    public int OrderIndex { get; set; }

    [NotMapped]
    public bool IsFinal
    {
        get => UnitId is null;
        set
        {
            if (value)
            {
                UnitId = null;
            }
        }
    }

    public Course? Course { get; set; }
    public Unit? Unit { get; set; }
    public ICollection<CourseUnitExamOption> Options { get; set; } = new List<CourseUnitExamOption>();
}

public class CourseUnitExamOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseUnitExamDefinitionId { get; set; }

    [Required]
    [StringLength(80)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [StringLength(220)]
    public string Label { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }
    public int OrderIndex { get; set; }

    public CourseUnitExamDefinition? CourseUnitExamDefinition { get; set; }
}

public class CourseSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public DateTime ScheduledStartAtUtc { get; set; }
    public DateTime ScheduledEndAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsCancelled { get; set; }

    // NC 21 NCAC 58H .0404: Commission must be notified 10 days before a scheduled course; late changes/cancellations (within 5 days of start) must be reported within 24 hours.
    public DateTime? CommissionNotifiedAtUtc { get; set; }
    public DateTime? PendingChangeAtUtc { get; set; }
    public DateTime? ScheduleChangeReportedAtUtc { get; set; }

    public Course? Course { get; set; }
}

public class ComplianceReminderLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [StringLength(60)]
    public string Category { get; set; } = string.Empty;

    public Guid EntityId { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}

public class Enrollment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public Guid CourseId { get; set; }
    [Required]
    [StringLength(40)]
    public string EnrollmentSource { get; set; } = "LearnerPurchase";

    public Guid? SponsoredByBrokerUserId { get; set; }

    [Required]
    [StringLength(40)]
    public string ConsentStatus { get; set; } = "NotRequired";

    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public DateTime AccessGrantedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DueAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? DueSoonReminderSentAt { get; set; }
    public DateTime? OverdueReminderSentAt { get; set; }
    public DateTime? CourseStartReminderSentAt { get; set; }
    public DateTime? EnrollmentClosingReminderSentAt { get; set; }
    public DateTime? ExamReminderSentAt { get; set; }
    public decimal ProgressPercent { get; set; }
    public bool Completed { get; set; }
    public Guid? PaymentTransactionId { get; set; }

    // NC 21 NCAC 58H .0207(f): completion must be reported to the Commission within 7 days.
    public DateTime? CompletionReportSubmittedAtUtc { get; set; }

    [StringLength(80)]
    public string? CertificationFirstName { get; set; }

    [StringLength(80)]
    public string? CertificationLastName { get; set; }

    [StringLength(40)]
    public string? CertificationLicenseNumber { get; set; }

    [StringLength(40)]
    public string? CertificationState { get; set; }

    [StringLength(40)]
    public string CertificationIdentitySource { get; set; } = "Pending";

    public bool CertificationNameConfirmed { get; set; }

    public UserAccount? UserAccount { get; set; }
    public Course? Course { get; set; }
    public ICollection<CourseReminder> Reminders { get; set; } = new List<CourseReminder>();
    public CompletionCertificate? CompletionCertificate { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorUserId { get; set; }

    [Required]
    [StringLength(160)]
    public string ActorEmail { get; set; } = "system@lms.local";

    [Required]
    [StringLength(80)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string TargetType { get; set; } = string.Empty;

    public Guid? TargetId { get; set; }

    [StringLength(800)]
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class LessonProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public Guid LessonId { get; set; }
    public bool Completed { get; set; }
    public DateTime? CompletedAt { get; set; }

    public UserAccount? UserAccount { get; set; }
    public Lesson? Lesson { get; set; }
}

public class CompletionCertificate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(60)]
    public string CertificateNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string VerificationCode { get; set; } = string.Empty;

    public Guid UserAccountId { get; set; }
    public Guid CourseId { get; set; }
    public Guid EnrollmentId { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddYears(1);
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(80)]
    public string? CertificationFirstName { get; set; }

    [StringLength(80)]
    public string? CertificationLastName { get; set; }

    [StringLength(40)]
    public string? CertificationLicenseNumber { get; set; }

    [StringLength(40)]
    public string? CertificationState { get; set; }

    [StringLength(40)]
    public string CertificationIdentitySource { get; set; } = "Pending";

    [StringLength(40)]
    public string CertificationReviewStatus { get; set; } = "PendingIdentity";

    public DateTime? CommissionSubmittedAtUtc { get; set; }

    [StringLength(160)]
    public string? InstructorName { get; set; }

    [StringLength(160)]
    public string? EducationDirectorName { get; set; }

    [StringLength(80)]
    public string? CommissionCourseNumber { get; set; }

    public int CreditHours { get; set; }

    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    [StringLength(300)]
    public string? RevocationReason { get; set; }

    public UserAccount? UserAccount { get; set; }
    public Course? Course { get; set; }
    public Enrollment? Enrollment { get; set; }
}

public class CourseExam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public Guid? UnitId { get; set; }

    [Required]
    [StringLength(160)]
    public string Title { get; set; } = "Final Exam";

    [Range(0, 100)]
    public decimal PassPercent { get; set; } = 80m;

    public int? MaxRetakesPerLearner { get; set; }
    public int RetakeCooldownMinutes { get; set; }

    public bool IsRequired { get; set; } = true;

    public Course? Course { get; set; }
    public Unit? Unit { get; set; }
    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
    public ICollection<ExamAttempt> Attempts { get; set; } = new List<ExamAttempt>();
}

public class ExamQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseExamId { get; set; }

    [Required]
    [StringLength(500)]
    public string Prompt { get; set; } = string.Empty;

    [Required]
    [StringLength(220)]
    public string OptionA { get; set; } = string.Empty;

    [Required]
    [StringLength(220)]
    public string OptionB { get; set; } = string.Empty;

    [Required]
    [StringLength(220)]
    public string OptionC { get; set; } = string.Empty;

    [Required]
    [StringLength(220)]
    public string OptionD { get; set; } = string.Empty;

    [Required]
    [StringLength(1)]
    public string CorrectOption { get; set; } = "A";

    [StringLength(500)]
    public string? FeedbackText { get; set; }

    public int OrderIndex { get; set; }

    public CourseExam? CourseExam { get; set; }
    public ICollection<ExamAnswer> Answers { get; set; } = new List<ExamAnswer>();
}

public class ExamAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseExamId { get; set; }
    public Guid UserAccountId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public int AttemptNumber { get; set; } = 1;
    public decimal ScorePercent { get; set; }
    public bool Passed { get; set; }
    public Guid? ExamProctoringSessionId { get; set; }

    [StringLength(500)]
    public string? FeedbackSummary { get; set; }

    public CourseExam? CourseExam { get; set; }
    public UserAccount? UserAccount { get; set; }
    public ICollection<ExamAnswer> Answers { get; set; } = new List<ExamAnswer>();
    public ExamProctoringSession? ExamProctoringSession { get; set; }
}

public class ExamProctoringSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public Guid UserAccountId { get; set; }

    [Required]
    [StringLength(160)]
    public string ProctorName { get; set; } = string.Empty;

    [StringLength(120)]
    public string? ExternalSessionId { get; set; }

    public DateTime IdentityVerifiedAtUtc { get; set; }
    public bool ClosedBookConfirmed { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public bool SecurityIncidentReported { get; set; }

    [StringLength(500)]
    public string? SecurityIncidentNotes { get; set; }

    // NC 21 NCAC 58H .0203: cheating incidents must be reported to the Commission within 10 days.
    public DateTime? CommissionReportedAtUtc { get; set; }

    public Course? Course { get; set; }
    public UserAccount? UserAccount { get; set; }
    public ExamAttempt? ExamAttempt { get; set; }
}

public class CourseActivitySession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public Guid UserAccountId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public int CreditedMinutes { get; set; }

    public Course? Course { get; set; }
    public UserAccount? UserAccount { get; set; }
}

public class ExamAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamAttemptId { get; set; }
    public Guid ExamQuestionId { get; set; }

    [Required]
    [StringLength(1)]
    public string SelectedOption { get; set; } = "A";

    public bool IsCorrect { get; set; }

    public ExamAttempt? ExamAttempt { get; set; }
    public ExamQuestion? ExamQuestion { get; set; }
}

public class RetakeGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseExamId { get; set; }
    public Guid UserAccountId { get; set; }
    public int GrantedAttempts { get; set; } = 1;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public Guid GrantedByAdminId { get; set; }

    public CourseExam? CourseExam { get; set; }
    public UserAccount? UserAccount { get; set; }
    public UserAccount? GrantedByAdmin { get; set; }
}

public class CourseReminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EnrollmentId { get; set; }
    
    [Required]
    [StringLength(40)]
    public string ReminderType { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public string? Message { get; set; }

    public Enrollment? Enrollment { get; set; }
}
