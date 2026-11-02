using System.Text.RegularExpressions;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Lms.Web.Components;
using Lms.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class NevadaRosterAccountJourneyUiTests
{
    [UiFact]
    public async Task NevadaSearchSelectionRegistrationAndEmailVerificationAutomaticallyConfirmLicenseProfile()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lms.Web"));
        var database = Path.Combine(Path.GetTempPath(), $"lms-nevada-account-{Guid.NewGuid():N}.db");
        const string recipient = "synthetic-nevada@example.invalid";
        const string password = "NevadaJourney123!";
        var mailbox = new CapturedEmailService();
        var journeyOptions = new EmailJourneyOptions { Enabled = true, TestRecipient = recipient };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(App).Assembly.FullName,
            ContentRootPath = root,
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0").UseStaticWebAssets();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddScoped<IClaimsTransformation, InstructorRoleClaims>();
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("login", policy =>
        {
            policy.PermitLimit = 100;
            policy.Window = TimeSpan.FromMinutes(1);
        }));
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();
        builder.Services.AddScoped<INotificationService, NotificationService>();
        builder.Services.AddScoped<IUserAccountService, UserAccountService>();
        builder.Services.AddSingleton(journeyOptions);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IEmailJourneyService, EmailJourneyService>();
        builder.Services.AddSingleton<IEmailService>(mailbox);
        builder.Services.AddSingleton(new TurnstileOptions());
        builder.Services.AddSingleton<RosterSearchProtection>();
        builder.Services.AddScoped<RosterSearchSession>();
        builder.Services.AddScoped<IRosterRegistrationService, RosterRegistrationService>();
        await using var app = builder.Build();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();
        app.MapGet("/Lms.Web.styles.css", () => Results.File(
            Path.Combine(root, "obj", "Debug", "net8.0", "scopedcss", "bundle", "Lms.Web.styles.css"), "text/css"));
        // Establish a profile-viewing session only after the actual email-confirmation flow succeeds.
        app.MapGet("/test/sign-in", async (HttpContext context, IUserAccountService accounts, IAuditLogService audit) =>
        {
            var login = await accounts.AuthenticateAsync(recipient, password);
            Assert.Equal(LoginStatus.Succeeded, login.Status);
            Assert.NotNull(login.User!.EmailVerifiedAtUtc);
            return await EmailJourneyEndpoints.SignInAsync(context, login.User, "/account", audit);
        });
        app.MapEmailJourney();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "Nevada", SourceName = "Synthetic Nevada journey roster",
            SourceUrl = NevadaNredBulkImportService.SourceUrl,
            ParserVersion = NevadaNredBulkImportService.ParserVersion,
            Status = RegulatorySnapshotStatuses.Approved, RetrievedAtUtc = DateTime.UtcNow
        };
        var selected = new RegulatoryLicensee
        {
            SnapshotId = snapshot.Id, State = "Nevada", Name = "Synthetic Nevada Student",
            LicenseNumber = "S.0099001", LicenseType = "Salesperson", LicenseStatus = "Active",
            LicenseIssuedDate = new(2020, 1, 15), LicenseRenewalDate = new(2027, 6, 30)
        };
        try
        {
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.RegulatoryDataSnapshots.Add(snapshot);
                db.RegulatoryLicensees.AddRange(selected, new RegulatoryLicensee
                {
                    SnapshotId = snapshot.Id, State = "Nevada", Name = "Synthetic Nevada Appraiser",
                    LicenseNumber = "A.0099002", LicenseType = "Appraiser", LicenseStatus = "Active"
                });
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            journeyOptions.PublicBaseUrl = url;
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync();
            var externalRequests = new List<string>();
            await page.RouteAsync("**/*", async route =>
            {
                if (new Uri(route.Request.Url).Host != "127.0.0.1")
                {
                    externalRequests.Add(route.Request.Url);
                    await route.AbortAsync();
                }
                else await route.ContinueAsync();
            });
            await page.GotoAsync($"{url}/roster-registration");
            await Assertions.Expect(page.GetByLabel("Roster state", new() { Exact = true })).ToBeEnabledAsync();
            await page.GetByLabel("Roster state", new() { Exact = true }).SelectOptionAsync("Nevada");
            await page.GetByLabel("Roster license number", new() { Exact = true }).FillAsync("A.0099002");
            await page.GetByRole(AriaRole.Button, new() { Name = "Find license", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("No matching records");
            await page.GetByLabel("Roster license number", new() { Exact = true }).FillAsync("0099001");
            await page.GetByRole(AriaRole.Button, new() { Name = "Find license", Exact = true }).ClickAsync();
            var record = page.GetByRole(AriaRole.Article, new() { Name = "Nevada license S.0099001", Exact = true });
            await Assertions.Expect(record).ToContainTextAsync("Synthetic Nevada Student");
            await Assertions.Expect(record).ToContainTextAsync("Salesperson");
            await record.GetByRole(AriaRole.Button, new() { Name = "This is my license record", Exact = true }).ClickAsync();
            await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
            await page.GetByLabel("Confirm password", new() { Exact = true }).FillAsync(password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create Account", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Verification email sent");
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.UserAccounts.SingleAsync();
                Assert.Equal(recipient, user.Email);
                Assert.Equal("Learner", user.Role);
                Assert.Null(user.EmailVerifiedAtUtc);
                var request = await db.RosterAccountRequests.SingleAsync();
                Assert.Equal(selected.Id, request.RegulatoryLicenseeId);
                Assert.Equal(user.Id, request.UserAccountId);
                Assert.Equal("Pending", request.Status);
                Assert.Empty(await db.UserRegulatoryLicenseLinks.ToListAsync());
            }
            var verification = Assert.Single(mailbox.Messages);
            Assert.Equal(recipient, verification.RecipientEmail);
            var match = Regex.Match(verification.HtmlBody, "href=\"([^\"]+)\"");
            Assert.True(match.Success);
            var verificationUrl = match.Groups[1].Value;
            await page.GotoAsync(verificationUrl);
            await page.GetByRole(AriaRole.Button, new() { Name = "Verify email address", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Email verified");
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.UserAccounts.SingleAsync();
                Assert.NotNull(user.EmailVerifiedAtUtc);
                Assert.True(user.EmailTwoStepEnabled);
                var request = await db.RosterAccountRequests.SingleAsync();
                Assert.Equal("Confirmed", request.Status);
                Assert.Equal(selected.Id, request.RegulatoryLicenseeId);
                Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "roster-request.confirmed"));
                Assert.NotNull((await db.EmailChallenges.SingleAsync()).ConsumedAtUtc);
                Assert.Empty(await db.UserRegulatoryLicenseLinks.ToListAsync());
                Assert.Equal("Active", (await db.RegulatoryLicensees.SingleAsync(row => row.Id == selected.Id)).LicenseStatus);
                Assert.Equal(RegulatorySnapshotStatuses.Approved, (await db.RegulatoryDataSnapshots.SingleAsync()).Status);
            }
            await page.GotoAsync(verificationUrl);
            await page.GetByRole(AriaRole.Button, new() { Name = "Verify email address", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
            await page.GotoAsync($"{url}/test/sign-in");
            var profile = page.GetByRole(AriaRole.Region, new() { Name = "License Profile", Exact = true });
            await Assertions.Expect(profile).ToContainTextAsync("S.0099001");
            await Assertions.Expect(profile).ToContainTextAsync("Nevada");
            await Assertions.Expect(profile).ToContainTextAsync("Confirmed");
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Account details", Exact = true }))
                .ToContainTextAsync("Email verified");
            Assert.Empty(externalRequests);
        }
        finally
        {
            await app.StopAsync();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                File.Delete(database + suffix);
        }
    }

    private sealed class CapturedEmailService : IEmailService
    {
        public List<EmailMessage> Messages { get; } = [];
        public Task<bool> SendAsync(EmailMessage message)
        {
            Messages.Add(message);
            return Task.FromResult(true);
        }
        public Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames) =>
            throw new InvalidOperationException("The roster journey must not send receipts.");
    }
}
