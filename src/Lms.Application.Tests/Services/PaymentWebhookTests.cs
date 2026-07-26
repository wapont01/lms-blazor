using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lms.Application.Tests.Services;

public class PaymentWebhookTests
{
    [Fact]
    public async Task ProcessStripeWebhookAsync_PaymentIntentSucceeded_CreatesCompletedTransaction()
    {
    await using var fixture = await WebhookFixture.CreateAsync();

        var learnerId = Guid.NewGuid();
        var learnerEmail = "webhook-learner@test.local";
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = learnerEmail,
            DisplayName = "Webhook Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var payload = $$"""
{
  "id": "evt_test_1",
  "type": "payment_intent.succeeded",
  "data": {
    "object": {
      "id": "pi_live_test_001",
      "amount": 12999,
      "metadata": {
        "learner_id": "{{learnerId}}",
        "learner_email": "{{learnerEmail}}"
      }
    }
  }
}
""";

        var result = await fixture.Service.ProcessStripeWebhookAsync(payload);

        Assert.True(result.Success);
        Assert.Equal("payment_intent.succeeded", result.EventType);

        var transaction = await fixture.DbContext.PaymentTransactions
            .FirstOrDefaultAsync(t => t.StripePaymentIntentId == "pi_live_test_001");

        Assert.NotNull(transaction);
        Assert.Equal("Completed", transaction.Status);
        Assert.Equal(129.99m, transaction.Amount);
        Assert.Equal(learnerId, transaction.LearnerId);
    }

    [Fact]
    public async Task ProcessStripeWebhookAsync_RepeatedSuccessEvent_IsIdempotent()
    {
      await using var fixture = await WebhookFixture.CreateAsync();

        var learnerId = Guid.NewGuid();
        fixture.DbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = "idem@test.local",
            DisplayName = "Idem Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });

        fixture.DbContext.PaymentTransactions.Add(new PaymentTransaction
        {
            LearnerId = learnerId,
            Amount = 50m,
            Status = "Completed",
            StripePaymentIntentId = "pi_live_test_idem",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var payload = """
{
  "id": "evt_test_2",
  "type": "payment_intent.succeeded",
  "data": {
    "object": {
      "id": "pi_live_test_idem",
      "amount": 5000,
      "metadata": {
        "learner_id": "ignored"
      }
    }
  }
}
""";

        var first = await fixture.Service.ProcessStripeWebhookAsync(payload);
        var second = await fixture.Service.ProcessStripeWebhookAsync(payload);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Contains("idempotent", second.Message, StringComparison.OrdinalIgnoreCase);

        var count = await fixture.DbContext.PaymentTransactions
            .CountAsync(t => t.StripePaymentIntentId == "pi_live_test_idem");
        Assert.Equal(1, count);
    }

      private sealed class WebhookFixture : IAsyncDisposable
      {
        private readonly SqliteConnection _connection;
        public Lms.Application.Data.ApplicationDbContext DbContext { get; }
        public PaymentService Service { get; }

        private WebhookFixture(
          SqliteConnection connection,
          Lms.Application.Data.ApplicationDbContext dbContext,
          PaymentService service)
        {
          _connection = connection;
          DbContext = dbContext;
          Service = service;
        }

        public static async Task<WebhookFixture> CreateAsync()
        {
          var connection = new SqliteConnection("Data Source=:memory:");
          await connection.OpenAsync();

          var options = new DbContextOptionsBuilder<Lms.Application.Data.ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

          var dbContext = new Lms.Application.Data.ApplicationDbContext(options);
          await dbContext.Database.EnsureCreatedAsync();

          var auditLogService = new AuditLogService(dbContext);
          var emailService = new TestEmailService();
          var pdfService = new TestPDFInvoiceService();
          var logger = new TestLogger<PaymentService>();
          var service = new PaymentService(dbContext, logger, auditLogService, emailService, pdfService);

          return new WebhookFixture(connection, dbContext, service);
        }

        public async ValueTask DisposeAsync()
        {
          await DbContext.DisposeAsync();
          await _connection.DisposeAsync();
        }
      }

      private sealed class TestEmailService : IEmailService
      {
        public Task<bool> SendAsync(EmailMessage message) => Task.FromResult(true);
        public Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames) => Task.FromResult(true);
      }

      private sealed class TestPDFInvoiceService : IPDFInvoiceService
      {
        public Task<string> GeneratePdfInvoiceAsync(Invoice invoice, PaymentTransaction transaction, string learnerEmail, List<string> courseNames, decimal taxRate = 0.08m)
          => Task.FromResult($"{invoice.InvoiceNumber}.pdf");
      }

      private sealed class TestLogger<T> : ILogger<T>
      {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        private sealed class NullScope : IDisposable
        {
          public static readonly NullScope Instance = new();
          public void Dispose() { }
        }
      }
}
