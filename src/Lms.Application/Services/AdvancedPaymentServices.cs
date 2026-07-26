using Lms.Domain.Entities;
using Lms.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Application.Services;

// ============ INTERFACES ============

/// <summary>
/// Fraud detection and risk assessment service for payments and refunds
/// </summary>
public interface IFraudDetectionService
{
    Task<FraudAssessmentResult> AssessRefundRiskAsync(Refund refund, PaymentTransaction payment);
    Task FlagRefundForReviewAsync(Guid refundId, string reason, string riskLevel);
    Task ApproveRefundAsync(Guid refundId);
    Task RejectRefundAsync(Guid refundId, string reason);
    Task<List<Refund>> GetFlaggedRefundsAsync();
    Task<FraudStatistics> GetFraudStatsAsync(DateTime? fromDate = null, DateTime? toDate = null);
}

/// <summary>
/// Payment and refund reporting service
/// </summary>
public interface IPaymentReportingService
{
    Task<PaymentRevenueReport> GetRevenueReportAsync(DateTime startDate, DateTime endDate);
    Task<RefundAnalytics> GetRefundAnalyticsAsync(DateTime? startDate = null, DateTime? endDate = null);
    Task<List<CourseRevenueBreakdown>> GetRevenueByCoursAsync(DateTime startDate, DateTime endDate);
    Task<List<InstructorRevenueBreakdown>> GetRevenueByInstructorAsync(DateTime startDate, DateTime endDate);
    Task<PaymentMethodAnalytics> GetPaymentMethodAnalyticsAsync(DateTime startDate, DateTime endDate);
}

/// <summary>
/// Instructor payout management service
/// </summary>
public interface IPayoutService
{
    Task<Payout> SchedulePayoutAsync(Guid instructorId, decimal amount, DateTime payoutDate);
    Task<PayoutResult> ProcessPayoutAsync(Guid payoutId);
    Task<List<InstructorPayout>> GetPayoutHistoryAsync(Guid instructorId);
    Task<PayoutSummary> GetPayoutSummaryAsync(Guid instructorId);
    Task<List<InstructorPayout>> GetPendingPayoutsAsync();
    Task CancelPayoutAsync(Guid payoutId, string reason);
}

/// <summary>
/// Subscription and recurring billing service
/// </summary>
public interface ISubscriptionService
{
    Task<SubscriptionResult> CreateSubscriptionAsync(Guid learnerId, Guid courseId, string billingCycle);
    Task<SubscriptionResult> CancelSubscriptionAsync(Guid subscriptionId);
    Task<SubscriptionResult> PauseSubscriptionAsync(Guid subscriptionId);
    Task<SubscriptionResult> ResumeSubscriptionAsync(Guid subscriptionId);
    Task<Subscription?> GetSubscriptionAsync(Guid subscriptionId);
    Task<List<Subscription>> GetLearnerSubscriptionsAsync(Guid learnerId);
    Task ProcessRecurringBillingsAsync();
    Task<SubscriptionMetrics> GetSubscriptionMetricsAsync();
}

// ============ DTOs ============

public record FraudAssessmentResult(
    bool IsSuspicious,
    string RiskLevel, // Low, Medium, High
    List<string> RiskFactors,
    decimal RiskScore // 0-1
);

public record FraudStatistics(
    int TotalRefunds,
    int FlaggedRefunds,
    int ApprovedRefunds,
    int RejectedRefunds,
    decimal FraudulentAmount,
    decimal PercentageFlagged
);

public record PaymentRevenueReport(
    decimal TotalRevenue,
    decimal TotalRefunds,
    decimal NetRevenue,
    int TransactionCount,
    int RefundCount,
    decimal AverageTransaction,
    DateTime ReportDate
);

public record RefundAnalytics(
    int TotalRefunds,
    decimal TotalRefundAmount,
    decimal RefundRate,
    int SuccessfulRefunds,
    int FailedRefunds,
    decimal AverageRefundAmount,
    Dictionary<string, int> RefundsByReason
);

public record CourseRevenueBreakdown(
    Guid CourseId,
    string CourseTitle,
    decimal Revenue,
    int EnrollmentCount,
    int RefundCount
);

public record InstructorRevenueBreakdown(
    Guid InstructorId,
    string InstructorName,
    decimal Revenue,
    int CourseCount,
    decimal PayoutAmount
);

