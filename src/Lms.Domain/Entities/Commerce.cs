using System.ComponentModel.DataAnnotations;

namespace Lms.Domain.Entities;

public class ShoppingCart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearnerId { get; set; }
    public List<CartItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastModifiedAt { get; set; }

    public decimal GetTotal() => Items.Sum(i => i.Price * i.Quantity);
}

public class CartItem
{
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public class PaymentTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearnerId { get; set; }
    public decimal Amount { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "Pending"; // Pending, Completed, Failed, Refunded

    public string? StripePaymentIntentId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? RefundedAt { get; set; }

    public UserAccount? Learner { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<PurchaseLine> PurchaseLines { get; set; } = new List<PurchaseLine>();
    public ICollection<PolicyDisclosureAcknowledgment> PolicyDisclosureAcknowledgments { get; set; } = new List<PolicyDisclosureAcknowledgment>();
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

public class PurchaseLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentTransactionId { get; set; }
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal LineSubtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PaymentTransaction? PaymentTransaction { get; set; }
    public Course? Course { get; set; }
}

public class PolicyDisclosureAcknowledgment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearnerId { get; set; }
    public Guid CourseId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public Guid? EnrollmentId { get; set; }
    public string DisclosureVersion { get; set; } = string.Empty;
    public DateTime DisclosurePublishedAtUtc { get; set; }
    public DateTime AcknowledgedAtUtc { get; set; } = DateTime.UtcNow;
    public string StudentLegalName { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public string ElectronicSignature { get; set; } = string.Empty;
    public string CourseTitle { get; set; } = string.Empty;
    public string? CommissionCourseNumber { get; set; }
    public string DeliveryMethod { get; set; } = string.Empty;
    public int InstructionalMinutes { get; set; }
    public decimal TuitionAndFees { get; set; }
    public decimal ProctoringFee { get; set; }
    public string SupportEmail { get; set; } = string.Empty;
    public string SupportTelephone { get; set; } = string.Empty;
    public string LicenseExaminationPerformanceRecord { get; set; } = string.Empty;
    public string AnnualSummaryReportData { get; set; } = string.Empty;
    public string DisclosureTextSnapshot { get; set; } = string.Empty;

    public UserAccount? Learner { get; set; }
    public Course? Course { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }
    public Enrollment? Enrollment { get; set; }
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentTransactionId { get; set; }
    
    [Required]
    [StringLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public string? PdfUrl { get; set; }
    public string? EmailAddress { get; set; }
    public DateTime? EmailSentAt { get; set; }

    public PaymentTransaction? PaymentTransaction { get; set; }
}

public class Refund
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentTransactionId { get; set; }
    public decimal Amount { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Initiated"; // Initiated, Processing, Succeeded, Failed

    public string? StripeRefundId { get; set; }
    public string? Reason { get; set; }
    public string? FailureReason { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Fraud Detection Fields
    public bool IsFlaggedForReview { get; set; } = false;
    public string? FraudRiskLevel { get; set; } // Low, Medium, High
    public string? FraudReason { get; set; }
    public DateTime? FraudFlaggedAt { get; set; }
    public bool IsApproved { get; set; } = true;

    public PaymentTransaction? PaymentTransaction { get; set; }
}

public class InstructorPayout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstructorId { get; set; }
    public decimal Amount { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Pending"; // Pending, Scheduled, Paid, Failed

    public string? StripeTransferId { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public string? FailureReason { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public UserAccount? Instructor { get; set; }
}

public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearnerId { get; set; }
    public Guid CourseId { get; set; }
    
    [Required]
    [StringLength(20)]
    public string BillingCycle { get; set; } = "Monthly"; // Monthly, Quarterly, Annual

    public decimal AmountPerCycle { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Active"; // Active, Paused, Cancelled, Expired

    public string? StripeSubscriptionId { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CancelledAt { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public UserAccount? Learner { get; set; }
    public Course? Course { get; set; }
}
