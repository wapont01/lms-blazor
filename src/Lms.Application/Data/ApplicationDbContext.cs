using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<SchoolProfile> SchoolProfiles => Set<SchoolProfile>();
    public DbSet<SchoolStaffMember> SchoolStaffMembers => Set<SchoolStaffMember>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LessonProgress> LessonProgresses => Set<LessonProgress>();
    public DbSet<CompletionCertificate> CompletionCertificates => Set<CompletionCertificate>();
    public DbSet<CourseAssessment> CourseAssessments => Set<CourseAssessment>();
    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();
    public DbSet<AssessmentAttempt> AssessmentAttempts => Set<AssessmentAttempt>();
    public DbSet<AssessmentAnswer> AssessmentAnswers => Set<AssessmentAnswer>();
    public DbSet<RetakeGrant> RetakeGrants => Set<RetakeGrant>();
    public DbSet<CourseCheckpointDefinition> CourseCheckpointDefinitions => Set<CourseCheckpointDefinition>();
    public DbSet<CourseCheckpointOption> CourseCheckpointOptions => Set<CourseCheckpointOption>();
    public DbSet<ModuleCheckpointProgress> ModuleCheckpointProgresses => Set<ModuleCheckpointProgress>();
    public DbSet<ExamProctoringSession> ExamProctoringSessions => Set<ExamProctoringSession>();
    public DbSet<CourseActivitySession> CourseActivitySessions => Set<CourseActivitySession>();
    public DbSet<BrokerLearnerAssignment> BrokerLearnerAssignments => Set<BrokerLearnerAssignment>();
    public DbSet<SystemNotification> SystemNotifications => Set<SystemNotification>();
    public DbSet<LicenseComplianceReminder> LicenseComplianceReminders => Set<LicenseComplianceReminder>();
    public DbSet<RegulatoryDataSnapshot> RegulatoryDataSnapshots => Set<RegulatoryDataSnapshot>();
    public DbSet<RegulatoryFirm> RegulatoryFirms => Set<RegulatoryFirm>();
    public DbSet<RegulatoryLicensee> RegulatoryLicensees => Set<RegulatoryLicensee>();
    public DbSet<RegulatoryFirmLicenseeAffiliation> RegulatoryFirmLicenseeAffiliations => Set<RegulatoryFirmLicenseeAffiliation>();
    public DbSet<UserRegulatoryLicenseLink> UserRegulatoryLicenseLinks => Set<UserRegulatoryLicenseLink>();
    public DbSet<CourseEligibilityPolicy> CourseEligibilityPolicies => Set<CourseEligibilityPolicy>();
    public DbSet<CourseReminder> CourseReminders => Set<CourseReminder>();
    public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();
    public DbSet<PolicyDisclosureAcknowledgment> PolicyDisclosureAcknowledgments => Set<PolicyDisclosureAcknowledgment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<InstructorPayout> InstructorPayouts => Set<InstructorPayout>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<CourseSession> CourseSessions => Set<CourseSession>();
    public DbSet<ComplianceReminderLog> ComplianceReminderLogs => Set<ComplianceReminderLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Course>(entity =>
        {
            entity.HasKey(course => course.Id);
            entity.HasIndex(course => course.Slug).IsUnique();
            entity.HasIndex(course => course.OwnerInstructorId);
            entity.Property(course => course.Title).HasMaxLength(120).IsRequired();
            entity.Property(course => course.Level).HasMaxLength(40).IsRequired();
            entity.Property(course => course.State).HasMaxLength(100);
            entity.Property(course => course.Price).HasPrecision(18, 2);
            entity.Property(course => course.ReviewStatus).HasMaxLength(30).IsRequired().HasDefaultValue(CourseReviewStatuses.Draft);
            entity.Property(course => course.ReviewNote).HasMaxLength(1000);
        });

        modelBuilder.Entity<Module>(entity =>
        {
            entity.HasKey(module => module.Id);
            entity.Property(module => module.Title).HasMaxLength(160).IsRequired();
            entity.HasOne(module => module.Course)
                .WithMany(course => course.Modules)
                .HasForeignKey(module => module.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.HasKey(lesson => lesson.Id);
            entity.Property(lesson => lesson.Title).HasMaxLength(160).IsRequired();
            entity.Property(lesson => lesson.ContentType).HasMaxLength(40).IsRequired();
            entity.HasOne(lesson => lesson.Module)
                .WithMany(module => module.Lessons)
                .HasForeignKey(lesson => lesson.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Email).HasMaxLength(160).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(user => user.LegalName).HasMaxLength(160);
            entity.Property(user => user.LicenseNumber).HasMaxLength(40);
            entity.Property(user => user.PhoneNumber).HasMaxLength(40);
            entity.Property(user => user.PermanentAddress).HasMaxLength(500);
            entity.Property(user => user.LicenseType).HasMaxLength(10);
            entity.Property(user => user.Role).HasMaxLength(40).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();
            entity.HasIndex(user => user.PasswordExpiresAt);
            entity.HasIndex(user => user.LockoutEndUtc);
            entity.HasIndex(user => user.InitialLicensureDate);
            entity.HasIndex(user => user.BicEligibleAt);
            entity.HasIndex(user => user.ContinuingEducationDueDate);
            entity.HasIndex(user => user.LicenseRenewalDueDate);
        });

        modelBuilder.Entity<LicenseComplianceReminder>(entity =>
        {
            entity.HasKey(reminder => reminder.Id);
            entity.Property(reminder => reminder.ReminderType).HasMaxLength(80).IsRequired();
            entity.HasIndex(reminder => new { reminder.UserAccountId, reminder.ReminderType, reminder.DueAtUtc }).IsUnique();
            entity.HasOne(reminder => reminder.UserAccount)
                .WithMany()
                .HasForeignKey(reminder => reminder.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RegulatoryDataSnapshot>(entity =>
        {
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.State).HasMaxLength(40).IsRequired();
            entity.Property(snapshot => snapshot.SourceName).HasMaxLength(120).IsRequired();
            entity.Property(snapshot => snapshot.SourceUrl).HasMaxLength(1000).IsRequired();
            entity.Property(snapshot => snapshot.ParserVersion).HasMaxLength(80);
            entity.Property(snapshot => snapshot.AcquisitionCost).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.Status).HasMaxLength(30).IsRequired();
            entity.HasIndex(snapshot => new { snapshot.State, snapshot.Status, snapshot.RetrievedAtUtc });
        });

        modelBuilder.Entity<RegulatoryFirm>(entity =>
        {
            entity.HasKey(firm => firm.Id);
            entity.Property(firm => firm.State).HasMaxLength(40).IsRequired();
            entity.Property(firm => firm.LicenseNumber).HasMaxLength(40).IsRequired();
            entity.Property(firm => firm.LegalName).HasMaxLength(200).IsRequired();
            entity.Property(firm => firm.TradeName).HasMaxLength(200);
            entity.Property(firm => firm.LicenseStatus).HasMaxLength(40);
            entity.Property(firm => firm.Address).HasMaxLength(500);
            entity.Property(firm => firm.QualifyingBrokerName).HasMaxLength(160);
            entity.HasIndex(firm => new { firm.SnapshotId, firm.State, firm.LicenseNumber }).IsUnique();
            entity.HasOne(firm => firm.Snapshot)
                .WithMany(snapshot => snapshot.Firms)
                .HasForeignKey(firm => firm.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RegulatoryLicensee>(entity =>
        {
            entity.HasKey(licensee => licensee.Id);
            entity.Property(licensee => licensee.State).HasMaxLength(40).IsRequired();
            entity.Property(licensee => licensee.LicenseNumber).HasMaxLength(40).IsRequired();
            entity.Property(licensee => licensee.Name).HasMaxLength(160).IsRequired();
            entity.Property(licensee => licensee.LicenseType).HasMaxLength(40);
            entity.Property(licensee => licensee.LicenseLevel).HasMaxLength(20);
            entity.Property(licensee => licensee.LicenseStatus).HasMaxLength(40);
            entity.HasIndex(licensee => new { licensee.SnapshotId, licensee.State, licensee.LicenseNumber }).IsUnique();
            entity.HasOne(licensee => licensee.Snapshot)
                .WithMany(snapshot => snapshot.Licensees)
                .HasForeignKey(licensee => licensee.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RegulatoryFirmLicenseeAffiliation>(entity =>
        {
            entity.HasKey(affiliation => affiliation.Id);
            entity.Property(affiliation => affiliation.BrokerInChargeName).HasMaxLength(160);
            entity.HasIndex(affiliation => new { affiliation.SnapshotId, affiliation.FirmId, affiliation.LicenseeId }).IsUnique();
            entity.HasOne(affiliation => affiliation.Snapshot)
                .WithMany()
                .HasForeignKey(affiliation => affiliation.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(affiliation => affiliation.Firm)
                .WithMany(firm => firm.LicenseeAffiliations)
                .HasForeignKey(affiliation => affiliation.FirmId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(affiliation => affiliation.Licensee)
                .WithMany(licensee => licensee.FirmAffiliations)
                .HasForeignKey(affiliation => affiliation.LicenseeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRegulatoryLicenseLink>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.HasIndex(link => link.UserAccountId).IsUnique();
            entity.HasIndex(link => new { link.RegulatorySnapshotId, link.RegulatoryLicenseeId });
            entity.HasOne(link => link.UserAccount)
                .WithMany(user => user.RegulatoryLicenseLinks)
                .HasForeignKey(link => link.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.RegulatorySnapshot)
                .WithMany()
                .HasForeignKey(link => link.RegulatorySnapshotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(link => link.RegulatoryLicensee)
                .WithMany()
                .HasForeignKey(link => link.RegulatoryLicenseeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CourseEligibilityPolicy>(entity =>
        {
            entity.HasKey(policy => policy.Id);
            entity.Property(policy => policy.State).HasMaxLength(40).IsRequired();
            entity.Property(policy => policy.LicenseLevel).HasMaxLength(20);
            entity.Property(policy => policy.ComplianceType).HasMaxLength(40).IsRequired();
            entity.Property(policy => policy.ContinuingEducationType).HasMaxLength(40);
            entity.Property(policy => policy.EligibilityMessage).HasMaxLength(500).IsRequired();
            entity.HasIndex(policy => new { policy.State, policy.LicenseLevel, policy.ComplianceType, policy.EffectiveFrom });
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.HasKey(enrollment => enrollment.Id);
            entity.Property(enrollment => enrollment.EnrollmentSource).HasMaxLength(40).IsRequired();
            entity.Property(enrollment => enrollment.ConsentStatus).HasMaxLength(40).IsRequired();
            entity.Property(enrollment => enrollment.ProgressPercent).HasPrecision(5, 2);
            entity.HasIndex(enrollment => new { enrollment.UserAccountId, enrollment.CourseId }).IsUnique();
            entity.HasIndex(enrollment => new { enrollment.SponsoredByBrokerUserId, enrollment.EnrollmentSource });
            entity.HasIndex(enrollment => new { enrollment.DueAtUtc, enrollment.Completed });
            entity.HasIndex(enrollment => new { enrollment.UserAccountId, enrollment.DueAtUtc });

            entity.HasOne(enrollment => enrollment.UserAccount)
                .WithMany(user => user.Enrollments)
                .HasForeignKey(enrollment => enrollment.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(enrollment => enrollment.Course)
                .WithMany(course => course.Enrollments)
                .HasForeignKey(enrollment => enrollment.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(enrollment => enrollment.SponsoredByBrokerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.ActorEmail).HasMaxLength(160).IsRequired();
            entity.Property(audit => audit.Action).HasMaxLength(80).IsRequired();
            entity.Property(audit => audit.TargetType).HasMaxLength(80).IsRequired();
            entity.Property(audit => audit.Details).HasMaxLength(800);
            entity.HasIndex(audit => audit.CreatedAt);
            entity.HasIndex(audit => audit.Action);
        });

        modelBuilder.Entity<LessonProgress>(entity =>
        {
            entity.HasKey(progress => progress.Id);
            entity.HasIndex(progress => new { progress.UserAccountId, progress.LessonId }).IsUnique();

            entity.HasOne(progress => progress.UserAccount)
                .WithMany(user => user.LessonProgresses)
                .HasForeignKey(progress => progress.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(progress => progress.Lesson)
                .WithMany(lesson => lesson.LessonProgresses)
                .HasForeignKey(progress => progress.LessonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompletionCertificate>(entity =>
        {
            entity.HasKey(certificate => certificate.Id);
            entity.Property(certificate => certificate.CertificateNumber).HasMaxLength(60).IsRequired();
            entity.Property(certificate => certificate.VerificationCode).HasMaxLength(64).IsRequired();
            entity.Property(certificate => certificate.RevocationReason).HasMaxLength(300);
            entity.HasIndex(certificate => certificate.CertificateNumber).IsUnique();
            entity.HasIndex(certificate => certificate.VerificationCode).IsUnique();
            entity.HasIndex(certificate => new { certificate.UserAccountId, certificate.ExpiresAt });

            entity.HasOne(certificate => certificate.UserAccount)
                .WithMany(user => user.CompletionCertificates)
                .HasForeignKey(certificate => certificate.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(certificate => certificate.Course)
                .WithMany(course => course.CompletionCertificates)
                .HasForeignKey(certificate => certificate.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(certificate => certificate.Enrollment)
                .WithOne(enrollment => enrollment.CompletionCertificate)
                .HasForeignKey<CompletionCertificate>(certificate => certificate.EnrollmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourseAssessment>(entity =>
        {
            entity.HasKey(assessment => assessment.Id);
            entity.Property(assessment => assessment.Title).HasMaxLength(160).IsRequired();
            entity.Property(assessment => assessment.PassPercent).HasPrecision(5, 2);
            entity.HasIndex(assessment => new { assessment.CourseId, assessment.IsRequired });

            entity.HasOne(assessment => assessment.Course)
                .WithMany(course => course.Assessments)
                .HasForeignKey(assessment => assessment.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssessmentQuestion>(entity =>
        {
            entity.HasKey(question => question.Id);
            entity.Property(question => question.Prompt).HasMaxLength(500).IsRequired();
            entity.Property(question => question.OptionA).HasMaxLength(220).IsRequired();
            entity.Property(question => question.OptionB).HasMaxLength(220).IsRequired();
            entity.Property(question => question.OptionC).HasMaxLength(220).IsRequired();
            entity.Property(question => question.OptionD).HasMaxLength(220).IsRequired();
            entity.Property(question => question.CorrectOption).HasMaxLength(1).IsRequired();
            entity.Property(question => question.FeedbackText).HasMaxLength(500);
            entity.HasIndex(question => new { question.CourseAssessmentId, question.OrderIndex });

            entity.HasOne(question => question.CourseAssessment)
                .WithMany(assessment => assessment.Questions)
                .HasForeignKey(question => question.CourseAssessmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssessmentAttempt>(entity =>
        {
            entity.HasKey(attempt => attempt.Id);
            entity.Property(attempt => attempt.ScorePercent).HasPrecision(5, 2);
            entity.Property(attempt => attempt.FeedbackSummary).HasMaxLength(500);
            entity.HasIndex(attempt => new { attempt.CourseAssessmentId, attempt.UserAccountId, attempt.SubmittedAt });

            entity.HasOne(attempt => attempt.CourseAssessment)
                .WithMany(assessment => assessment.Attempts)
                .HasForeignKey(attempt => attempt.CourseAssessmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(attempt => attempt.UserAccount)
                .WithMany(user => user.AssessmentAttempts)
                .HasForeignKey(attempt => attempt.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssessmentAnswer>(entity =>
        {
            entity.HasKey(answer => answer.Id);
            entity.Property(answer => answer.SelectedOption).HasMaxLength(1).IsRequired();
            entity.HasIndex(answer => new { answer.AssessmentAttemptId, answer.AssessmentQuestionId }).IsUnique();

            entity.HasOne(answer => answer.AssessmentAttempt)
                .WithMany(attempt => attempt.Answers)
                .HasForeignKey(answer => answer.AssessmentAttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(answer => answer.AssessmentQuestion)
                .WithMany(question => question.Answers)
                .HasForeignKey(answer => answer.AssessmentQuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RetakeGrant>(entity =>
        {
            entity.HasKey(grant => grant.Id);
            entity.HasIndex(grant => new { grant.CourseAssessmentId, grant.UserAccountId }).IsUnique();

            entity.HasOne(grant => grant.CourseAssessment)
                .WithMany()
                .HasForeignKey(grant => grant.CourseAssessmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(grant => grant.UserAccount)
                .WithMany()
                .HasForeignKey(grant => grant.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(grant => grant.GrantedByAdmin)
                .WithMany()
                .HasForeignKey(grant => grant.GrantedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CourseCheckpointDefinition>(entity =>
        {
            entity.HasKey(definition => definition.Id);
            entity.Property(definition => definition.Key).HasMaxLength(80).IsRequired();
            entity.Property(definition => definition.Title).HasMaxLength(160).IsRequired();
            entity.Property(definition => definition.Prompt).HasMaxLength(500).IsRequired();
            entity.Property(definition => definition.Description).HasMaxLength(500);
            entity.HasIndex(definition => new { definition.CourseId, definition.Key }).IsUnique();

            entity.HasOne(definition => definition.Course)
                .WithMany()
                .HasForeignKey(definition => definition.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(definition => definition.Module)
                .WithMany()
                .HasForeignKey(definition => definition.ModuleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CourseCheckpointOption>(entity =>
        {
            entity.HasKey(option => option.Id);
            entity.Property(option => option.Key).HasMaxLength(80).IsRequired();
            entity.Property(option => option.Label).HasMaxLength(220).IsRequired();
            entity.HasIndex(option => new { option.CourseCheckpointDefinitionId, option.OrderIndex });

            entity.HasOne(option => option.CourseCheckpointDefinition)
                .WithMany(definition => definition.Options)
                .HasForeignKey(option => option.CourseCheckpointDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModuleCheckpointProgress>(entity =>
        {
            entity.HasKey(progress => progress.Id);
            entity.Property(progress => progress.CheckpointKey).HasMaxLength(80).IsRequired();
            entity.HasIndex(progress => new { progress.UserAccountId, progress.CourseId, progress.CheckpointKey }).IsUnique();

            entity.HasOne(progress => progress.UserAccount)
                .WithMany(user => user.ModuleCheckpointProgresses)
                .HasForeignKey(progress => progress.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(progress => progress.Course)
                .WithMany(course => course.ModuleCheckpointProgresses)
                .HasForeignKey(progress => progress.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BrokerLearnerAssignment>(entity =>
        {
            entity.HasKey(assignment => assignment.Id);
            entity.HasIndex(assignment => new { assignment.BrokerUserId, assignment.LearnerUserId }).IsUnique();
            entity.HasIndex(assignment => assignment.LearnerUserId);

            entity.HasOne(assignment => assignment.BrokerUser)
                .WithMany(user => user.BrokerAssignments)
                .HasForeignKey(assignment => assignment.BrokerUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(assignment => assignment.LearnerUser)
                .WithMany(user => user.LearnerAssignments)
                .HasForeignKey(assignment => assignment.LearnerUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(assignment => assignment.AssignedByUser)
                .WithMany()
                .HasForeignKey(assignment => assignment.AssignedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SystemNotification>(entity =>
        {
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Category).HasMaxLength(80).IsRequired();
            entity.Property(notification => notification.Title).HasMaxLength(160).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(600).IsRequired();
            entity.HasIndex(notification => new { notification.RecipientUserId, notification.ReadAt, notification.CreatedAt });

            entity.HasOne(notification => notification.RecipientUser)
                .WithMany(user => user.Notifications)
                .HasForeignKey(notification => notification.RecipientUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourseReminder>(entity =>
        {
            entity.HasKey(reminder => reminder.Id);
            entity.Property(reminder => reminder.ReminderType).HasMaxLength(40).IsRequired();
            entity.Property(reminder => reminder.Message).HasMaxLength(500);
            entity.HasIndex(reminder => new { reminder.EnrollmentId, reminder.ReminderType }).IsUnique();
            entity.HasIndex(reminder => reminder.SentAt);

            entity.HasOne(reminder => reminder.Enrollment)
                .WithMany(enrollment => enrollment.Reminders)
                .HasForeignKey(reminder => reminder.EnrollmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShoppingCart>(entity =>
        {
            entity.HasKey(cart => cart.Id);
            entity.HasIndex(cart => cart.LearnerId).IsUnique();
            entity.OwnsMany(cart => cart.Items, items =>
            {
                items.WithOwner().HasForeignKey("ShoppingCartId");
                items.HasKey("Id");
            });
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(transaction => transaction.Id);
            entity.Property(transaction => transaction.Status).HasMaxLength(50).IsRequired();
            entity.Property(transaction => transaction.Amount).HasPrecision(18, 2);
            entity.Property(transaction => transaction.StripePaymentIntentId).HasMaxLength(120);
            entity.Property(transaction => transaction.FailureReason).HasMaxLength(300);
            entity.HasIndex(transaction => new { transaction.LearnerId, transaction.CreatedAt });
            entity.HasIndex(transaction => transaction.Status);

            entity.HasOne(transaction => transaction.Learner)
                .WithMany()
                .HasForeignKey(transaction => transaction.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(invoice => invoice.Id);
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(50).IsRequired();
            entity.Property(invoice => invoice.EmailAddress).HasMaxLength(160);
            entity.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();
            entity.HasIndex(invoice => new { invoice.PaymentTransactionId, invoice.IssuedAt });

            entity.HasOne(invoice => invoice.PaymentTransaction)
                .WithMany(transaction => transaction.Invoices)
                .HasForeignKey(invoice => invoice.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}