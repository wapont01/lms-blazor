using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lms.Application.Data;
using Lms.Domain.Entities;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lms.Application.Services;

public record PaymentResult(bool Success, string Message, string? StripePaymentIntentId = null, string? ErrorCode = null);
public record WebhookProcessResult(bool Success, string Message, string? EventType = null, string? StripePaymentIntentId = null);

public interface IPaymentService
{
    Task<PaymentResult> ProcessPaymentAsync(Guid learnerId, decimal amount, string paymentMethodId, string email);
    Task<PaymentResult> RefundPaymentAsync(string stripePaymentIntentId);
    Task<Invoice> GenerateInvoiceAsync(PaymentTransaction transaction, List<string> courseNames, string learnerEmail, decimal taxRate = 0.08m);
    Task<WebhookProcessResult> ProcessStripeWebhookAsync(string payload, string? stripeSignatureHeader = null);
}

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PaymentService> _logger;
    private readonly IAuditLogService _auditLogService;
    private readonly IEmailService _emailService;
    private readonly IPDFInvoiceService _pdfInvoiceService;
    private readonly string? _stripeSecretKey;
    private readonly string? _stripeWebhookSecret;
    private readonly bool _stripeApiEnabled;

    public PaymentService(ApplicationDbContext dbContext, ILogger<PaymentService> logger, IAuditLogService auditLogService, IEmailService emailService, IPDFInvoiceService pdfInvoiceService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _auditLogService = auditLogService;
        _emailService = emailService;
        _pdfInvoiceService = pdfInvoiceService;
        _stripeSecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        _stripeWebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
        _stripeApiEnabled = string.Equals(Environment.GetEnvironmentVariable("STRIPE_ENABLE_DIRECT_API"), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("STRIPE_ENABLE_DIRECT_API"), "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PaymentResult> ProcessPaymentAsync(Guid learnerId, decimal amount, string paymentMethodId, string email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(paymentMethodId))
                return new PaymentResult(false, "Payment method is required.");

            if (amount <= 0)
                return new PaymentResult(false, "Invalid payment amount.");

            // Simulate failures for Stripe test decline cards
            // 4000000000000002: Card declined
            // 4000002500003155: Card declined
            var isDeclined = paymentMethodId == "4000000000000002" || paymentMethodId == "4000002500003155";

            if (isDeclined)
            {
                var declinedStripeIntentId = $"pi_test_{Guid.NewGuid().ToString("N")[..24]}";
                var failedTransaction = new PaymentTransaction
                {
                    LearnerId = learnerId,
                    Amount = amount,
                    Status = "Failed",
                    StripePaymentIntentId = declinedStripeIntentId,
                    FailureReason = "Test card declined",
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.PaymentTransactions.Add(failedTransaction);
                await _dbContext.SaveChangesAsync();

                await _auditLogService.WriteAsync(learnerId, email, "payment.failed", "PaymentTransaction", failedTransaction.Id, $"Intent: {declinedStripeIntentId}");

                _logger.LogWarning("Payment failed for learner {LearnerId}: {StripeIntentId}", learnerId, declinedStripeIntentId);
                return new PaymentResult(false, "Payment declined. Please try another payment method.", null, "card_declined");
            }

            string stripeIntentId;
            bool paymentSucceeded;
            string? failureReason = null;
            string? errorCode = null;

            var canCallStripeDirectly = _stripeApiEnabled
                && !string.IsNullOrWhiteSpace(_stripeSecretKey)
                && paymentMethodId.StartsWith("pm_", StringComparison.OrdinalIgnoreCase);

            if (canCallStripeDirectly)
            {
                var stripeResult = await CreateStripePaymentIntentAsync(learnerId, amount, paymentMethodId, email);
                stripeIntentId = stripeResult.StripePaymentIntentId ?? $"pi_test_{Guid.NewGuid().ToString("N")[..24]}";
                paymentSucceeded = stripeResult.Success;
                failureReason = stripeResult.Message;
                errorCode = stripeResult.ErrorCode;
            }
            else
            {
                // Keep deterministic test behavior when Stripe direct API mode is not configured.
                await Task.Delay(500);
                stripeIntentId = $"pi_test_{Guid.NewGuid().ToString("N")[..24]}";
                paymentSucceeded = true;
            }

            if (!paymentSucceeded)
            {
                var failedTransaction = new PaymentTransaction
                {
                    LearnerId = learnerId,
                    Amount = amount,
                    Status = "Failed",
                    StripePaymentIntentId = stripeIntentId,
                    FailureReason = failureReason ?? "Payment failed",
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.PaymentTransactions.Add(failedTransaction);
                await _dbContext.SaveChangesAsync();

                await _auditLogService.WriteAsync(learnerId, email, "payment.failed", "PaymentTransaction", failedTransaction.Id, $"Intent: {stripeIntentId}");
                return new PaymentResult(false, failureReason ?? "Payment failed.", null, errorCode ?? "payment_failed");
            }

            // Create successful transaction
            var transaction = new PaymentTransaction
            {
                LearnerId = learnerId,
                Amount = amount,
                Status = "Completed",
                StripePaymentIntentId = stripeIntentId,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            _dbContext.PaymentTransactions.Add(transaction);
            await _dbContext.SaveChangesAsync();

            await _auditLogService.WriteAsync(learnerId, email, "payment.completed", "PaymentTransaction", transaction.Id, $"Amount: ${amount}, Intent: {stripeIntentId}");

            _logger.LogInformation("Payment completed for learner {LearnerId}: {StripeIntentId}", learnerId, stripeIntentId);
            
            return new PaymentResult(true, "Payment successful!", stripeIntentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Payment processing error for learner {learnerId}");
            await _auditLogService.WriteAsync(learnerId, email, "payment.error", "PaymentTransaction", null, $"Error: {ex.Message}");
            return new PaymentResult(false, "Payment processing failed. Please try again.", null, "processing_error");
        }
    }

    public async Task<PaymentResult> RefundPaymentAsync(string stripePaymentIntentId)
    {
        try
        {
            var transaction = await _dbContext.PaymentTransactions
                .FirstOrDefaultAsync(t => t.StripePaymentIntentId == stripePaymentIntentId);

            if (transaction == null)
                return new PaymentResult(false, "Transaction not found.");

            if (_stripeApiEnabled && !string.IsNullOrWhiteSpace(_stripeSecretKey) && !stripePaymentIntentId.StartsWith("pi_test_", StringComparison.OrdinalIgnoreCase))
            {
                var refundResult = await CreateStripeRefundAsync(stripePaymentIntentId);
                if (!refundResult.Success)
                {
                    return new PaymentResult(false, refundResult.Message, null, refundResult.ErrorCode);
                }
            }

            transaction.Status = "Refunded";
            transaction.RefundedAt = DateTime.UtcNow;

            _dbContext.PaymentTransactions.Update(transaction);
            await _dbContext.SaveChangesAsync();

            await _auditLogService.WriteAsync(transaction.LearnerId, null, "payment.refunded", "PaymentTransaction", transaction.Id, $"Intent: {stripePaymentIntentId}");

            _logger.LogInformation($"Refund processed: {stripePaymentIntentId}");
            return new PaymentResult(true, "Refund processed successfully.", stripePaymentIntentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Refund processing error: {stripePaymentIntentId}");
            return new PaymentResult(false, "Refund processing failed.", null, "refund_error");
        }
    }

    public async Task<Invoice> GenerateInvoiceAsync(PaymentTransaction transaction, List<string> courseNames, string learnerEmail, decimal taxRate = 0.08m)
    {
        try
        {
            // Generate invoice number (format: INV-YYYYMMDD-XXXXX)
            var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(10000, 99999)}";

            var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Id == transaction.LearnerId);
            
            var invoice = new Invoice
            {
                PaymentTransactionId = transaction.Id,
                InvoiceNumber = invoiceNumber,
                IssuedAt = DateTime.UtcNow,
                EmailAddress = user?.Email ?? learnerEmail,
                EmailSentAt = DateTime.UtcNow
            };

            // Generate PDF invoice
            try
            {
                var pdfFileName = await _pdfInvoiceService.GeneratePdfInvoiceAsync(invoice, transaction, learnerEmail, courseNames, taxRate);
                invoice.PdfUrl = $"/App_Data/Invoices/{pdfFileName}";
                _logger.LogInformation($"PDF invoice generated and stored: {invoice.PdfUrl}");
            }
            catch (Exception pdfEx)
            {
                _logger.LogWarning(pdfEx, $"PDF generation failed for invoice {invoiceNumber}, continuing without PDF");
                // Continue processing without PDF - don't fail the invoice generation
            }

            _dbContext.Invoices.Add(invoice);
            await _dbContext.SaveChangesAsync();

            // Send receipt email
            if (!string.IsNullOrWhiteSpace(user?.Email))
            {
                var emailSent = await _emailService.SendReceiptAsync(
                    user.Email,
                    invoiceNumber,
                    transaction.Amount,
                    courseNames
                );

                if (!emailSent)
                {
                    _logger.LogWarning($"Receipt email failed to send for invoice {invoiceNumber}, but invoice was created");
                }
            }

            await _auditLogService.WriteAsync(transaction.LearnerId, user?.Email, "receipt.generated", "Invoice", invoice.Id, $"InvoiceNumber: {invoiceNumber}, Amount: ${transaction.Amount}");

            _logger.LogInformation($"Invoice generated: {invoiceNumber} for transaction {transaction.Id}");
            return invoice;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Invoice generation error for transaction {transaction.Id}");
            throw;
        }
    }

    public async Task<WebhookProcessResult> ProcessStripeWebhookAsync(string payload, string? stripeSignatureHeader = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new WebhookProcessResult(false, "Webhook payload is empty.");
            }

            if (!string.IsNullOrWhiteSpace(_stripeWebhookSecret))
            {
                var isValidSignature = VerifyStripeWebhookSignature(payload, stripeSignatureHeader, _stripeWebhookSecret);
                if (!isValidSignature)
                {
                    _logger.LogWarning("Rejected Stripe webhook due to invalid signature.");
                    return new WebhookProcessResult(false, "Invalid Stripe signature.");
                }
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var eventType = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

            if (string.IsNullOrWhiteSpace(eventType))
            {
                return new WebhookProcessResult(false, "Missing Stripe event type.");
            }

            switch (eventType)
            {
                case "payment_intent.succeeded":
                    return await HandlePaymentIntentSucceededAsync(root, eventType);
                case "payment_intent.payment_failed":
                    return await HandlePaymentIntentFailedAsync(root, eventType);
                case "charge.refunded":
                    return await HandleChargeRefundedAsync(root, eventType);
                default:
                    return new WebhookProcessResult(true, $"Ignored unsupported event type: {eventType}", eventType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Stripe webhook event.");
            return new WebhookProcessResult(false, "Webhook processing failed.");
        }
    }

    private async Task<PaymentResult> CreateStripePaymentIntentAsync(Guid learnerId, decimal amount, string paymentMethodId, string email)
    {
        if (string.IsNullOrWhiteSpace(_stripeSecretKey))
        {
            return new PaymentResult(false, "Stripe API key is not configured.", null, "stripe_key_missing");
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _stripeSecretKey);

        var cents = (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        var values = new Dictionary<string, string>
        {
            ["amount"] = cents.ToString(),
            ["currency"] = "usd",
            ["confirm"] = "true",
            ["payment_method"] = paymentMethodId,
            ["metadata[learner_id]"] = learnerId.ToString(),
            ["metadata[learner_email]"] = email
        };

        using var requestBody = new FormUrlEncodedContent(values);
        var response = await client.PostAsync("https://api.stripe.com/v1/payment_intents", requestBody);
        var responseText = await response.Content.ReadAsStringAsync();

        using var resultDoc = JsonDocument.Parse(responseText);
        var root = resultDoc.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            var message = TryReadNestedString(root, "error", "message") ?? "Stripe payment intent creation failed.";
            var code = TryReadNestedString(root, "error", "code") ?? "stripe_api_error";
            return new PaymentResult(false, message, null, code);
        }

        var stripeIntentId = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;

        if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "requires_capture", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "processing", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentResult(true, "Payment successful!", stripeIntentId);
        }

        var declineMessage = TryReadNestedString(root, "last_payment_error", "message")
            ?? "Payment failed at processor.";
        var declineCode = TryReadNestedString(root, "last_payment_error", "code") ?? "payment_failed";
        return new PaymentResult(false, declineMessage, stripeIntentId, declineCode);
    }

    private async Task<PaymentResult> CreateStripeRefundAsync(string stripePaymentIntentId)
    {
        if (string.IsNullOrWhiteSpace(_stripeSecretKey))
        {
            return new PaymentResult(false, "Stripe API key is not configured.", null, "stripe_key_missing");
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _stripeSecretKey);

        var refundContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["payment_intent"] = stripePaymentIntentId
        });

        var response = await client.PostAsync("https://api.stripe.com/v1/refunds", refundContent);
        var responseText = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return new PaymentResult(true, "Refund processed successfully.", stripePaymentIntentId);
        }

        using var errorDoc = JsonDocument.Parse(responseText);
        var message = TryReadNestedString(errorDoc.RootElement, "error", "message") ?? "Stripe refund failed.";
        var code = TryReadNestedString(errorDoc.RootElement, "error", "code") ?? "stripe_refund_error";
        return new PaymentResult(false, message, null, code);
    }

    private async Task<WebhookProcessResult> HandlePaymentIntentSucceededAsync(JsonElement root, string eventType)
    {
        var paymentIntent = GetStripeDataObject(root);
        if (paymentIntent is null)
        {
            return new WebhookProcessResult(false, "Missing data.object in webhook payload.", eventType);
        }

        var stripePaymentIntentId = paymentIntent.Value.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(stripePaymentIntentId))
        {
            return new WebhookProcessResult(false, "Missing payment intent id.", eventType);
        }

        var existing = await _dbContext.PaymentTransactions
            .FirstOrDefaultAsync(transaction => transaction.StripePaymentIntentId == stripePaymentIntentId);

        if (existing is null)
        {
            var metadataLearnerId = TryReadNestedString(paymentIntent.Value, "metadata", "learner_id");
            var metadataEmail = TryReadNestedString(paymentIntent.Value, "metadata", "learner_email");
            if (!Guid.TryParse(metadataLearnerId, out var learnerId))
            {
                _logger.LogWarning("Stripe webhook payment_intent.succeeded ignored because metadata learner_id was missing: {StripePaymentIntentId}", stripePaymentIntentId);
                return new WebhookProcessResult(true, "Ignored webhook with missing learner metadata.", eventType, stripePaymentIntentId);
            }

            var amountCents = paymentIntent.Value.TryGetProperty("amount", out var amountElement) ? amountElement.GetInt64() : 0;
            var amount = Math.Round(amountCents / 100m, 2, MidpointRounding.AwayFromZero);

            existing = new PaymentTransaction
            {
                LearnerId = learnerId,
                Amount = amount,
                Status = "Completed",
                StripePaymentIntentId = stripePaymentIntentId,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            _dbContext.PaymentTransactions.Add(existing);
            await _dbContext.SaveChangesAsync();
            await _auditLogService.WriteAsync(learnerId, metadataEmail, "payment.completed.webhook", "PaymentTransaction", existing.Id, $"Intent: {stripePaymentIntentId}");

            return new WebhookProcessResult(true, "Created completed transaction from webhook.", eventType, stripePaymentIntentId);
        }

        if (string.Equals(existing.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookProcessResult(true, "Already processed (idempotent).", eventType, stripePaymentIntentId);
        }

        existing.Status = "Completed";
        existing.FailureReason = null;
        existing.CompletedAt = existing.CompletedAt ?? DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new WebhookProcessResult(true, "Updated transaction to completed.", eventType, stripePaymentIntentId);
    }

    private async Task<WebhookProcessResult> HandlePaymentIntentFailedAsync(JsonElement root, string eventType)
    {
        var paymentIntent = GetStripeDataObject(root);
        if (paymentIntent is null)
        {
            return new WebhookProcessResult(false, "Missing data.object in webhook payload.", eventType);
        }

        var stripePaymentIntentId = paymentIntent.Value.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(stripePaymentIntentId))
        {
            return new WebhookProcessResult(false, "Missing payment intent id.", eventType);
        }

        var failureReason = TryReadNestedString(paymentIntent.Value, "last_payment_error", "message") ?? "Payment failed at processor.";
        var existing = await _dbContext.PaymentTransactions
            .FirstOrDefaultAsync(transaction => transaction.StripePaymentIntentId == stripePaymentIntentId);

        if (existing is null)
        {
            var metadataLearnerId = TryReadNestedString(paymentIntent.Value, "metadata", "learner_id");
            var metadataEmail = TryReadNestedString(paymentIntent.Value, "metadata", "learner_email");
            if (!Guid.TryParse(metadataLearnerId, out var learnerId))
            {
                return new WebhookProcessResult(true, "Ignored failed payment without learner metadata.", eventType, stripePaymentIntentId);
            }

            var amountCents = paymentIntent.Value.TryGetProperty("amount", out var amountElement) ? amountElement.GetInt64() : 0;
            var amount = Math.Round(amountCents / 100m, 2, MidpointRounding.AwayFromZero);

            existing = new PaymentTransaction
            {
                LearnerId = learnerId,
                Amount = amount,
                Status = "Failed",
                StripePaymentIntentId = stripePaymentIntentId,
                FailureReason = failureReason,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.PaymentTransactions.Add(existing);
            await _dbContext.SaveChangesAsync();
            await _auditLogService.WriteAsync(learnerId, metadataEmail, "payment.failed.webhook", "PaymentTransaction", existing.Id, $"Intent: {stripePaymentIntentId}");

            return new WebhookProcessResult(true, "Created failed transaction from webhook.", eventType, stripePaymentIntentId);
        }

        if (string.Equals(existing.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookProcessResult(true, "Already processed (idempotent).", eventType, stripePaymentIntentId);
        }

        existing.Status = "Failed";
        existing.FailureReason = failureReason;
        await _dbContext.SaveChangesAsync();

        return new WebhookProcessResult(true, "Updated transaction to failed.", eventType, stripePaymentIntentId);
    }

    private async Task<WebhookProcessResult> HandleChargeRefundedAsync(JsonElement root, string eventType)
    {
        var chargeObject = GetStripeDataObject(root);
        if (chargeObject is null)
        {
            return new WebhookProcessResult(false, "Missing data.object in webhook payload.", eventType);
        }

        var stripePaymentIntentId = chargeObject.Value.TryGetProperty("payment_intent", out var intentElement)
            ? intentElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(stripePaymentIntentId))
        {
            return new WebhookProcessResult(true, "Ignored refund webhook without payment_intent.", eventType);
        }

        var existing = await _dbContext.PaymentTransactions
            .FirstOrDefaultAsync(transaction => transaction.StripePaymentIntentId == stripePaymentIntentId);

        if (existing is null)
        {
            return new WebhookProcessResult(true, "No local transaction found for refund webhook.", eventType, stripePaymentIntentId);
        }

        if (string.Equals(existing.Status, "Refunded", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookProcessResult(true, "Already processed (idempotent).", eventType, stripePaymentIntentId);
        }

        existing.Status = "Refunded";
        existing.RefundedAt = existing.RefundedAt ?? DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new WebhookProcessResult(true, "Updated transaction to refunded.", eventType, stripePaymentIntentId);
    }

    private static JsonElement? GetStripeDataObject(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var dataElement))
        {
            return null;
        }

        if (!dataElement.TryGetProperty("object", out var objectElement))
        {
            return null;
        }

        return objectElement;
    }

    private static string? TryReadNestedString(JsonElement element, string parentPropertyName, string childPropertyName)
    {
        if (!element.TryGetProperty(parentPropertyName, out var parentElement))
        {
            return null;
        }

        if (!parentElement.TryGetProperty(childPropertyName, out var childElement))
        {
            return null;
        }

        return childElement.GetString();
    }

    private static bool VerifyStripeWebhookSignature(string payload, string? signatureHeader, string webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var parts = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var timestampPart = parts.FirstOrDefault(part => part.StartsWith("t=", StringComparison.OrdinalIgnoreCase));
        var signaturePart = parts.FirstOrDefault(part => part.StartsWith("v1=", StringComparison.OrdinalIgnoreCase));

        if (timestampPart is null || signaturePart is null)
        {
            return false;
        }

        var timestamp = timestampPart[2..];
        var expectedSignature = signaturePart[3..];
        var signedPayload = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var computedSignature = Convert.ToHexString(computedBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant()));
    }
}
