using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lms.Application.Data;
using Lms.Domain.Entities;

namespace Lms.Application.Services;

public class RefundService : IRefundService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RefundService> _logger;
    private readonly IEmailService _emailService;

    public RefundService(ApplicationDbContext context, ILogger<RefundService> logger, IEmailService emailService)
    {
        _context = context;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task<RefundResult> InitiateRefundAsync(PaymentTransaction paymentTransaction, string? reason = null)
    {
        try
        {
            if (paymentTransaction.Status != "Completed")
            {
                return new RefundResult
                {
                    Success = false,
                    Message = "Can only refund completed payments",
                    ErrorCode = "INVALID_PAYMENT_STATUS"
                };
            }

            if (string.IsNullOrEmpty(paymentTransaction.StripePaymentIntentId))
            {
                return new RefundResult
                {
                    Success = false,
                    Message = "Payment has no associated Stripe payment intent",
                    ErrorCode = "NO_STRIPE_INTENT"
                };
            }

            // Stub Stripe refund - simulate refund processing
            var stripeRefundId = $"re_test_{Guid.NewGuid().ToString("N").Substring(0, 24)}";

            var refund = new Refund
            {
                PaymentTransactionId = paymentTransaction.Id,
                Amount = paymentTransaction.Amount,
                StripeRefundId = stripeRefundId,
                Status = "Succeeded", // Stub: always succeeds
                Reason = reason,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            _context.Refunds.Add(refund);

            paymentTransaction.Status = "Refunded";
            paymentTransaction.RefundedAt = DateTime.UtcNow;
            _context.PaymentTransactions.Update(paymentTransaction);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Refund initiated for payment {PaymentId}: Stripe refund {RefundId}", 
                paymentTransaction.Id, stripeRefundId);

            // Send refund notification email
            await SendRefundEmailAsync(paymentTransaction, refund);

            return new RefundResult
            {
                Success = true,
                Message = "Refund initiated successfully",
                RefundId = refund.Id,
                StripeRefundId = stripeRefundId,
                RefundedAmount = refund.Amount,
                Status = refund.Status
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating refund for payment {PaymentId}", paymentTransaction.Id);
            return new RefundResult
            {
                Success = false,
                Message = "An error occurred while processing the refund",
                ErrorCode = "INTERNAL_ERROR"
            };
        }
    }

    public async Task<Refund> GetRefundStatusAsync(Refund refund)
    {
        try
        {
            if (string.IsNullOrEmpty(refund.StripeRefundId))
                return refund;

            // Stub: Always return the refund as-is (would query Stripe API in production)
            return refund;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking refund status for {RefundId}", refund.Id);
            return refund;
        }
    }

    public async Task<RefundResult> RefundPaymentAsync(PaymentTransaction paymentTransaction, decimal? amount = null, string? reason = null)
    {
        try
        {
            if (paymentTransaction.Status != "Completed")
            {
                return new RefundResult
                {
                    Success = false,
                    Message = "Can only refund completed payments",
                    ErrorCode = "INVALID_PAYMENT_STATUS"
                };
            }

            var refundAmount = amount ?? paymentTransaction.Amount;

            if (refundAmount <= 0 || refundAmount > paymentTransaction.Amount)
            {
                return new RefundResult
                {
                    Success = false,
                    Message = "Refund amount must be between 0 and the payment amount",
                    ErrorCode = "INVALID_AMOUNT"
                };
            }

            // Stub Stripe refund - create mock refund ID
            var stripeRefundId = $"re_test_{Guid.NewGuid().ToString("N").Substring(0, 24)}";

            var refund = new Refund
            {
                PaymentTransactionId = paymentTransaction.Id,
                Amount = refundAmount,
                StripeRefundId = stripeRefundId,
                Status = "Succeeded", // Stub: always succeeds
                Reason = reason,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            _context.Refunds.Add(refund);

            // Update payment transaction status only if full refund
            if (refundAmount == paymentTransaction.Amount)
            {
                paymentTransaction.Status = "Refunded";
                paymentTransaction.RefundedAt = DateTime.UtcNow;
                _context.PaymentTransactions.Update(paymentTransaction);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Refund processed for payment {PaymentId}: Amount {Amount}, Status {Status}", 
                paymentTransaction.Id, refundAmount, refund.Status);

            // Send refund notification email
            await SendRefundEmailAsync(paymentTransaction, refund);

            return new RefundResult
            {
                Success = true,
                Message = "Refund processed successfully",
                RefundId = refund.Id,
                StripeRefundId = stripeRefundId,
                RefundedAmount = refund.Amount,
                Status = refund.Status
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing refund for payment {PaymentId}", paymentTransaction.Id);
            return new RefundResult
            {
                Success = false,
                Message = "An error occurred while processing the refund",
                ErrorCode = "INTERNAL_ERROR"
            };
        }
    }

    private async Task SendRefundEmailAsync(PaymentTransaction paymentTransaction, Refund refund)
    {
        try
        {
            var learner = await _context.UserAccounts.FindAsync(paymentTransaction.LearnerId);
            if (learner == null) return;

            var subject = "Payment Refund Processed";
            var body = $@"
<h2>Refund Confirmation</h2>
<p>Dear {learner.DisplayName},</p>
<p>Your refund has been processed successfully.</p>
<ul>
    <li><strong>Refund Amount:</strong> ${refund.Amount:F2}</li>
    <li><strong>Status:</strong> {refund.Status}</li>
    <li><strong>Reason:</strong> {refund.Reason ?? "N/A"}</li>
    <li><strong>Date:</strong> {refund.CreatedAt:MMM dd, yyyy}</li>
</ul>
<p>The refund should appear in your account within 5-10 business days.</p>
<p>Thank you!</p>
";

            var emailMessage = new EmailMessage(learner.Email, subject, body);
            await _emailService.SendAsync(emailMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send refund email for refund {RefundId}", refund.Id);
        }
    }
}
