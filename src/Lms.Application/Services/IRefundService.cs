using Lms.Domain.Entities;

namespace Lms.Application.Services;

public interface IRefundService
{
    /// <summary>
    /// Initiates a refund for a completed payment transaction.
    /// </summary>
    /// <param name="paymentTransaction">The payment transaction to refund</param>
    /// <param name="reason">Optional reason for the refund</param>
    /// <returns>Refund entity with status and Stripe refund ID</returns>
    Task<RefundResult> InitiateRefundAsync(PaymentTransaction paymentTransaction, string? reason = null);

    /// <summary>
    /// Gets the status of a refund.
    /// </summary>
    /// <param name="refund">The refund to check</param>
    /// <returns>Updated refund with current status from Stripe</returns>
    Task<Refund> GetRefundStatusAsync(Refund refund);

    /// <summary>
    /// Processes a refund for a payment transaction by amount.
    /// </summary>
    /// <param name="paymentTransaction">The payment transaction</param>
    /// <param name="amount">Amount to refund (default: full amount)</param>
    /// <param name="reason">Reason for refund</param>
    /// <returns>Result with refund details</returns>
    Task<RefundResult> RefundPaymentAsync(PaymentTransaction paymentTransaction, decimal? amount = null, string? reason = null);
}

public class RefundResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Guid? RefundId { get; set; }
    public string? StripeRefundId { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? Status { get; set; }
    public string? ErrorCode { get; set; }
}
