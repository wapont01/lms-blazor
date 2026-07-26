using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace Lms.Application.Services;

public record EmailMessage(string RecipientEmail, string Subject, string HtmlBody);

public interface IEmailService
{
    Task<bool> SendAsync(EmailMessage message);
    Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames);
}

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string? _smtpUsername;
    private readonly string? _smtpPassword;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly bool _enableSsl;
    private readonly int _smtpTimeoutMs;
    private readonly string _deliveryMode;
    private readonly string _invoicePath;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
        
        // Configuration from environment or appsettings
        _smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "localhost";
        _smtpPort = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var port) ? port : 25;
        _smtpUsername = Environment.GetEnvironmentVariable("SMTP_USERNAME");
        _smtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        _fromEmail = Environment.GetEnvironmentVariable("SMTP_FROM_EMAIL") ?? "noreply@lms.local";
        _fromName = Environment.GetEnvironmentVariable("SMTP_FROM_NAME") ?? "LMS System";
        _enableSsl = bool.TryParse(Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL"), out var enableSsl) && enableSsl;
        _smtpTimeoutMs = int.TryParse(Environment.GetEnvironmentVariable("SMTP_TIMEOUT_MS"), out var timeoutMs) ? timeoutMs : 15000;

        // Use safe default in dev/test environments unless explicitly set to "smtp".
        _deliveryMode = (Environment.GetEnvironmentVariable("EMAIL_DELIVERY_MODE") ?? "log").Trim().ToLowerInvariant();

        var baseDirectory = AppContext.BaseDirectory;
        _invoicePath = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "Lms.Web", "App_Data", "Invoices"));
    }

    public async Task<bool> SendAsync(EmailMessage message)
    {
        return await SendInternalAsync(message, attachmentFilePaths: null);
    }

    private async Task<bool> SendInternalAsync(EmailMessage message, IReadOnlyCollection<string>? attachmentFilePaths)
    {
        try
        {
            if (_deliveryMode != "smtp")
            {
                _logger.LogInformation("Email delivery mode is '{Mode}'. Simulating send to {Recipient}: {Subject}", _deliveryMode, message.RecipientEmail, message.Subject);
                return true;
            }

            using var client = new SmtpClient(_smtpHost, _smtpPort);
            client.EnableSsl = _enableSsl;
            client.Timeout = _smtpTimeoutMs;

            if (!string.IsNullOrWhiteSpace(_smtpUsername) && !string.IsNullOrWhiteSpace(_smtpPassword))
            {
                client.Credentials = new NetworkCredential(_smtpUsername, _smtpPassword);
            }

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_fromEmail, _fromName),
                Subject = message.Subject,
                Body = message.HtmlBody,
                IsBodyHtml = true
            };

            mailMessage.To.Add(message.RecipientEmail);

            if (attachmentFilePaths is not null)
            {
                foreach (var path in attachmentFilePaths.Where(File.Exists))
                {
                    mailMessage.Attachments.Add(new Attachment(path));
                }
            }

            await client.SendMailAsync(mailMessage);

            _logger.LogInformation($"Email sent to {message.RecipientEmail}: {message.Subject}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send email to {message.RecipientEmail}: {message.Subject}");
            return false;
        }
    }

    public async Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames)
    {
        try
        {
            var coursesList = string.Join("</li>\n<li>", courseNames);
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #4a90e2; color: white; padding: 20px; text-align: center; border-radius: 4px 4px 0 0; }}
        .content {{ background: #f9f9f9; padding: 20px; border: 1px solid #ddd; }}
        .receipt-section {{ margin: 20px 0; }}
        .receipt-label {{ font-weight: bold; color: #666; }}
        .receipt-value {{ font-size: 1.1em; color: #333; margin-bottom: 10px; }}
        .courses-list {{ list-style: none; padding: 0; }}
        .courses-list li {{ padding: 8px; background: white; margin: 5px 0; border-left: 3px solid #4a90e2; padding-left: 12px; }}
        .total-amount {{ font-size: 1.5em; font-weight: bold; color: #4a90e2; margin: 20px 0; }}
        .footer {{ text-align: center; margin-top: 20px; color: #999; font-size: 0.9em; }}
        .button {{ display: inline-block; background: #4a90e2; color: white; padding: 10px 20px; text-decoration: none; border-radius: 4px; margin: 20px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✓ Payment Successful</h1>
            <p>Your receipt and course access information</p>
        </div>
        
        <div class='content'>
            <div class='receipt-section'>
                <p class='receipt-label'>Invoice Number:</p>
                <p class='receipt-value'>{invoiceNumber}</p>
            </div>

            <div class='receipt-section'>
                <p class='receipt-label'>Transaction Date:</p>
                <p class='receipt-value'>{DateTime.UtcNow:MMMM dd, yyyy 'at' h:mm tt 'UTC'}</p>
            </div>

            <div class='receipt-section'>
                <p class='receipt-label'>Courses Purchased:</p>
                <ul class='courses-list'>
                    <li>{coursesList}</li>
                </ul>
            </div>

            <div class='receipt-section'>
                <p class='receipt-label'>Amount Paid:</p>
                <div class='total-amount'>${amount:F2}</div>
            </div>

            <div class='receipt-section'>
                <p>Your courses are now available in your dashboard. You can start learning immediately:</p>
                <a href='https://lms.local/my-courses' class='button'>View My Courses</a>
            </div>

            <div class='receipt-section'>
                <h3>What's Next?</h3>
                <ul>
                    <li>Access your purchased courses from your dashboard</li>
                    <li>Track your progress and complete assessments</li>
                    <li>Download certificates upon completion</li>
                    <li>Contact support for any questions</li>
                </ul>
            </div>
        </div>

        <div class='footer'>
            <p>This is an automated message. Please do not reply to this email.</p>
            <p>&copy; {DateTime.UtcNow.Year} Learning Management System. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

            var message = new EmailMessage(
                recipientEmail,
                $"Receipt for Purchase - {invoiceNumber}",
                htmlBody
            );

            var invoiceFile = Path.Combine(_invoicePath, $"{invoiceNumber}.pdf");
            if (File.Exists(invoiceFile))
            {
                return await SendInternalAsync(message, new[] { invoiceFile });
            }

            _logger.LogWarning("Invoice PDF not found for attachment: {InvoiceFile}", invoiceFile);
            return await SendInternalAsync(message, attachmentFilePaths: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to generate receipt email for {recipientEmail}");
            return false;
        }
    }
}
