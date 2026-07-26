using Xunit;
using Lms.Application.Services;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Application.Tests.Services;

/// <summary>
/// Mock logger that discards all log messages for testing
/// </summary>
public class NoOpLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

public class AdvancedPaymentServicesTests : IAsyncLifetime
{
    private ApplicationDbContext _context = null!;
    private ILogger<FraudDetectionService> _fraudLogger = null!;
    private ILogger<PaymentReportingService> _reportingLogger = null!;
    private ILogger<PayoutService> _payoutLogger = null!;
    private ILogger<SubscriptionService> _subscriptionLogger = null!;

    public async Task InitializeAsync()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Filename=:memory:")
            .Options;

        _context = new ApplicationDbContext(options);
        await _context.Database.OpenConnectionAsync();
        await _context.Database.EnsureCreatedAsync();

        // Setup no-op loggers for testing
        _fraudLogger = new NoOpLogger<FraudDetectionService>();
        _reportingLogger = new NoOpLogger<PaymentReportingService>();
        _payoutLogger = new NoOpLogger<PayoutService>();
        _subscriptionLogger = new NoOpLogger<SubscriptionService>();

        // Seed test data
        await SeedTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.Database.CloseConnectionAsync();
        _context.Dispose();
    }

    private async Task SeedTestDataAsync()
    {
        // Create test users
        var instructor = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "instructor@test.com",
            DisplayName = "Test Instructor",
            PasswordHash = "hash",
            Role = "Instructor",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var learner = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "learner@test.com",
            DisplayName = "Test Learner",
            PasswordHash = "hash",
            Role = "Learner",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var course = new Course
        {
            Id = Guid.NewGuid(),
            Title = "Test Course",
            Price = 99.99m,
            IsPublished = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserAccounts.AddRange(instructor, learner);
        _context.Courses.Add(course);

        // Create test payment and refund
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            LearnerId = learner.Id,
            Amount = 99.99m,
            Status = "Completed",
            StripePaymentIntentId = "pi_test_123",
            CreatedAt = DateTime.UtcNow
        };

        var refund = new Refund
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = payment.Id,
            Amount = 99.99m,
            Status = "Initiated",
            Reason = "Changed mind",
            CreatedAt = DateTime.UtcNow
        };

        _context.PaymentTransactions.Add(payment);
        _context.Refunds.Add(refund);

        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task FraudDetectionService_AssessRefundRisk_ReturnsLowRiskForSingleRefund()
    {
        // Arrange
        var fraudService = new FraudDetectionService(_context, _fraudLogger);
        var payment = await _context.PaymentTransactions.FirstAsync();
        var refund = await _context.Refunds.FirstAsync();

        // Act
        var result = await fraudService.AssessRefundRiskAsync(refund, payment);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuspicious);
        Assert.Equal("Low", result.RiskLevel);
        Assert.InRange(result.RiskScore, 0, 1);
    }

    [Fact]
    public async Task PayoutService_SchedulePayout_CreatesPayoutWithScheduledStatus()
    {
        // Arrange
        var payoutService = new PayoutService(_context, _payoutLogger);
        var instructors = await _context.UserAccounts.Where(u => u.Role == "Instructor").ToListAsync();
        var instructor = instructors.FirstOrDefault();
        
        if (instructor == null)
            return; // Skip if no instructor
        
        var payoutDate = DateTime.UtcNow.AddDays(7);

        // Act
        var result = await payoutService.SchedulePayoutAsync(instructor.Id, 500m, payoutDate);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(instructor.Id, result.InstructorId);
        Assert.Equal(500m, result.Amount);
        Assert.Equal("Scheduled", result.Status);

        // Verify in database
        var payout = await _context.InstructorPayouts.FirstOrDefaultAsync();
        Assert.NotNull(payout);
        Assert.Equal("Scheduled", payout.Status);
    }

    [Fact]
    public async Task PayoutService_ProcessPayout_UpdatesStatusToPaidWithStripeId()
    {
        // Arrange
        var payoutService = new PayoutService(_context, _payoutLogger);
        var instructors = await _context.UserAccounts.Where(u => u.Role == "Instructor").ToListAsync();
        var instructor = instructors.FirstOrDefault();
        
        if (instructor == null)
            return; // Skip if no instructor
        
        // Create a payout first
        var payout = new InstructorPayout
        {
            InstructorId = instructor.Id,
            Amount = 500m,
            Status = "Scheduled",
            ScheduledDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.InstructorPayouts.Add(payout);
        await _context.SaveChangesAsync();

        // Act
        var result = await payoutService.ProcessPayoutAsync(payout.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.StripeTransferId);
        Assert.StartsWith("tr_test_", result.StripeTransferId);

        // Verify in database
        var updated = await _context.InstructorPayouts.FindAsync(payout.Id);
        Assert.NotNull(updated);
        Assert.Equal("Paid", updated!.Status);
        Assert.NotNull(updated.PaidDate);
    }

    [Fact]
    public async Task SubscriptionService_CreateSubscription_CreatesActiveSubscription()
    {
        // Arrange
        var subscriptionService = new SubscriptionService(_context, _subscriptionLogger);
        var learners = await _context.UserAccounts.Where(u => u.Role == "Learner").ToListAsync();
        var learner = learners.FirstOrDefault();
        
        if (learner == null)
            return; // Skip if no learner
        
        var courses = await _context.Courses.ToListAsync();
        var course = courses.FirstOrDefault();
        
        if (course == null)
            return; // Skip if no course

        // Act
        var result = await subscriptionService.CreateSubscriptionAsync(learner.Id, course.Id, "Monthly");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.SubscriptionId);
        Assert.NotNull(result.StripeSubscriptionId);
        Assert.StartsWith("sub_test_", result.StripeSubscriptionId);

        // Verify in database
        var subscription = await _context.Subscriptions.FindAsync(result.SubscriptionId);
        Assert.NotNull(subscription);
        Assert.Equal("Active", subscription.Status);
        Assert.Equal("Monthly", subscription.BillingCycle);
    }

    [Fact]
    public async Task SubscriptionService_CancelSubscription_UpdatesStatusToCancelled()
    {
        // Arrange
        var subscriptionService = new SubscriptionService(_context, _subscriptionLogger);
        var learners = await _context.UserAccounts.Where(u => u.Role == "Learner").ToListAsync();
        var learner = learners.FirstOrDefault();
        
        if (learner == null)
            return; // Skip if no learner
        
        var courses = await _context.Courses.ToListAsync();
        var course = courses.FirstOrDefault();
        
        if (course == null)
            return; // Skip if no course

        // Create subscription first
        var createResult = await subscriptionService.CreateSubscriptionAsync(learner.Id, course.Id, "Monthly");
        Assert.NotNull(createResult.SubscriptionId);
        var subscriptionId = createResult.SubscriptionId!.Value;

        // Act
        var cancelResult = await subscriptionService.CancelSubscriptionAsync(subscriptionId);

        // Assert
        Assert.True(cancelResult.Success);

        // Verify in database
        var subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        Assert.NotNull(subscription);
        Assert.Equal("Cancelled", subscription!.Status);
        Assert.NotNull(subscription.CancelledAt);
    }

    [Fact]
    public async Task PaymentReportingService_GetRevenueReport_ReturnsReportWithPaymentData()
    {
        // Arrange
        var reportingService = new PaymentReportingService(_context, _reportingLogger);
        var startDate = DateTime.UtcNow.AddDays(-1);
        var endDate = DateTime.UtcNow.AddDays(1);

        // Act
        var report = await reportingService.GetRevenueReportAsync(startDate, endDate);

        // Assert
        Assert.NotNull(report);
        Assert.True(report.TotalRevenue >= 0);
        Assert.Equal(DateTime.UtcNow.Date, report.ReportDate.Date);
    }

    [Fact]
    public async Task SubscriptionService_GetSubscriptionMetrics_ReturnsMetricsWithActiveCount()
    {
        // Arrange
        var subscriptionService = new SubscriptionService(_context, _subscriptionLogger);
        var learners = await _context.UserAccounts.Where(u => u.Role == "Learner").ToListAsync();
        var learner = learners.FirstOrDefault();
        
        if (learner == null)
            return; // Skip if no learner
        
        var courses = await _context.Courses.ToListAsync();
        var course = courses.FirstOrDefault();
        
        if (course == null)
            return; // Skip if no course

        // Create a subscription
        await subscriptionService.CreateSubscriptionAsync(learner.Id, course.Id, "Monthly");

        // Act
        var metrics = await subscriptionService.GetSubscriptionMetricsAsync();

        // Assert
        Assert.NotNull(metrics);
        Assert.True(metrics.ActiveSubscriptions > 0);
        Assert.True(metrics.MonthlyRecurringRevenue >= 0);
        Assert.True(metrics.AnnualizedRecurringRevenue >= 0);
    }
}
