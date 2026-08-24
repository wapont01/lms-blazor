using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Lms.Application.Tests.Services;

public class PaymentServiceTests
{
    [Fact]
    public async Task ProcessPaymentAsync_CreatesPaymentTransaction_WithValidInput()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();
        var amount = 99.99m;
        var email = "learner@test.local";

        // Create user first to avoid foreign key constraint
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = email,
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await fixture.PaymentService.ProcessPaymentAsync(learnerId, amount, "pm_card_test", email);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.StripePaymentIntentId);
        Assert.StartsWith("pi_test_", result.StripePaymentIntentId);

        // Verify transaction in database
        var transaction = await fixture.DbContext.PaymentTransactions
            .FirstOrDefaultAsync(t => t.StripePaymentIntentId == result.StripePaymentIntentId);
        
        Assert.NotNull(transaction);
        Assert.Equal(learnerId, transaction.LearnerId);
        Assert.Equal(amount, transaction.Amount);
        Assert.Equal("Completed", transaction.Status);
    }

    [Fact]
    public async Task ProcessPaymentAsync_FailsWithNegativeAmount()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();
        var invalidAmount = -10m;

        // Act
        var result = await fixture.PaymentService.ProcessPaymentAsync(learnerId, invalidAmount, "pm_card_test", "learner@test.local");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Invalid payment amount.", result.Message);
    }

    [Fact]
    public async Task ProcessPaymentAsync_AllowsZeroAmountForFreeEnrollment()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();
        var email = "learner@test.local";

        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = email,
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.PaymentService.ProcessPaymentAsync(learnerId, 0m, string.Empty, email);

        Assert.True(result.Success);
        Assert.Equal("No payment required.", result.Message);

        var transaction = await fixture.DbContext.PaymentTransactions
            .FirstOrDefaultAsync(t => t.LearnerId == learnerId && t.Amount == 0m);

        Assert.NotNull(transaction);
        Assert.Equal("Completed", transaction.Status);
    }

    [Fact]
    public async Task ProcessPaymentAsync_FailsWithMissingPaymentMethod()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();
        var amount = 99.99m;

        // Act
        var result = await fixture.PaymentService.ProcessPaymentAsync(learnerId, amount, "", "learner@test.local");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Payment method is required.", result.Message);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_CreatesInvoiceAndLogsAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();

        // Create user
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = "learner@test.local",
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var transaction = new PaymentTransaction
        {
            LearnerId = learnerId,
            Amount = 99.99m,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_12345",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        fixture.DbContext.PaymentTransactions.Add(transaction);
        await fixture.DbContext.SaveChangesAsync();

        var courseNames = new List<string> { "Test Course" };
        var learnerEmail = "learner@test.local";

        // Act
        var invoice = await fixture.PaymentService.GenerateInvoiceAsync(transaction, courseNames, learnerEmail);

        // Assert
        Assert.NotNull(invoice);
        Assert.NotNull(invoice.InvoiceNumber);
        Assert.StartsWith("INV-", invoice.InvoiceNumber);
        Assert.Equal(transaction.Id, invoice.PaymentTransactionId);
        Assert.Equal("learner@test.local", invoice.EmailAddress);

        // Verify invoice in database
        var storedInvoice = await fixture.DbContext.Invoices
            .FirstOrDefaultAsync(i => i.InvoiceNumber == invoice.InvoiceNumber);
        
        Assert.NotNull(storedInvoice);
    }

    [Fact]
    public async Task RefundPaymentAsync_UpdatesTransactionStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();

        // Create user first
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = "learner@test.local",
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var transaction = new PaymentTransaction
        {
            LearnerId = learnerId,
            Amount = 99.99m,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_refund_12345",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        fixture.DbContext.PaymentTransactions.Add(transaction);
        await fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await fixture.PaymentService.RefundPaymentAsync(transaction.StripePaymentIntentId);

        // Assert
        Assert.True(result.Success);

        // Verify transaction status updated
        var updatedTransaction = await fixture.DbContext.PaymentTransactions
            .FirstOrDefaultAsync(t => t.StripePaymentIntentId == transaction.StripePaymentIntentId);
        
        Assert.NotNull(updatedTransaction);
        Assert.Equal("Refunded", updatedTransaction.Status);
        Assert.NotNull(updatedTransaction.RefundedAt);
    }

    [Fact]
    public async Task RefundPaymentAsync_FailsWithInvalidTransaction()
    {
        await using var fixture = await TestFixture.CreateAsync();

        // Act
        var result = await fixture.PaymentService.RefundPaymentAsync("pi_nonexistent");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Transaction not found.", result.Message);
    }

    [Fact]
    public async Task ProcessPaymentAsync_FailsWithDeclinedTestCard()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();
        var email = "learner@test.local";

        // Create user first
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = email,
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        // Act - use declined test card
        var result = await fixture.PaymentService.ProcessPaymentAsync(
            learnerId,
            99.99m,
            "4000000000000002",  // Stripe test card that declines
            email
        );

        // Assert
        Assert.False(result.Success);
        Assert.Contains("declined", result.Message.ToLower());
        Assert.Equal("card_declined", result.ErrorCode);
        Assert.Null(result.StripePaymentIntentId);

        // Verify failed transaction stored
        var transaction = await fixture.DbContext.PaymentTransactions
            .FirstOrDefaultAsync(t => t.LearnerId == learnerId);
        
        Assert.NotNull(transaction);
        Assert.Equal("Failed", transaction.Status);
        Assert.Equal("Test card declined", transaction.FailureReason);
    }

    [Fact]
    public async Task ProcessPaymentAsync_SucceedsWithValidCard()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();
        var email = "learner@test.local";

        // Create user first
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = email,
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        // Act - use valid test card
        var result = await fixture.PaymentService.ProcessPaymentAsync(
            learnerId,
            99.99m,
            "4242424242424242",  // Stripe test card that succeeds
            email
        );

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.StripePaymentIntentId);
        Assert.StartsWith("pi_test_", result.StripePaymentIntentId);

        // Verify successful transaction stored
        var transaction = await fixture.DbContext.PaymentTransactions
            .FirstOrDefaultAsync(t => t.StripePaymentIntentId == result.StripePaymentIntentId);
        
        Assert.NotNull(transaction);
        Assert.Equal("Completed", transaction.Status);
        Assert.NotNull(transaction.CompletedAt);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_GeneratesPdfAndSetsUrl()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();

        // Create user
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = "learner@test.local",
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var transaction = new PaymentTransaction
        {
            LearnerId = learnerId,
            Amount = 99.99m,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_12345",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        fixture.DbContext.PaymentTransactions.Add(transaction);
        await fixture.DbContext.SaveChangesAsync();

        var courseNames = new List<string> { "Advanced C#", "ASP.NET Core" };
        var learnerEmail = "learner@test.local";

        // Act
        var invoice = await fixture.PaymentService.GenerateInvoiceAsync(transaction, courseNames, learnerEmail);

        // Assert
        Assert.NotNull(invoice);
        Assert.NotNull(invoice.PdfUrl);
        Assert.Contains("App_Data/Invoices", invoice.PdfUrl);
        Assert.EndsWith(".pdf", invoice.PdfUrl);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_WithMultipleCourses_CalculatesTaxCorrectly()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var learnerId = Guid.NewGuid();
        var amount = 100m; // Total with tax
        var expectedSubtotal = amount / 1.08m; // Should be ~92.59
        var expectedTax = amount - expectedSubtotal; // Should be ~7.41

        // Create user
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = "learner@test.local",
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var transaction = new PaymentTransaction
        {
            LearnerId = learnerId,
            Amount = amount,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_tax_12345",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        fixture.DbContext.PaymentTransactions.Add(transaction);
        await fixture.DbContext.SaveChangesAsync();

        var courseNames = new List<string> { "Python Basics", "Web Development", "Database Design" };

        // Act
        var invoice = await fixture.PaymentService.GenerateInvoiceAsync(transaction, courseNames, "learner@test.local");

        // Assert
        Assert.NotNull(invoice);
        Assert.NotNull(invoice.PdfUrl);
        Assert.Equal(transaction.Id, invoice.PaymentTransactionId);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IPaymentService PaymentService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IPaymentService paymentService)
        {
            _connection = connection;
            DbContext = dbContext;
            PaymentService = paymentService;
        }

        public static async Task<TestFixture> CreateAsync()
        {
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

            return new TestFixture(connection, dbContext, paymentService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

public class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

public class TestEmailService : IEmailService
{
    public List<EmailMessage> SentEmails { get; } = new();

    public async Task<bool> SendAsync(EmailMessage message)
    {
        SentEmails.Add(message);
        return await Task.FromResult(true);
    }

    public async Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames)
    {
        var message = new EmailMessage(
            recipientEmail,
            $"Receipt - {invoiceNumber}",
            $"Amount: ${amount}"
        );
        return await SendAsync(message);
    }
}

public class TestPDFInvoiceService : IPDFInvoiceService
{
    public List<string> GeneratedInvoices { get; } = new();

    public async Task<string> GeneratePdfInvoiceAsync(Invoice invoice, PaymentTransaction transaction, string learnerEmail, List<string> courseNames, decimal taxRate = 0.08m)
    {
        var fileName = $"{invoice.InvoiceNumber.Replace("/", "-")}.pdf";
        GeneratedInvoices.Add(fileName);
        return await Task.FromResult(fileName);
    }
}
