using System.Net.Http.Json;
using System.Net.Mail;

namespace Lms.Application.Services;

public sealed class MailSlurpOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public Guid SenderInboxId { get; set; }
    public string[] AllowedRecipients { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Any(char.IsWhiteSpace) || ApiKey.Any(char.IsControl)
            || SenderInboxId == Guid.Empty || AllowedRecipients.Length == 0
            || AllowedRecipients.Any(address => !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address))
        {
            throw new InvalidOperationException("MailSlurp requires a valid API key, sender inbox ID, and explicit recipient email allowlist.");
        }
    }
}

public sealed class MailSlurpEmailTransport(HttpClient client, MailSlurpOptions options)
{
    public async Task SendAsync(EmailMessage message, IReadOnlyCollection<string>? attachmentFilePaths = null)
    {
        options.Validate();
        if (!options.AllowedRecipients.Contains(message.RecipientEmail, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MailSlurp blocked delivery: recipient is not on the development allowlist.");
        }

        var attachments = new List<string>();
        var files = new List<EmailAttachment>(message.Attachments ?? []);
        foreach (var path in attachmentFilePaths ?? [])
        {
            var content = await File.ReadAllBytesAsync(path);
            files.Add(new EmailAttachment(Path.GetFileName(path),
                Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? "application/pdf" : "application/octet-stream", content));
        }
        foreach (var file in files)
        {
            using var upload = await PostAsync("attachments", new
            {
                base64Contents = Convert.ToBase64String(file.Content),
                contentType = file.ContentType,
                filename = file.FileName
            });
            var ids = await upload.Content.ReadFromJsonAsync<string[]>()
                ?? throw new InvalidOperationException("MailSlurp returned no attachment identifiers.");
            if (ids.Length == 0)
                throw new InvalidOperationException("MailSlurp returned an empty attachment upload result.");
            attachments.AddRange(ids);
        }

        using var response = await PostAsync($"inboxes/{options.SenderInboxId}", new
        {
            to = new[] { message.RecipientEmail },
            subject = message.Subject,
            body = message.HtmlBody,
            isHTML = true,
            attachments
        });
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri("https://api.mailslurp.com/"), path));
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            // Provider response bodies can contain message contents or credentials.
            throw new HttpRequestException($"MailSlurp request failed with HTTP {(int)status}.", null, status);
        }
        return response;
    }
}
