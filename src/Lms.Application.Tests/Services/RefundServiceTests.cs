using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Lms.Application.Tests.Services;

public class RefundServiceTests
{
    private class TestEmailService : IEmailService
    {
        public Task<bool> SendAsync(EmailMessage message) => Task.FromResult(true);
        public Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames) => Task.FromResult(true);
    }

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public readonly ApplicationDbContext DbContext;
        public readonly IRefundService RefundService;

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IRefundService refundService)
        {
            _connection = connection;
            DbContext = dbContext;
            RefundService = refundService;
        }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            connection.Open();

            var dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(dbContextOptions);
            await dbContext.Database.EnsureCreatedAsync();

            var logger = new TestLogger<RefundService>();
            var emailService = new TestEmailService();

            var refundService = new RefundService(dbContext, logger, emailService);

            return new TestFixture(connection, dbContext, refundService);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitiateRefund_CreatesRefundAndUpdatesPayment_ForCompletedTransaction()
    {
        // Arrange
        var fixture = await TestFixture.CreateAsync();
        var learner = new UserAccount { Email = "learner@test.com", DisplayName = "Test Learner" };
        await fixture.DbContext.UserAccounts.AddAsync(learner);

        var payment = new PaymentTransaction
        {
            LearnerId = learner.Id,
            Amount = 99.99m,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_12345",
            CreatedAt = DateTime.UtcNow
        };
        await fixture.DbContext.PaymentTransactions.AddAsync(payment);
        await fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await fixture.RefundService.InitiateRefundAsync(payment, "requested_by_customer");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.RefundId);
        Assert.NotNull(result.StripeRefundId);
        Assert.Equal(99.99m, result.RefundedAmount);

        // Verify payment updated to Refunded
        var updatedPayment = await fixture.DbContext.PaymentTransactions.FindAsync(payment.Id);
        Assert.NotNull(updatedPayment);
        var updatedPaymentValue = updatedPayment!;
        Assert.Equal("Refunded", updatedPaymentValue.Status);
        Assert.NotNull(updatedPaymentValue.RefundedAt);

        // Verify refund created
        var refund = await fixture.DbContext.Refunds
            .FirstOrDefaultAsync(r => r.PaymentTransactionId == payment.Id);
        Assert.NotNull(refund);
        var refundValue = refund!;
        Assert.Equal(99.99m, refundValue.Amount);
        Assert.Equal("Succeeded", refundValue.Status);
        Assert.Equal("requested_by_customer", refundValue.Reason);

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task InitiateRefund_Fails_ForNonCompletedPayment()
    {
        // Arrange
        var fixture = await TestFixture.CreateAsync();
        var learner = new UserAccount { Email = "learner@test.com", DisplayName = "Test Learner" };
        await fixture.DbContext.UserAccounts.AddAsync(learner);

        var payment = new PaymentTransaction
        {
            LearnerId = learner.Id,
            Amount = 99.99m,
            Status = "Pending",
            StripePaymentIntentId = "pi_test_12345",
            CreatedAt = DateTime.UtcNow
        };
        await fixture.DbContext.PaymentTransactions.AddAsync(payment);
        await fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await fixture.RefundService.InitiateRefundAsync(payment);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("INVALID_PAYMENT_STATUS", result.ErrorCode);
        Assert.NotNull(result.Message);
        Assert.Contains("completed", result.Message!, StringComparison.OrdinalIgnoreCase);

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task RefundPayment_HandlesPartialRefund_WithValidAmount()
    {
        // Arrange
        var fixture = await TestFixture.CreateAsync();
        var learner = new UserAccount { Email = "learner@test.com", DisplayName = "Test Learner" };
        await fixture.DbContext.UserAccounts.AddAsync(learner);

        var payment = new PaymentTransaction
        {
            LearnerId = learner.Id,
            Amount = 100m,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_12345",
            CreatedAt = DateTime.UtcNow
        };
        await fixture.DbContext.PaymentTransactions.AddAsync(payment);
        await fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await fixture.RefundService.RefundPaymentAsync(payment, 30m, "duplicate");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(30m, result.RefundedAmount);

        // Verify payment NOT marked as Refunded (partial refund)
        var updatedPayment = await fixture.DbContext.PaymentTransactions.FindAsync(payment.Id);
        Assert.NotNull(updatedPayment);
        Assert.Equal("Completed", updatedPayment!.Status); // Should NOT be Refunded for partial

        // Verify refund created with correct amount
        var refund = await fixture.DbContext.Refunds
            .FirstOrDefaultAsync(r => r.PaymentTransactionId == payment.Id);
        Assert.NotNull(refund);
        Assert.Equal(30m, refund!.Amount);

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task RefundPayment_Fails_ForInvalidAmount()
    {
        // Arrange
        var fixture = await TestFixture.CreateAsync();
        var learner = new UserAccount { Email = "learner@test.com", DisplayName = "Test Learner" };
        await fixture.DbContext.UserAccounts.AddAsync(learner);

        var payment = new PaymentTransaction
        {
            LearnerId = learner.Id,
            Amount = 100m,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_12345",
            CreatedAt = DateTime.UtcNow
        };
        await fixture.DbContext.PaymentTransactions.AddAsync(payment);
        await fixture.DbContext.SaveChangesAsync();

        // Act
        var resultTooHigh = await fixture.RefundService.RefundPaymentAsync(payment, 150m);
        var resultZero = await fixture.RefundService.RefundPaymentAsync(payment, 0m);

        // Assert
        Assert.False(resultTooHigh.Success);
        Assert.Equal("INVALID_AMOUNT", resultTooHigh.ErrorCode);

        Assert.False(resultZero.Success);
        Assert.Equal("INVALID_AMOUNT", resultZero.ErrorCode);

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task RefundPayment_Fails_WhenNoStripePaymentIntent()
    {
        // Arrange
        var fixture = await TestFixture.CreateAsync();
        var learner = new UserAccount { Email = "learner@test.com", DisplayName = "Test Learner" };
        await fixture.DbContext.UserAccounts.AddAsync(learner);

        var payment = new PaymentTransaction
        {
            LearnerId = learner.Id,
            Amount = 100m,
            Status = "Completed",
            StripePaymentIntentId = null, // No Stripe intent
            CreatedAt = DateTime.UtcNow
        };
        await fixture.DbContext.PaymentTransactions.AddAsync(payment);
        await fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await fixture.RefundService.InitiateRefundAsync(payment);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("NO_STRIPE_INTENT", result.ErrorCode);

        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task GetRefundStatus_ReturnsRefund_WithCurrentStatus()
    {
        // Arrange
        var fixture = await TestFixture.CreateAsync();
        var learner = new UserAccount { Email = "learner@test.com", DisplayName = "Test Learner" };
        await fixture.DbContext.UserAccounts.AddAsync(learner);

        var payment = new PaymentTransaction
        {
            LearnerId = learner.Id,
            Amount = 50m,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_12345",
            CreatedAt = DateTime.UtcNow
        };
        await fixture.DbContext.PaymentTransactions.AddAsync(payment);
        await fixture.DbContext.SaveChangesAsync();

        var refund = new Refund
        {
            PaymentTransactionId = payment.Id,
            Amount = 50m,
            Status = "Processing",
            StripeRefundId = "re_test_12345",
            CreatedAt = DateTime.UtcNow
        };
        await fixture.DbContext.Refunds.AddAsync(refund);
        await fixture.DbContext.SaveChangesAsync();

        // Act
        var result = await fixture.RefundService.GetRefundStatusAsync(refund);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(refund.Id, result.Id);
        Assert.Equal("re_test_12345", result.StripeRefundId);

        await fixture.DisposeAsync();
    }
}
