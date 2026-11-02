using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lms.Application.Services;

public sealed class InstructorMailboxOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string[] AllowedRecipients { get; set; } = [];
    public List<InstructorMailboxBinding> Accounts { get; set; } = [];

    public void Validate()
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Any(char.IsWhiteSpace) || ApiKey.Any(char.IsControl)
            || Accounts.Count == 0 || Accounts.Any(a => a.UserId == Guid.Empty || a.InboxId == Guid.Empty)
            || Accounts.Select(a => a.UserId).Distinct().Count() != Accounts.Count
            || Accounts.Select(a => a.InboxId).Distinct().Count() != Accounts.Count
            || AllowedRecipients.Length == 0
            || AllowedRecipients.Any(a => a.Any(char.IsControl)
                || !System.Net.Mail.MailAddress.TryCreate(a, out var address) || address.Address != a))
            throw new InvalidOperationException("Instructor mailbox requires a key, unique user/inbox bindings, and an explicit test-recipient allowlist.");
    }
}

public sealed class InstructorMailboxBinding
{
    public Guid UserId { get; set; }
    public Guid InboxId { get; set; }
}

public sealed class MailboxProviderRestrictionException(string message) : InvalidOperationException(message);

public sealed class MailSlurpMailboxProvider(HttpClient client, InstructorMailboxOptions options) : IMailboxProvider
{
    private Guid Inbox(Guid owner)
    {
        if (!options.Enabled)
            throw new InvalidOperationException("Instructor mail is not connected. An administrator must configure a dedicated MailSlurp test inbox for your account.");
        return options.Accounts.SingleOrDefault(a => a.UserId == owner)?.InboxId
            ?? throw new InvalidOperationException("No instructor mailbox is assigned to your account. Contact your administrator.");
    }

    public async Task<MailboxAccount> GetAccountAsync(Guid owner)
    {
        var inbox = Inbox(owner);
        var data = await RequestAsync(HttpMethod.Get, $"inboxes/{inbox}");
        return new($"mailslurp:{inbox}", Text(data, "emailAddress"), "MailSlurp", true);
    }

