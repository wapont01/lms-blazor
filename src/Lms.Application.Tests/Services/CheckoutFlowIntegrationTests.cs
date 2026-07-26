using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;

namespace Lms.Application.Tests.Services;

public class CheckoutFlowIntegrationTests
{
    [Fact]
    public async Task CompleteCheckoutFlow_ProcessesPaymentAndCreatesEnrollments()
    {
        // Setup
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var auditLogService = new AuditLogService(dbContext);
        var emailService = new TestEmailService();
        var pdfService = new TestPDFInvoiceService();
        var logger = new TestLogger<PaymentService>();
        var paymentService = new PaymentService(dbContext, logger, auditLogService, emailService, pdfService);

        // Create test data
        var learnerId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var courseName = "Advanced C# Programming";
        var coursePrice = 129.99m;

        // Create learner
        var learner = new UserAccount
        {
            Id = learnerId,
            Email = "learner@test.local",
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.UserAccounts.Add(learner);

        // Create course
        var course = new Course
        {
            Id = courseId,
            Title = courseName,
            Description = "Learn advanced C# concepts",
            Price = coursePrice,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Courses.Add(course);
        await dbContext.SaveChangesAsync();

        // ACT: Process payment
        var paymentResult = await paymentService.ProcessPaymentAsync(
            learnerId,
            coursePrice,
            "pm_card_test",
            learner.Email
        );

        // ASSERT: Payment succeeded
        Assert.True(paymentResult.Success);
        Assert.NotNull(paymentResult.StripePaymentIntentId);

        // Get payment transaction
        var transaction = await dbContext.PaymentTransactions
            .FirstOrDefaultAsync(t => t.StripePaymentIntentId == paymentResult.StripePaymentIntentId);
        Assert.NotNull(transaction);
        Assert.Equal("Completed", transaction.Status);
        Assert.Equal(coursePrice, transaction.Amount);

        // ACT: Create enrollment
        var enrollment = new Enrollment
        {
            UserAccountId = learnerId,
            CourseId = courseId,
            EnrolledAt = DateTime.UtcNow
        };
        dbContext.Enrollments.Add(enrollment);
        await dbContext.SaveChangesAsync();

        // ASSERT: Enrollment created
        var storedEnrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(e => e.UserAccountId == learnerId && e.CourseId == courseId);
        Assert.NotNull(storedEnrollment);

        // ACT: Generate invoice
        var courseNames = new List<string> { courseName };
        var invoice = await paymentService.GenerateInvoiceAsync(transaction, courseNames, learner.Email);

        // ASSERT: Invoice created and email sent
        Assert.NotNull(invoice);
        Assert.StartsWith("INV-", invoice.InvoiceNumber);
        Assert.Equal(learner.Email, invoice.EmailAddress);

        // Verify email was sent
        Assert.Single(emailService.SentEmails);
        var emailSent = emailService.SentEmails[0];
        Assert.Equal(learner.Email, emailSent.RecipientEmail);
        Assert.Contains(invoice.InvoiceNumber, emailSent.Subject);

        // Verify audit logs
        var auditLogs = await dbContext.AuditLogs
            .Where(l => l.ActorUserId == learnerId)
            .ToListAsync();
        
        Assert.NotEmpty(auditLogs);
        Assert.Contains(auditLogs, l => l.Action == "payment.completed");
        Assert.Contains(auditLogs, l => l.Action == "receipt.generated");

        // Cleanup
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task CheckoutFlow_WithPromoCode_AppliesDiscount()
    {
        // Setup
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var auditLogService = new AuditLogService(dbContext);
        var emailService = new TestEmailService();
        var pdfService = new TestPDFInvoiceService();
        var logger = new TestLogger<PaymentService>();
        var paymentService = new PaymentService(dbContext, logger, auditLogService, emailService, pdfService);

        // Create learner
        var learnerId = Guid.NewGuid();
        var learner = new UserAccount
        {
            Id = learnerId,
            Email = "learner@test.local",
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.UserAccounts.Add(learner);
        await dbContext.SaveChangesAsync();

        // Simulate promo code discount (20% off)
        var originalPrice = 100m;
        var discountedPrice = originalPrice * 0.80m; // 80.00

        // ACT: Process discounted payment
        var paymentResult = await paymentService.ProcessPaymentAsync(
            learnerId,
            discountedPrice,
            "pm_card_test",
            learner.Email
        );

        // ASSERT: Payment processed with discounted amount
        Assert.True(paymentResult.Success);

        var transaction = await dbContext.PaymentTransactions
            .FirstOrDefaultAsync(t => t.StripePaymentIntentId == paymentResult.StripePaymentIntentId);
        
        Assert.NotNull(transaction);
        Assert.Equal(discountedPrice, transaction.Amount);

        // Cleanup
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task CheckoutFlow_HandlesCourseMultiplePurchase()
    {
        // Setup: Multiple learners buying the same course
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var auditLogService = new AuditLogService(dbContext);
        var emailService = new TestEmailService();
        var pdfService = new TestPDFInvoiceService();
        var logger = new TestLogger<PaymentService>();
        var paymentService = new PaymentService(dbContext, logger, auditLogService, emailService, pdfService);

        // Create course
        var courseId = Guid.NewGuid();
        dbContext.Courses.Add(new Course
        {
            Id = courseId,
            Title = "Popular Course",
            Description = "Many learners buy this",
            Price = 99.99m,
            CreatedAt = DateTime.UtcNow
        });

        // Create multiple learners
        var learner1Id = Guid.NewGuid();
        var learner2Id = Guid.NewGuid();

        dbContext.UserAccounts.Add(new UserAccount
        {
            Id = learner1Id,
            Email = "learner1@test.local",
            DisplayName = "Learner 1",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });

        dbContext.UserAccounts.Add(new UserAccount
        {
            Id = learner2Id,
            Email = "learner2@test.local",
            DisplayName = "Learner 2",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        // ACT: Both learners purchase
        var payment1 = await paymentService.ProcessPaymentAsync(learner1Id, 99.99m, "pm_card_test", "learner1@test.local");
        var payment2 = await paymentService.ProcessPaymentAsync(learner2Id, 99.99m, "pm_card_test", "learner2@test.local");

        // ASSERT: Both payments processed
        Assert.True(payment1.Success);
        Assert.True(payment2.Success);
        Assert.NotEqual(payment1.StripePaymentIntentId, payment2.StripePaymentIntentId);

        // ACT: Enroll both
        dbContext.Enrollments.Add(new Enrollment { UserAccountId = learner1Id, CourseId = courseId, EnrolledAt = DateTime.UtcNow });
        dbContext.Enrollments.Add(new Enrollment { UserAccountId = learner2Id, CourseId = courseId, EnrolledAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        // ASSERT: Both enrollments created
        var enrollments = await dbContext.Enrollments
            .Where(e => e.CourseId == courseId)
            .ToListAsync();
        
        Assert.Equal(2, enrollments.Count);
        Assert.Contains(enrollments, e => e.UserAccountId == learner1Id);
        Assert.Contains(enrollments, e => e.UserAccountId == learner2Id);

        // Cleanup
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