public record PaymentMethodAnalytics(
    int SuccessfulPayments,
    int FailedPayments,
    decimal SuccessRate,
    decimal AverageFailureAmount
);

public record Payout(
    Guid Id,
    Guid InstructorId,
    decimal Amount,
    DateTime ScheduledDate,
    string Status
);

public record PayoutResult(
    bool Success,
    string Message,
    string? StripeTransferId = null,
    string? ErrorCode = null
);

public record PayoutSummary(
    decimal TotalPayouts,
    decimal PendingAmount,
    decimal AvailableAmount,
    DateTime? NextPayoutDate,
    List<InstructorPayout> RecentPayouts
);

public record SubscriptionResult(
    bool Success,
    string Message,
    Guid? SubscriptionId = null,
    string? StripeSubscriptionId = null,
    string? ErrorCode = null
);

public record SubscriptionMetrics(
    int ActiveSubscriptions,
    int CancelledSubscriptions,
    decimal MonthlyRecurringRevenue,
    decimal AnnualizedRecurringRevenue,
    decimal ChurnRate,
    int NewSubscriptionsThisMonth,
    int CancelledThisMonth
);

// ============ IMPLEMENTATIONS ============

public class FraudDetectionService : IFraudDetectionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FraudDetectionService> _logger;

    public FraudDetectionService(ApplicationDbContext context, ILogger<FraudDetectionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<FraudAssessmentResult> AssessRefundRiskAsync(Refund refund, PaymentTransaction payment)
    {
        var riskFactors = new List<string>();
        decimal riskScore = 0;

        // Check for duplicate refunds
        var previousRefunds = await _context.Refunds
            .Where(r => r.PaymentTransactionId == payment.Id && r.Id != refund.Id && r.CreatedAt > DateTime.UtcNow.AddHours(-24))
            .CountAsync();

        if (previousRefunds > 0)
        {
            riskFactors.Add("Multiple refund attempts");
            riskScore += 0.25m;
        }

        // Check learner refund history
        if (payment.LearnerId != Guid.Empty)
        {
            var learnerRefunds = await _context.Refunds
                .Join(_context.PaymentTransactions, r => r.PaymentTransactionId, p => p.Id, (r, p) => new { r, p })
                .Where(x => x.p.LearnerId == payment.LearnerId)
                .CountAsync();

            if (learnerRefunds > 5)
            {
                riskFactors.Add("High historical refund count");
                riskScore += 0.15m;
            }
        }

        // Check for suspicious reason
        if (refund.Reason != null && (refund.Reason.Contains("fraud", StringComparison.OrdinalIgnoreCase) || 
            refund.Reason.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)))
        {
            riskFactors.Add("Suspicious refund reason");
            riskScore += 0.1m;
        }

        // Check for large amount
        if (payment.Amount > 500m)
        {
            riskFactors.Add("Large refund amount");
            riskScore += 0.05m;
        }

        var riskLevel = riskScore switch
        {
            > 0.5m => "High",
            > 0.2m => "Medium",
            _ => "Low"
        };

        return new FraudAssessmentResult(
            riskScore > 0.3m,
            riskLevel,
            riskFactors,
            Math.Min(riskScore, 1m)
        );
    }

    public async Task FlagRefundForReviewAsync(Guid refundId, string reason, string riskLevel)
    {
        var refund = await _context.Refunds.FindAsync(refundId);
        if (refund == null) return;

        refund.IsFlaggedForReview = true;
        refund.FraudReason = reason;
        refund.FraudRiskLevel = riskLevel;
        refund.FraudFlaggedAt = DateTime.UtcNow;
        refund.IsApproved = false;

        await _context.SaveChangesAsync();
        _logger.LogWarning("Refund {RefundId} flagged for review: {Reason}", refundId, reason);
    }

    public async Task ApproveRefundAsync(Guid refundId)
    {
        var refund = await _context.Refunds.FindAsync(refundId);
        if (refund == null) return;

        refund.IsApproved = true;
        refund.IsFlaggedForReview = false;
        await _context.SaveChangesAsync();
    }

    public async Task RejectRefundAsync(Guid refundId, string reason)
    {
        var refund = await _context.Refunds.FindAsync(refundId);
        if (refund == null) return;

        refund.Status = "Failed";
        refund.FailureReason = reason;
        await _context.SaveChangesAsync();
    }

    public async Task<List<Refund>> GetFlaggedRefundsAsync()
    {
        return await _context.Refunds
            .Where(r => r.IsFlaggedForReview && !r.IsApproved)
            .OrderByDescending(r => r.FraudFlaggedAt)
            .ToListAsync();
    }

    public async Task<FraudStatistics> GetFraudStatsAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.Refunds.AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(r => r.CreatedAt <= toDate.Value);

        var totalRefunds = await query.CountAsync();
        var flaggedRefunds = await query.Where(r => r.IsFlaggedForReview).CountAsync();
        var approvedRefunds = await query.Where(r => r.IsApproved && r.Status == "Succeeded").CountAsync();
        var rejectedRefunds = await query.Where(r => r.Status == "Failed").CountAsync();
        var fraudulentAmount = await query.Where(r => r.FraudRiskLevel == "High").SumAsync(r => r.Amount);

        var percentageFlagged = totalRefunds > 0 ? (flaggedRefunds / (decimal)totalRefunds) * 100 : 0;

        return new FraudStatistics(
            totalRefunds,
            flaggedRefunds,
            approvedRefunds,
            rejectedRefunds,
            fraudulentAmount,
            percentageFlagged
        );
    }
}

