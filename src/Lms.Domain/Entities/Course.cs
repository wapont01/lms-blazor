using System.ComponentModel.DataAnnotations;

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

    public string? Jurisdiction { get; set; }

    [Range(0, 100000)]
    public decimal Price { get; set; }

    public bool IsPublished { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EnrollmentClosesAtUtc { get; set; }

    public ICollection<Module> Modules { get; set; } = new List<Module>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<CompletionCertificate> CompletionCertificates { get; set; } = new List<CompletionCertificate>();
    public ICollection<CourseAssessment> Assessments { get; set; } = new List<CourseAssessment>();
    public ICollection<ModuleCheckpointProgress> ModuleCheckpointProgresses { get; set; } = new List<ModuleCheckpointProgress>();
}

public class Module
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
    public Guid ModuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentType { get; set; } = "Text";
    public string? ContentUrl { get; set; }
    public string? TextContent { get; set; }
    public int DurationMinutes { get; set; }
    public int OrderIndex { get; set; }
    public bool IsRequired { get; set; } = true;

    public Module? Module { get; set; }
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

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<LessonProgress> LessonProgresses { get; set; } = new List<LessonProgress>();
    public ICollection<CompletionCertificate> CompletionCertificates { get; set; } = new List<CompletionCertificate>();
    public ICollection<AssessmentAttempt> AssessmentAttempts { get; set; } = new List<AssessmentAttempt>();
    public ICollection<ModuleCheckpointProgress> ModuleCheckpointProgresses { get; set; } = new List<ModuleCheckpointProgress>();
    public ICollection<BrokerLearnerAssignment> BrokerAssignments { get; set; } = new List<BrokerLearnerAssignment>();
    public ICollection<BrokerLearnerAssignment> LearnerAssignments { get; set; } = new List<BrokerLearnerAssignment>();
    public ICollection<SystemNotification> Notifications { get; set; } = new List<SystemNotification>();
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

public class ModuleCheckpointProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public Guid CourseId { get; set; }

    [Required]
    [StringLength(80)]
    public string CheckpointKey { get; set; } = string.Empty;

    public bool Passed { get; set; }
    public DateTime PassedAt { get; set; } = DateTime.UtcNow;

    public UserAccount? UserAccount { get; set; }
    public Course? Course { get; set; }
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
    public DateTime? DueAtUtc { get; set; }
    public DateTime? DueSoonReminderSentAt { get; set; }
    public DateTime? OverdueReminderSentAt { get; set; }
    public DateTime? CourseStartReminderSentAt { get; set; }
    public DateTime? EnrollmentClosingReminderSentAt { get; set; }
    public DateTime? AssessmentReminderSentAt { get; set; }
    public decimal ProgressPercent { get; set; }
    public bool Completed { get; set; }
    public Guid? PaymentTransactionId { get; set; }

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

    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    [StringLength(300)]
    public string? RevocationReason { get; set; }

    public UserAccount? UserAccount { get; set; }
    public Course? Course { get; set; }
    public Enrollment? Enrollment { get; set; }
}

public class CourseAssessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }

    [Required]
    [StringLength(160)]
    public string Title { get; set; } = "Final Assessment";

    [Range(0, 100)]
    public decimal PassPercent { get; set; } = 80m;

    public int? MaxRetakesPerLearner { get; set; }
    public int RetakeCooldownMinutes { get; set; }

    public bool IsRequired { get; set; } = true;

    public Course? Course { get; set; }
    public ICollection<AssessmentQuestion> Questions { get; set; } = new List<AssessmentQuestion>();
    public ICollection<AssessmentAttempt> Attempts { get; set; } = new List<AssessmentAttempt>();
}

public class AssessmentQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseAssessmentId { get; set; }

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

    public CourseAssessment? CourseAssessment { get; set; }
    public ICollection<AssessmentAnswer> Answers { get; set; } = new List<AssessmentAnswer>();
}

public class AssessmentAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseAssessmentId { get; set; }
    public Guid UserAccountId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public int AttemptNumber { get; set; } = 1;
    public decimal ScorePercent { get; set; }
    public bool Passed { get; set; }

    [StringLength(500)]
    public string? FeedbackSummary { get; set; }

    public CourseAssessment? CourseAssessment { get; set; }
    public UserAccount? UserAccount { get; set; }
    public ICollection<AssessmentAnswer> Answers { get; set; } = new List<AssessmentAnswer>();
}

public class AssessmentAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentAttemptId { get; set; }
    public Guid AssessmentQuestionId { get; set; }

    [Required]
    [StringLength(1)]
    public string SelectedOption { get; set; } = "A";

    public bool IsCorrect { get; set; }

    public AssessmentAttempt? AssessmentAttempt { get; set; }
    public AssessmentQuestion? AssessmentQuestion { get; set; }
}

public class RetakeGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseAssessmentId { get; set; }
    public Guid UserAccountId { get; set; }
    public int GrantedAttempts { get; set; } = 1;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public Guid GrantedByAdminId { get; set; }

    public CourseAssessment? CourseAssessment { get; set; }
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