    public async Task<MailboxPage> ListAsync(Guid owner, bool sent, int page)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        var inbox = Inbox(owner);
        var path = sent ? $"sent?inboxId={inbox}&page={page}&size=20&sort=DESC"
            : $"inboxes/{inbox}/emails/paginated?page={page}&size=20&sort=DESC";
        var data = await RequestAsync(HttpMethod.Get, path);
        var messages = data.GetProperty("content").EnumerateArray().Select(e => Map(e, sent)).ToArray();
        return new(messages, !data.GetProperty("last").GetBoolean());
    }

    public async Task<MailboxMessage> ReadAsync(Guid owner, string id, bool sent)
    {
        var data = await GetOwnedAsync(owner, id, sent);
        return Map(data, sent);
    }

    public async Task<IReadOnlyList<MailboxMessage>> ConversationAsync(Guid owner, MailboxMessage message)
    {
        await GetOwnedAsync(owner, message.Id, message.Sent);
        if (string.IsNullOrWhiteSpace(message.ThreadId)) return [message];
        var data = await RequestAsync(HttpMethod.Get,
            $"emails/threads/{Uri.EscapeDataString(message.ThreadId)}/items");
        var result = new List<MailboxMessage>();
        foreach (var item in data.GetProperty("items").EnumerateArray())
        {
            var sent = Text(item, "itemType").Contains("SENT", StringComparison.OrdinalIgnoreCase);
            var id = Text(item, "entityId");
            var detail = await RequestAsync(HttpMethod.Get, $"{(sent ? "sent" : "emails")}/{Segment(id)}");
            // A provider thread may span several inboxes in the same API account.
            if (Text(detail, "inboxId") == Inbox(owner).ToString()) result.Add(Map(detail, sent));
        }
        if (result.All(m => m.Id != message.Id)) result.Add(message);
        return result.OrderBy(m => m.CreatedAt).ToArray();
    }

    public async Task SendAsync(Guid owner, MailboxCompose message)
    {
        InstructorMailboxService.Validate(message);
        var inbox = Inbox(owner);
        foreach (var recipient in new[] { message.To, message.Cc }.Where(r => r.Length > 0))
            if (!options.AllowedRecipients.Contains(recipient, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test mode blocked delivery: every recipient must be on the instructor mailbox allowlist.");
        if (message.ReplyToId is not null)
        {
            var original = await ReadAsync(owner, message.ReplyToId, false);
            if (!string.Equals(original.From, message.To, StringComparison.OrdinalIgnoreCase) || message.Cc.Length > 0)
                throw new InvalidOperationException("Replies must go only to the original sender. Use Compose to change recipients.");
            var originalData = await GetOwnedAsync(owner, message.ReplyToId, false);
            var replyTo = Text(originalData, "replyTo");
            if (replyTo.Length > 0 && !options.AllowedRecipients.Contains(replyTo, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test mode blocked the message's Reply-To address.");
        }
        var attachments = new List<string>();
        foreach (var file in message.Attachments)
        {
            var uploaded = await RequestAsync(HttpMethod.Post, "attachments", new
            {
                base64Contents = Convert.ToBase64String(file.Content),
                contentType = file.ContentType,
                filename = file.FileName
            });
            attachments.AddRange(uploaded.EnumerateArray().Select(e => e.GetString()
                ?? throw new InvalidOperationException("Attachment upload returned no identifier.")));
        }
        if (message.ReplyToId is not null)
            await RequestAsync(HttpMethod.Put, $"emails/{Segment(message.ReplyToId)}",
                new { body = message.Body, isHTML = false, attachments });
        else
            await RequestAsync(HttpMethod.Post, $"inboxes/{inbox}/confirm", new
            {
                to = new[] { message.To }, cc = message.Cc.Length > 0 ? new[] { message.Cc } : [],
                subject = message.Subject, body = message.Body, isHTML = false, attachments
            });
    }

    public async Task<MailboxFile> DownloadAsync(Guid owner, string messageId, bool sent, string attachmentId)
    {
        var message = await ReadAsync(owner, messageId, sent);
        if (!message.Attachments.Contains(attachmentId, StringComparer.Ordinal))
            throw new InvalidOperationException("Attachment does not belong to this message.");
        var path = $"attachments/{Uri.EscapeDataString(attachmentId)}";
        var metadata = await RequestAsync(HttpMethod.Get, $"{path}/metadata");
        if (metadata.GetProperty("contentLength").GetInt64() > 5 * 1024 * 1024)
            throw new InvalidOperationException("This attachment exceeds the 5 MB download limit.");
        var data = await RequestAsync(HttpMethod.Get, $"{path}/base64");
        var bytes = Convert.FromBase64String(Text(data, "base64FileContents"));
        if (bytes.Length > 5 * 1024 * 1024) throw new InvalidOperationException("Attachment exceeds the 5 MB limit.");
        return new(attachmentId, Path.GetFileName(Text(metadata, "name")), "application/octet-stream", bytes);
    }

    private async Task<JsonElement> GetOwnedAsync(Guid owner, string id, bool sent)
    {
        var inbox = Inbox(owner);
        var data = await RequestAsync(HttpMethod.Get, $"{(sent ? "sent" : "emails")}/{Segment(id)}");
        if (Text(data, "inboxId") != inbox.ToString())
            throw new InvalidOperationException("Message is not available in your mailbox.");
        return data;
    }

    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri("https://api.mailslurp.com/"), path));
        request.Headers.Add("x-api-key", options.ApiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (Text(error.RootElement, "errorCode") == "W_400_SEND_EVALUATION_OPTIONS_UNSUPPORTED")
                throw new MailboxProviderRestrictionException(
                    "MailSlurp's current evaluation plan does not support this send operation. Threaded replies require a provider plan that supports them. No message was sent.");
        }
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"MailSlurp mailbox request failed (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.Clone();
    }

    private static MailboxMessage Map(JsonElement data, bool sent)
    {
        var body = Text(data, "body");
        if (data.TryGetProperty("isHTML", out var html) && html.ValueKind == JsonValueKind.True)
        {
            body = Regex.Replace(body, "<(script|style)\\b[^>]*>.*?</\\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
            body = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(1)));
        }
        return new(Text(data, "id"), Text(data, "subject"), Text(data, "from"), Strings(data, "to"),
            Strings(data, "cc"), body, data.GetProperty("createdAt").GetDateTimeOffset(),
            sent || (data.TryGetProperty("read", out var read) && read.ValueKind == JsonValueKind.True),
            sent, Text(data, "threadId"), Strings(data, "attachments"));
    }

    private static string Segment(string id) => Guid.TryParse(id, out var guid)
        ? guid.ToString() : throw new InvalidOperationException("Invalid mailbox message identifier.");
    private static string Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static string[] Strings(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray() : [];
}