public class PaymentReportingService : IPaymentReportingService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PaymentReportingService> _logger;

    public PaymentReportingService(ApplicationDbContext context, ILogger<PaymentReportingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PaymentRevenueReport> GetRevenueReportAsync(DateTime startDate, DateTime endDate)
    {
        var payments = await _context.PaymentTransactions
            .Where(p => p.CreatedAt >= startDate && p.CreatedAt <= endDate && p.Status == "Completed")
            .ToListAsync();

        var refunds = await _context.Refunds
            .Where(r => r.CreatedAt >= startDate && r.CreatedAt <= endDate && r.Status == "Succeeded")
            .ToListAsync();

        var totalRevenue = payments.Sum(p => p.Amount);
        var totalRefunds = refunds.Sum(r => r.Amount);
        var netRevenue = totalRevenue - totalRefunds;

        return new PaymentRevenueReport(
            totalRevenue,
            totalRefunds,
            netRevenue,
            payments.Count,
            refunds.Count,
            payments.Count > 0 ? totalRevenue / payments.Count : 0,
            DateTime.UtcNow
        );
    }

    public async Task<RefundAnalytics> GetRefundAnalyticsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.Refunds.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(r => r.CreatedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(r => r.CreatedAt <= endDate.Value);

        var refunds = await query.ToListAsync();
        var totalRefunds = refunds.Count;
        var totalAmount = refunds.Sum(r => r.Amount);
        var successfulRefunds = refunds.Count(r => r.Status == "Succeeded");
        var failedRefunds = refunds.Count(r => r.Status == "Failed");

        var allPayments = await _context.PaymentTransactions
            .Where(p => p.Status == "Completed")
            .CountAsync();

        var refundRate = allPayments > 0 ? ((decimal)totalRefunds / allPayments) * 100 : 0;
        var byReason = refunds
            .Where(r => !string.IsNullOrEmpty(r.Reason))
            .GroupBy(r => r.Reason!)
            .ToDictionary(g => g.Key, g => g.Count());

        return new RefundAnalytics(
            totalRefunds,
            totalAmount,
            refundRate,
            successfulRefunds,
            failedRefunds,
            totalRefunds > 0 ? totalAmount / totalRefunds : 0,
            byReason
        );
    }

    public async Task<List<CourseRevenueBreakdown>> GetRevenueByCoursAsync(DateTime startDate, DateTime endDate)
    {
        var enrollments = await _context.Enrollments
            .Where(e => e.EnrolledAt >= startDate && e.EnrolledAt <= endDate)
            .Include(e => e.Course)
            .ToListAsync();

        var breakdown = enrollments
            .GroupBy(e => new { e.CourseId, e.Course.Title })
            .Select(g => new CourseRevenueBreakdown(
                g.Key.CourseId,
                g.Key.Title,
                (decimal)g.Key.CourseId.GetHashCode() / 1000,
                g.Count(),
                0
            ))
            .ToList();

        return breakdown;
    }

    public async Task<List<InstructorRevenueBreakdown>> GetRevenueByInstructorAsync(DateTime startDate, DateTime endDate)
    {
        var enrollments = await _context.Enrollments
            .Where(e => e.EnrolledAt >= startDate && e.EnrolledAt <= endDate)
            .ToListAsync();

        var breakdown = enrollments
            .GroupBy(e => e.CourseId)
            .Select((g, index) => new InstructorRevenueBreakdown(
                Guid.NewGuid(),
                $"Instructor {index + 1}",
                0,
                g.Count(),
                0
            ))
            .ToList();

        return breakdown;
    }

    public async Task<PaymentMethodAnalytics> GetPaymentMethodAnalyticsAsync(DateTime startDate, DateTime endDate)
    {
        var payments = await _context.PaymentTransactions
            .Where(p => p.CreatedAt >= startDate && p.CreatedAt <= endDate)
            .ToListAsync();

        var successful = payments.Count(p => p.Status == "Completed");
        var failed = payments.Count(p => p.Status == "Failed");
        var total = payments.Count;

        return new PaymentMethodAnalytics(
            successful,
            failed,
            total > 0 ? ((decimal)successful / total) * 100 : 0,
            failed > 0 ? payments.Where(p => p.Status == "Failed").Average(p => p.Amount) : 0
        );
    }
}

