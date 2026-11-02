using System.Net;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class InstructorMailboxTests
{
    private readonly Guid _owner = Guid.NewGuid(), _other = Guid.NewGuid(), _inbox = Guid.NewGuid();

    [Fact]
    public async Task DisabledOrUnassignedMailboxMakesNoNetworkRequests()
    {
        var handler = new Handler(_ => throw new Exception("Unexpected network request"));
        var disabled = new MailSlurpMailboxProvider(new HttpClient(handler), new());
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.GetAccountAsync(_owner));
        var provider = Provider(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAccountAsync(_other));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void RejectsSharedInboxBindings()
    {
        var options = Options();
        options.Accounts.Add(new() { UserId = _other, InboxId = _inbox });
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsAnotherInstructorsMessage(bool sent)
    {
        var handler = new Handler(_ => Message(Guid.NewGuid(), Guid.NewGuid(), sent));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Provider(handler).ReadAsync(_owner, Guid.NewGuid().ToString(), sent));
    }

    [Fact]
    public async Task RejectsUnapprovedRecipientsBeforeUploadingOrSending()
    {
        var handler = new Handler(_ => "{}");
        var compose = new MailboxCompose("person@example.org", "", "Subject", "Body",
            [new("file.txt", "text/plain", [1])]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(handler).SendAsync(_owner, compose));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task CcCannotBypassAllowlist()
    {
        var handler = new Handler(_ => "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(handler).SendAsync(_owner,
            new("commission@example.invalid", "person@example.org", "Subject", "Body", [])));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task SendsPlainTextWithAttachmentsFromAssignedInbox()
    {
        var handler = new Handler(path => path == "/attachments" ? "[\"attachment-id\"]" : "{}");
        await Provider(handler).SendAsync(_owner,
            new("commission@example.invalid", "", "Subject", "Body", [new("test.txt", "text/plain", [1, 2])]));
        Assert.Equal(2, handler.Calls.Count);
        var sent = handler.Calls[1];
        Assert.Equal($"/inboxes/{_inbox}/confirm", sent.Path);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.False(body.RootElement.GetProperty("isHTML").GetBoolean());
        Assert.Equal("attachment-id", body.RootElement.GetProperty("attachments")[0].GetString());
    }

    [Fact]
    public async Task RepliesUseProviderThreadingAndBlockUnapprovedReplyTo()
    {
        var id = Guid.NewGuid();
        var handler = new Handler(path => path.StartsWith("/emails/")
            ? Message(id, _inbox, false) : "{}");
        await Provider(handler).SendAsync(_owner,
            new("commission@example.invalid", "", "Re: Subject", "Reply", [], id.ToString()));
        Assert.Equal(HttpMethod.Put, handler.Calls.Last().Method);
        Assert.Equal($"/emails/{id}", handler.Calls.Last().Path);
        handler = new Handler(_ => Message(id, _inbox, false, "outsider@example.org"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(handler).SendAsync(_owner,
            new("commission@example.invalid", "", "Re: Subject", "Reply", [], id.ToString())));
        Assert.All(handler.Calls, c => Assert.Equal(HttpMethod.Get, c.Method));
    }

    [Fact]
    public async Task ListsInboxAndSentUsingAssignedInboxAndPagination()
    {
        var handler = new Handler(_ => """{"content":[],"last":false}""");
        var provider = Provider(handler);
        Assert.True((await provider.ListAsync(_owner, false, 2)).HasNext);
        Assert.True((await provider.ListAsync(_owner, true, 1)).HasNext);
        Assert.Contains($"/inboxes/{_inbox}/emails/paginated", handler.Calls[0].Path);
        Assert.Contains("page=2", handler.Calls[0].Query);
        Assert.Contains($"inboxId={_inbox}", handler.Calls[1].Query);
    }

    [Fact]
    public async Task HtmlIsConvertedToTextAndAttachmentsRequireMessageOwnership()
    {
        var id = Guid.NewGuid();
        var handler = new Handler(_ => Message(id, _inbox, false));
        var provider = Provider(handler);
        var message = await provider.ReadAsync(_owner, id.ToString(), false);
        Assert.DoesNotContain("<", message.Body);
        Assert.DoesNotContain("bad()", message.Body);
        Assert.Contains("Hello & welcome", message.Body);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.DownloadAsync(_owner, id.ToString(), false, "not-owned"));
        Assert.All(handler.Calls, c => Assert.StartsWith("/emails/", c.Path));
    }

    [Fact]
    public async Task ConversationExcludesOtherMailboxes()
    {
        var id = Guid.NewGuid(); var foreignId = Guid.NewGuid(); var thread = Guid.NewGuid();
        var handler = new Handler(path => path.EndsWith("/items")
            ? JsonSerializer.Serialize(new { items = new[] {
                new { itemType = "RECEIVED_EMAIL", entityId = id },
                new { itemType = "SENT_EMAIL", entityId = foreignId } } })
            : Message(path.Contains(foreignId.ToString()) ? foreignId : id,
                path.Contains(foreignId.ToString()) ? Guid.NewGuid() : _inbox, false, thread: thread));
        var provider = Provider(handler);
        var message = await provider.ReadAsync(_owner, id.ToString(), false);
        var conversation = await provider.ConversationAsync(_owner, message);
        Assert.Equal(id.ToString(), Assert.Single(conversation).Id);
    }

    [Fact]
    public async Task ProviderFailureNeverLooksLikeSuccessfulDelivery()
    {
        var handler = new Handler(_ => "{}") { Status = HttpStatusCode.TooManyRequests };
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(handler).SendAsync(_owner,
            new("commission@example.invalid", "", "Subject", "Body", [])));
    }

    [Fact]
    public async Task EvaluationRestrictionIsExplicitAndNeverFallsBackToAnotherSend()
    {
        var handler = new Handler(_ => """{"errorCode":"W_400_SEND_EVALUATION_OPTIONS_UNSUPPORTED","message":"Private provider detail"}""")
        { Status = HttpStatusCode.BadRequest };
        var error = await Assert.ThrowsAsync<MailboxProviderRestrictionException>(() => Provider(handler).SendAsync(_owner,
            new("commission@example.invalid", "", "Subject", "Body", [])));
        Assert.Contains("evaluation plan", error.Message);
        Assert.DoesNotContain("Private provider detail", error.Message);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public void ValidatesAddressSubjectAndAttachmentLimits()
    {
        Assert.Throws<InvalidOperationException>(() => InstructorMailboxService.Validate(
            new("bad", "", "Subject", "Body", [])));
        Assert.Throws<InvalidOperationException>(() => InstructorMailboxService.Validate(
            new("commission@example.invalid", "", "Header\r\nInjection", "Body", [])));
        Assert.Throws<InvalidOperationException>(() => InstructorMailboxService.Validate(
            new("commission@example.invalid", "", "Subject", "Body", [new("big", "text/plain", new byte[5 * 1024 * 1024 + 1])])));
        InstructorMailboxService.Validate(new("commission@example.invalid", "", "Subject", "Body",
            [new("limit", "text/plain", new byte[5 * 1024 * 1024])]));
    }

    [Fact]
    public async Task DraftsPersistAndCannotBeReadOrChangedByAnotherInstructor()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.UserAccounts.AddRange(new UserAccount { Id = _owner, Email = "one@example.invalid", DisplayName = "One" },
            new UserAccount { Id = _other, Email = "two@example.invalid", DisplayName = "Two" });
        await db.SaveChangesAsync();
        var fake = new FakeProvider();
        var service = new InstructorMailboxService(new Identity(_owner), fake, db);
        var compose = new MailboxCompose("commission@example.invalid", "", "Draft subject", "Draft body", []);
        var id = await service.SaveDraftAsync(compose, null);
        db.ChangeTracker.Clear();
        Assert.Equal("Draft body", Assert.Single(await service.GetStatesAsync()).Body);
        var other = new InstructorMailboxService(new Identity(_other), fake, db);
        Assert.Empty(await other.GetStatesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.SaveDraftAsync(compose, id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.SendAsync(compose, id));
        Assert.Equal(0, fake.Sends);
        fake.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SendAsync(compose, id));
        Assert.Single(await service.GetStatesAsync());
        fake.Fail = false;
        await service.SendAsync(compose, id);
        Assert.Empty(await service.GetStatesAsync());
        Assert.Equal(1, fake.Sends);
    }

    private MailSlurpMailboxProvider Provider(Handler handler) => new(new HttpClient(handler), Options());
    private InstructorMailboxOptions Options() => new()
    {
        Enabled = true, ApiKey = "synthetic-key", AllowedRecipients = ["commission@example.invalid"],
        Accounts = [new() { UserId = _owner, InboxId = _inbox }]
    };
    private static string Message(Guid id, Guid inbox, bool sent, string replyTo = "", Guid? thread = null) =>
        JsonSerializer.Serialize(new { id, inboxId = inbox, subject = "Subject", from = "commission@example.invalid",
            to = new[] { "instructor@example.invalid" }, cc = Array.Empty<string>(), replyTo,
            body = "<script>bad()</script><p>Hello &amp; welcome</p>", isHTML = true,
            createdAt = DateTimeOffset.UtcNow, read = sent, threadId = thread, attachments = new[] { "file-id" } });

    private sealed class Handler(Func<string, string> response) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public List<(HttpMethod Method, string Path, string Query, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("synthetic-key", request.Headers.GetValues("x-api-key").Single());
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(token)));
            return new(Status) { Content = new StringContent(response(request.RequestUri.AbsolutePath), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Identity(Guid id) : IMailboxIdentity
    {
        public Task<Guid> GetInstructorIdAsync() => Task.FromResult(id);
    }
    private sealed class FakeProvider : IMailboxProvider
    {
        public int Sends { get; private set; }
        public bool Fail { get; set; }
        public Task<MailboxAccount> GetAccountAsync(Guid owner) => Task.FromResult(new MailboxAccount("test", "test@example.invalid", "Test", true));
        public Task SendAsync(Guid owner, MailboxCompose message)
        {
            if (Fail) throw new HttpRequestException("Synthetic failure");
            Sends++; return Task.CompletedTask;
        }
        public Task<MailboxPage> ListAsync(Guid owner, bool sent, int page) => throw new NotSupportedException();
        public Task<MailboxMessage> ReadAsync(Guid owner, string id, bool sent) => throw new NotSupportedException();
        public Task<IReadOnlyList<MailboxMessage>> ConversationAsync(Guid owner, MailboxMessage message) => throw new NotSupportedException();
        public Task<MailboxFile> DownloadAsync(Guid owner, string messageId, bool sent, string attachmentId) => throw new NotSupportedException();
    }
}
