using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class InstructorMailboxUiTests
{
    [UiFact]
    public async Task Mailbox_ComposeDraftReplyArchiveAndAttachments_WorkAcrossScreenSizes()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lms.Web"));
        var database = Path.Combine(Path.GetTempPath(), $"lms-mail-ui-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MailboxTestHost).Assembly.FullName,
            EnvironmentName = "Testing"
        });
        var previewUrl = Environment.GetEnvironmentVariable("MAILBOX_UI_PREVIEW_URL");
        if (previewUrl is not null && (!Uri.TryCreate(previewUrl, UriKind.Absolute, out var previewUri)
            || previewUri.Scheme != "http" || previewUri.Host != "127.0.0.1"))
            throw new InvalidOperationException("The synthetic mailbox preview must bind to loopback HTTP.");
        builder.WebHost.UseUrls(previewUrl ?? "http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database};Pooling=False"));
        var owner = Guid.NewGuid();
        builder.Services.AddSingleton<IMailboxIdentity>(new Identity(owner));
        builder.Services.AddSingleton<IMailboxProvider, TestProvider>();
        builder.Services.AddScoped<InstructorMailboxService>();
        await using var app = builder.Build();
        app.UseAntiforgery();
        app.MapGet("/app.css", () => Results.File(Path.Combine(root, "wwwroot", "app.css"), "text/css"));
        app.MapGet("/Lms.Web.styles.css", () => Results.File(
            Path.Combine(root, "obj", "Debug", "net8.0", "scopedcss", "bundle", "Lms.Web.styles.css"), "text/css"));
        app.MapRazorComponents<MailboxTestHost>().AddInteractiveServerRenderMode();
        try
        {
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.UserAccounts.Add(new UserAccount { Id = owner, Email = "instructor@example.invalid", DisplayName = "Synthetic Instructor" });
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 1000 } });
            await page.GotoAsync(url);
            await Assertions.Expect(page.GetByText("Test mode · MailSlurp")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "+ Compose" })).ToBeEnabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "+ Compose" }).ClickAsync();
            await page.GetByLabel("To", new() { Exact = true }).FillAsync("commission@example.invalid");
            await page.GetByLabel("Subject", new() { Exact = true }).FillAsync("Synthetic approval request");
            await page.GetByLabel("Message", new() { Exact = true }).FillAsync("Synthetic draft text");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save draft", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Draft saved.");
            await page.ReloadAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Drafts", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button).Filter(new() { HasText = "Synthetic approval request" }).ClickAsync();
            await Assertions.Expect(page.GetByLabel("Message", new() { Exact = true })).ToHaveValueAsync("Synthetic draft text");
            await page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Message accepted by the email provider.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Inbox", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button).Filter(new() { HasText = "Synthetic commission response" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Download attachment 1" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Save synthetic.txt" })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Reply", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByLabel("To", new() { Exact = true })).ToHaveAttributeAsync("readonly", "");
            await page.GetByLabel("Message", new() { Exact = true }).FillAsync("Synthetic reply");
            await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
            {
                Name = "synthetic.txt", MimeType = "text/plain", Buffer = "synthetic file"u8.ToArray()
            });
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Message accepted by the email provider.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Inbox", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button).Filter(new() { HasText = "Synthetic commission response" }).ClickAsync();
            await Assertions.Expect(page.Locator(".mail-message")).ToHaveCountAsync(2);
            await page.GetByRole(AriaRole.Button, new() { Name = "Archive", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Archived", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button).Filter(new() { HasText = "Synthetic commission response" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Move to Inbox", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Inbox", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button).Filter(new() { HasText = "Synthetic commission response" }).ClickAsync();
            await Assertions.Expect(page.Locator(".mail-message")).ToHaveCountAsync(2);
            foreach (var width in new[] { 1440, 768, 390 })
            {
                await page.SetViewportSizeAsync(width, 1000);
                Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"),
                    $"Mailbox overflowed at {width}px");
            }
            await page.SetViewportSizeAsync(1440, 1000);
            var positions = await page.EvaluateAsync<double[]>("""
                () => {
                const top = selector => {
                    const range = document.createRange();
                    range.selectNodeContents(document.querySelector(selector));
                    return range.getBoundingClientRect().top;
                };
                return [top('.sidebar h3'), top('.mail-header h1')];
                }
                """);
            Assert.True(Math.Abs(positions[0] - positions[1]) <= 4, $"Header text misaligned: {string.Join(", ", positions)}");
            var screenshot = Environment.GetEnvironmentVariable("MAILBOX_UI_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot))
                await page.ScreenshotAsync(new() { Path = screenshot, FullPage = true });
            if (previewUrl is not null)
                await app.WaitForShutdownAsync();
        }
        finally
        {
            await app.StopAsync();
            if (File.Exists(database)) File.Delete(database);
        }
    }

    private sealed class Identity(Guid owner) : IMailboxIdentity
    {
        public Task<Guid> GetInstructorIdAsync() => Task.FromResult(owner);
    }

    private sealed class TestProvider : IMailboxProvider
    {
        private readonly List<MailboxMessage> _messages = [
            new("inbound", "Synthetic commission response", "commission@example.invalid",
                ["instructor@example.invalid"], [], "Synthetic response body", DateTimeOffset.UtcNow, false, false, "thread", ["file"])
        ];
        public Task<MailboxAccount> GetAccountAsync(Guid owner) => Task.FromResult(
            new MailboxAccount("synthetic", "instructor@example.invalid", "MailSlurp", true));
        public Task<MailboxPage> ListAsync(Guid owner, bool sent, int page) => Task.FromResult(
            new MailboxPage(_messages.Where(m => m.Sent == sent).Reverse().ToArray(), false));
        public Task<MailboxMessage> ReadAsync(Guid owner, string id, bool sent) =>
            Task.FromResult(_messages.Single(m => m.Id == id && m.Sent == sent));
        public Task<IReadOnlyList<MailboxMessage>> ConversationAsync(Guid owner, MailboxMessage message) =>
            Task.FromResult<IReadOnlyList<MailboxMessage>>(_messages.Where(m => m.ThreadId == message.ThreadId).ToArray());
        public Task SendAsync(Guid owner, MailboxCompose message)
        {
            InstructorMailboxService.Validate(message);
            _messages.Add(new(Guid.NewGuid().ToString(), message.Subject, "instructor@example.invalid", [message.To], [],
                message.Body, DateTimeOffset.UtcNow, true, true, message.ReplyToId is null ? "new-thread" : "thread", []));
            return Task.CompletedTask;
        }
        public Task<MailboxFile> DownloadAsync(Guid owner, string messageId, bool sent, string attachmentId) =>
            Task.FromResult(new MailboxFile("file", "synthetic.txt", "text/plain", "synthetic file"u8.ToArray()));
    }
}