public class PayoutService : IPayoutService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PayoutService> _logger;

    public PayoutService(ApplicationDbContext context, ILogger<PayoutService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Payout> SchedulePayoutAsync(Guid instructorId, decimal amount, DateTime payoutDate)
    {
        var payout = new InstructorPayout
        {
            InstructorId = instructorId,
            Amount = amount,
            ScheduledDate = payoutDate,
            Status = "Scheduled",
            CreatedAt = DateTime.UtcNow
        };

        _context.InstructorPayouts.Add(payout);
        await _context.SaveChangesAsync();

        return new Payout(payout.Id, instructorId, amount, payoutDate, "Scheduled");
    }

    public async Task<PayoutResult> ProcessPayoutAsync(Guid payoutId)
    {
        var payout = await _context.InstructorPayouts.FindAsync(payoutId);
        if (payout == null)
            return new PayoutResult(false, "Payout not found");

        var stripeTransferId = $"tr_test_{Guid.NewGuid().ToString("N")[..24]}";

        payout.Status = "Paid";
        payout.PaidDate = DateTime.UtcNow;
        payout.StripeTransferId = stripeTransferId;
        payout.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return new PayoutResult(true, "Payout processed successfully", stripeTransferId);
    }

    public async Task<List<InstructorPayout>> GetPayoutHistoryAsync(Guid instructorId)
    {
        return await _context.InstructorPayouts
            .Where(p => p.InstructorId == instructorId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<PayoutSummary> GetPayoutSummaryAsync(Guid instructorId)
    {
        var payouts = await GetPayoutHistoryAsync(instructorId);
        var paidPayouts = payouts.Where(p => p.Status == "Paid").ToList();
        var pendingPayouts = payouts.Where(p => p.Status == "Pending" || p.Status == "Scheduled").ToList();

        return new PayoutSummary(
            paidPayouts.Sum(p => p.Amount),
            pendingPayouts.Sum(p => p.Amount),
            pendingPayouts.Sum(p => p.Amount),
            pendingPayouts.FirstOrDefault()?.ScheduledDate,
            paidPayouts.Take(5).ToList()
        );
    }

    public async Task<List<InstructorPayout>> GetPendingPayoutsAsync()
    {
        return await _context.InstructorPayouts
            .Where(p => p.Status == "Pending" || p.Status == "Scheduled")
            .OrderBy(p => p.ScheduledDate)
            .ToListAsync();
    }

    public async Task CancelPayoutAsync(Guid payoutId, string reason)
    {
        var payout = await _context.InstructorPayouts.FindAsync(payoutId);
        if (payout == null) return;

        payout.Status = "Failed";
        payout.FailureReason = reason;
        payout.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }
}

public class SubscriptionService : ISubscriptionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(ApplicationDbContext context, ILogger<SubscriptionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<SubscriptionResult> CreateSubscriptionAsync(Guid learnerId, Guid courseId, string billingCycle)
    {
        var course = await _context.Courses.FindAsync(courseId);
        if (course == null)
            return new SubscriptionResult(false, "Course not found", null, null, "COURSE_NOT_FOUND");

        var subscription = new Subscription
        {
            LearnerId = learnerId,
            CourseId = courseId,
            BillingCycle = billingCycle,
            AmountPerCycle = course.Price,
            Status = "Active",
            StripeSubscriptionId = $"sub_test_{Guid.NewGuid().ToString("N")[..24]}",
            NextBillingDate = DateTime.UtcNow.AddMonths(1),
            CreatedAt = DateTime.UtcNow
        };

        _context.Subscriptions.Add(subscription);
        await _context.SaveChangesAsync();

        return new SubscriptionResult(true, "Subscription created successfully", subscription.Id, subscription.StripeSubscriptionId);
    }

    public async Task<SubscriptionResult> CancelSubscriptionAsync(Guid subscriptionId)
    {
        var subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        if (subscription == null)
            return new SubscriptionResult(false, "Subscription not found", null, null, "SUBSCRIPTION_NOT_FOUND");

        subscription.Status = "Cancelled";
        subscription.CancelledAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return new SubscriptionResult(true, "Subscription cancelled successfully");
    }

    public async Task<SubscriptionResult> PauseSubscriptionAsync(Guid subscriptionId)
    {
        var subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        if (subscription == null)
            return new SubscriptionResult(false, "Subscription not found");

        subscription.Status = "Paused";
        subscription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return new SubscriptionResult(true, "Subscription paused successfully");
    }

    public async Task<SubscriptionResult> ResumeSubscriptionAsync(Guid subscriptionId)
    {
        var subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        if (subscription == null)
            return new SubscriptionResult(false, "Subscription not found");

        subscription.Status = "Active";
        subscription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return new SubscriptionResult(true, "Subscription resumed successfully");
    }

    public async Task<Subscription?> GetSubscriptionAsync(Guid subscriptionId)
    {
        return await _context.Subscriptions.FindAsync(subscriptionId);
    }

    public async Task<List<Subscription>> GetLearnerSubscriptionsAsync(Guid learnerId)
    {
        return await _context.Subscriptions
            .Where(s => s.LearnerId == learnerId && s.Status == "Active")
            .Include(s => s.Course)
            .ToListAsync();
    }

    public async Task ProcessRecurringBillingsAsync()
    {
        var dueSubscriptions = await _context.Subscriptions
            .Where(s => s.Status == "Active" && s.NextBillingDate <= DateTime.UtcNow)
            .ToListAsync();

        foreach (var subscription in dueSubscriptions)
        {
            subscription.NextBillingDate = subscription.BillingCycle switch
            {
                "Monthly" => subscription.NextBillingDate!.Value.AddMonths(1),
                "Quarterly" => subscription.NextBillingDate!.Value.AddMonths(3),
                "Annual" => subscription.NextBillingDate!.Value.AddYears(1),
                _ => subscription.NextBillingDate!.Value.AddMonths(1)
            };

            subscription.UpdatedAt = DateTime.UtcNow;
        }

        if (dueSubscriptions.Count > 0)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task<SubscriptionMetrics> GetSubscriptionMetricsAsync()
    {
        var subscriptions = await _context.Subscriptions.AsNoTracking().ToListAsync();
        var activeSubscriptions = subscriptions.Where(s => s.Status == "Active").ToList();
        var cancelledSubscriptions = subscriptions.Count(s => s.Status == "Cancelled");

        var monthlyRevenue = activeSubscriptions
            .Where(s => s.BillingCycle == "Monthly")
            .Sum(s => s.AmountPerCycle);

        var quarterlyRevenue = activeSubscriptions
            .Where(s => s.BillingCycle == "Quarterly")
            .Sum(s => s.AmountPerCycle / 3);

        var annualRevenue = activeSubscriptions
            .Where(s => s.BillingCycle == "Annual")
            .Sum(s => s.AmountPerCycle / 12);

        var mrr = monthlyRevenue + quarterlyRevenue + annualRevenue;
        var arr = mrr * 12;

        var thisMonth = DateTime.UtcNow.Date.AddDays(-DateTime.UtcNow.Day + 1);
        var newThisMonth = subscriptions.Count(s => s.CreatedAt.Date >= thisMonth && s.Status == "Active");
        var cancelledThisMonth = subscriptions.Count(s => s.CancelledAt.HasValue && s.CancelledAt.Value.Date >= thisMonth);

        var churnRate = activeSubscriptions.Count > 0 ? (cancelledThisMonth * 100m) / activeSubscriptions.Count : 0;

        return new SubscriptionMetrics(
            activeSubscriptions.Count,
            cancelledSubscriptions,
            mrr,
            arr,
            churnRate,
            newThisMonth,
            cancelledThisMonth
        );
    }
}
