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
    public DbSet<CourseReminder> CourseReminders => Set<CourseReminder>();
    public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();
    public DbSet<PolicyDisclosureAcknowledgment> PolicyDisclosureAcknowledgments => Set<PolicyDisclosureAcknowledgment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<InstructorPayout> InstructorPayouts => Set<InstructorPayout>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

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
            entity.Property(course => course.ComplianceType).HasMaxLength(40).IsRequired().HasDefaultValue(CourseComplianceTypes.Unspecified);
            entity.Property(course => course.DeliveryMethod).HasMaxLength(40).IsRequired().HasDefaultValue(CourseDeliveryMethods.DistanceEducation);
            entity.Property(course => course.ContinuingEducationType).HasMaxLength(40);
            entity.Property(course => course.CommissionCourseNumber).HasMaxLength(80);
            entity.Property(course => course.RequiredInstructionalMinutes).HasDefaultValue(0);
            entity.Property(course => course.MinimumPassingPercent).HasDefaultValue(75);
            entity.Property(course => course.MinimumAttendancePercent).HasDefaultValue(80);
            entity.Property(course => course.Jurisdiction).HasMaxLength(100);
            entity.Property(course => course.Price).HasPrecision(18, 2);
            entity.Property(course => course.ReviewStatus).HasMaxLength(30).IsRequired().HasDefaultValue(CourseReviewStatuses.Draft);
            entity.Property(course => course.ReviewNote).HasMaxLength(1000);
        });

        modelBuilder.Entity<SchoolProfile>(entity =>
        {
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.LegalName).HasMaxLength(200).IsRequired();
            entity.Property(profile => profile.AdvertisedName).HasMaxLength(200).IsRequired();
            entity.Property(profile => profile.StreetAddress).HasMaxLength(200).IsRequired();
            entity.Property(profile => profile.City).HasMaxLength(100).IsRequired();
            entity.Property(profile => profile.State).HasMaxLength(2).IsRequired();
            entity.Property(profile => profile.PostalCode).HasMaxLength(20).IsRequired();
            entity.Property(profile => profile.EducationDirectorName).HasMaxLength(160).IsRequired();
            entity.Property(profile => profile.CorporateOfficerName).HasMaxLength(160).IsRequired();
            entity.Property(profile => profile.PrimaryInstructorName).HasMaxLength(160).IsRequired();
            entity.Property(profile => profile.PrimaryInstructorEmail).HasMaxLength(160).IsRequired();
            entity.Property(profile => profile.PrimaryInstructorTelephone).HasMaxLength(40).IsRequired();
            entity.Property(profile => profile.ProviderLicenseNumber).HasMaxLength(80);
            entity.Property(profile => profile.InstructorLicenseNumber).HasMaxLength(80);
            entity.Property(profile => profile.SupportEmail).HasMaxLength(160).IsRequired();
            entity.Property(profile => profile.SupportTelephone).HasMaxLength(40).IsRequired();
            entity.Property(profile => profile.SupportHours).HasMaxLength(200);
            entity.Property(profile => profile.WebsiteUrl).HasMaxLength(300);
            entity.Property(profile => profile.LicenseExaminationPerformanceRecord).HasMaxLength(1000).IsRequired();
            entity.Property(profile => profile.AnnualSummaryReportData).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<SchoolStaffMember>(entity =>
        {
            entity.HasKey(member => member.Id);
            entity.Property(member => member.Name).HasMaxLength(160).IsRequired();
            entity.Property(member => member.Role).HasMaxLength(40).IsRequired();
            entity.Property(member => member.Title).HasMaxLength(120);
            entity.Property(member => member.LicenseNumber).HasMaxLength(80);
            entity.Property(member => member.Email).HasMaxLength(160);
            entity.Property(member => member.Telephone).HasMaxLength(40);
            entity.HasIndex(member => new { member.SchoolProfileId, member.Role });
            entity.HasOne(member => member.SchoolProfile)
                .WithMany(profile => profile.StaffMembers)
                .HasForeignKey(member => member.SchoolProfileId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(user => user.LicenseStatus).HasMaxLength(40);
            entity.Property(user => user.Role).HasMaxLength(40).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();
            entity.HasIndex(user => user.PasswordExpiresAt);
            entity.HasIndex(user => user.LockoutEndUtc);
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

        modelBuilder.Entity<PurchaseLine>(entity =>
        {
            entity.HasKey(line => line.Id);
            entity.Property(line => line.CourseTitle).HasMaxLength(120).IsRequired();
            entity.Property(line => line.UnitPrice).HasPrecision(18, 2);
            entity.Property(line => line.LineSubtotal).HasPrecision(18, 2);
            entity.Property(line => line.TaxAmount).HasPrecision(18, 2);
            entity.Property(line => line.DiscountAmount).HasPrecision(18, 2);
            entity.Property(line => line.LineTotal).HasPrecision(18, 2);
            entity.HasIndex(line => line.PaymentTransactionId);
            entity.HasOne(line => line.PaymentTransaction)
                .WithMany(transaction => transaction.PurchaseLines)
                .HasForeignKey(line => line.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(line => line.Course)
                .WithMany()
                .HasForeignKey(line => line.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PolicyDisclosureAcknowledgment>(entity =>
        {
            entity.HasKey(acknowledgment => acknowledgment.Id);
            entity.Property(acknowledgment => acknowledgment.DisclosureVersion).HasMaxLength(40).IsRequired();
            entity.Property(acknowledgment => acknowledgment.StudentLegalName).HasMaxLength(160).IsRequired();
            entity.Property(acknowledgment => acknowledgment.StudentEmail).HasMaxLength(160).IsRequired();
            entity.Property(acknowledgment => acknowledgment.ElectronicSignature).HasMaxLength(160).IsRequired();
            entity.Property(acknowledgment => acknowledgment.CourseTitle).HasMaxLength(120).IsRequired();
            entity.Property(acknowledgment => acknowledgment.CommissionCourseNumber).HasMaxLength(80);
            entity.Property(acknowledgment => acknowledgment.DeliveryMethod).HasMaxLength(40).IsRequired();
            entity.Property(acknowledgment => acknowledgment.TuitionAndFees).HasPrecision(18, 2);
            entity.Property(acknowledgment => acknowledgment.ProctoringFee).HasPrecision(18, 2);
            entity.Property(acknowledgment => acknowledgment.SupportEmail).HasMaxLength(160).IsRequired();
            entity.Property(acknowledgment => acknowledgment.SupportTelephone).HasMaxLength(40).IsRequired();
            entity.HasIndex(acknowledgment => new { acknowledgment.LearnerId, acknowledgment.CourseId, acknowledgment.AcknowledgedAtUtc });
            entity.HasIndex(acknowledgment => acknowledgment.PaymentTransactionId);
            entity.HasOne(acknowledgment => acknowledgment.Learner)
                .WithMany()
                .HasForeignKey(acknowledgment => acknowledgment.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(acknowledgment => acknowledgment.Course)
                .WithMany()
                .HasForeignKey(acknowledgment => acknowledgment.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(acknowledgment => acknowledgment.PaymentTransaction)
                .WithMany(transaction => transaction.PolicyDisclosureAcknowledgments)
                .HasForeignKey(acknowledgment => acknowledgment.PaymentTransactionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(acknowledgment => acknowledgment.Enrollment)
                .WithMany()
                .HasForeignKey(acknowledgment => acknowledgment.EnrollmentId)
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
            entity.Property(certificate => certificate.InstructorName).HasMaxLength(160);
            entity.Property(certificate => certificate.EducationDirectorName).HasMaxLength(160);
            entity.Property(certificate => certificate.CommissionCourseNumber).HasMaxLength(80);
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

            entity.HasOne(attempt => attempt.ExamProctoringSession)
                .WithOne(session => session.AssessmentAttempt)
                .HasForeignKey<AssessmentAttempt>(attempt => attempt.ExamProctoringSessionId)
                .OnDelete(DeleteBehavior.SetNull);
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

        modelBuilder.Entity<ExamProctoringSession>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.Property(session => session.ProctorName).HasMaxLength(160).IsRequired();
            entity.Property(session => session.ExternalSessionId).HasMaxLength(120);
            entity.Property(session => session.SecurityIncidentNotes).HasMaxLength(500);
            entity.HasIndex(session => new { session.CourseId, session.UserAccountId, session.ExpiresAtUtc });
            entity.HasIndex(session => session.ExternalSessionId);

            entity.HasOne(session => session.Course)
                .WithMany(course => course.ExamProctoringSessions)
                .HasForeignKey(session => session.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(session => session.UserAccount)
                .WithMany(user => user.ExamProctoringSessions)
                .HasForeignKey(session => session.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourseActivitySession>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.HasIndex(session => new { session.CourseId, session.UserAccountId, session.StartedAtUtc });
            entity.HasIndex(session => new { session.UserAccountId, session.EndedAtUtc });

            entity.HasOne(session => session.Course)
                .WithMany(course => course.ActivitySessions)
                .HasForeignKey(session => session.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(session => session.UserAccount)
                .WithMany(user => user.CourseActivitySessions)
                .HasForeignKey(session => session.UserAccountId)
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