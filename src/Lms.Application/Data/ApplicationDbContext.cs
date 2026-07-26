using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
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
    public DbSet<ModuleCheckpointProgress> ModuleCheckpointProgresses => Set<ModuleCheckpointProgress>();
    public DbSet<BrokerLearnerAssignment> BrokerLearnerAssignments => Set<BrokerLearnerAssignment>();
    public DbSet<SystemNotification> SystemNotifications => Set<SystemNotification>();
    public DbSet<CourseReminder> CourseReminders => Set<CourseReminder>();
    public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
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
            entity.Property(course => course.Title).HasMaxLength(120).IsRequired();
            entity.Property(course => course.Level).HasMaxLength(40).IsRequired();
            entity.Property(course => course.Jurisdiction).HasMaxLength(100);
            entity.Property(course => course.Price).HasPrecision(18, 2);
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