using System.Net;
using System.Text.Json;
using Lms.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class MailSlurpEmailTransportTests
{
    private static MailSlurpOptions Options() => new()
    {
        ApiKey = "synthetic-test-key",
        SenderInboxId = Guid.NewGuid(),
        AllowedRecipients = ["test@example.invalid"]
    };

    [Fact]
    public async Task SendsHtmlUsingConfiguredInboxAndKey()
    {
        var options = Options();
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var service = new EmailService(NullLogger<EmailService>.Instance, new MailSlurpEmailTransport(client, options));
        Assert.True(await service.SendAsync(new("test@example.invalid", "Connection test", "<p>Synthetic</p>")));
        var call = Assert.Single(handler.Calls);
        Assert.Equal($"https://api.mailslurp.com/inboxes/{options.SenderInboxId}", call.Uri);
        Assert.Equal(options.ApiKey, call.Key);
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal("test@example.invalid", body.RootElement.GetProperty("to")[0].GetString());
        Assert.True(body.RootElement.GetProperty("isHTML").GetBoolean());
        Assert.Equal("<p>Synthetic</p>", body.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task RejectsRecipientsBeforeAnyNetworkRequest_AndSurfacesFailure()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var transport = new MailSlurpEmailTransport(client, Options());
        var message = new EmailMessage("actual-person@example.org", "Blocked", "No send");
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(message));
        var service = new EmailService(NullLogger<EmailService>.Instance, transport);
        Assert.False(await service.SendAsync(message));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task ProviderFailureIsNotReportedAsSuccess()
    {
        var handler = new Handler { Status = HttpStatusCode.Unauthorized };
        using var client = new HttpClient(handler);
        var service = new EmailService(NullLogger<EmailService>.Instance, new MailSlurpEmailTransport(client, Options()));
        Assert.False(await service.SendAsync(new("test@example.invalid", "Test", "Synthetic")));
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task UploadsAttachmentBeforeSending()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var transport = new MailSlurpEmailTransport(client, Options());
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "synthetic attachment");
            await transport.SendAsync(new("test@example.invalid", "Attachment test", "Synthetic"), [path]);
            Assert.Equal(2, handler.Calls.Count);
            using var upload = JsonDocument.Parse(handler.Calls[0].Body);
            Assert.Equal(Convert.ToBase64String(await File.ReadAllBytesAsync(path)),
                upload.RootElement.GetProperty("base64Contents").GetString());
            using var send = JsonDocument.Parse(handler.Calls[1].Body);
            Assert.Equal("attachment-id", send.RootElement.GetProperty("attachments")[0].GetString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RequiresKeySenderAndAllowlist()
    {
        Assert.Throws<InvalidOperationException>(() => new MailSlurpOptions().Validate());
        var options = Options();
        options.ApiKey = "key\u0016";
        Assert.Throws<InvalidOperationException>(options.Validate);
        options = Options();
        options.AllowedRecipients = [];
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public List<(string Uri, string Key, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.RequestUri!.AbsoluteUri, request.Headers.GetValues("x-api-key").Single(),
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(request.RequestUri.AbsolutePath == "/attachments" ? "[\"attachment-id\"]" : "")
            };
        }
    }
}
