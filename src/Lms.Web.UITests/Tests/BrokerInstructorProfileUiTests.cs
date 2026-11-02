using System.Security.Claims;
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

public sealed class BrokerInstructorProfileUiTests
{
    [UiFact]
    public async Task BrokerAddsInstructorLicenseFromAccount_KeepingBrokerAccessAndOriginalLicense()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lms.Web"));
        var database = Path.Combine(Path.GetTempPath(), $"lms-broker-instructor-{Guid.NewGuid():N}.db");
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
        builder.Services.AddSingleton(new EmailJourneyOptions { Enabled = true });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IEmailJourneyService, EmailJourneyService>();
        builder.Services.AddSingleton<IEmailService, BlockedEmailService>();
        builder.Services.AddSingleton(new TurnstileOptions());
        builder.Services.AddSingleton<RosterSearchProtection>();
        builder.Services.AddScoped<RosterSearchSession>();
        builder.Services.AddScoped<IRosterRegistrationService, RosterRegistrationService>();
        builder.Services.AddScoped<IInstructorRecordIdentity, InstructorRecordIdentity>();
        builder.Services.AddScoped<InstructorRecordService>();
        await using var app = builder.Build();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();
        app.MapGet("/Lms.Web.styles.css", () => Results.File(
            Path.Combine(root, "obj", "Debug", "net8.0", "scopedcss", "bundle", "Lms.Web.styles.css"), "text/css"));
        app.MapGet("/test/sign-in", async (HttpContext context, IUserAccountService accounts, IAuditLogService audit) =>
        {
            var login = await accounts.AuthenticateAsync("combined-broker@example.invalid", "BrokerTest123!");
            Assert.Equal(LoginStatus.Succeeded, login.Status);
            return await EmailJourneyEndpoints.SignInAsync(context, login.User!, "/account", audit);
        });
        app.MapGet("/test/roles", (HttpContext context) => string.Join(",",
            context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).OrderBy(value => value)))
            .RequireAuthorization();
        app.MapGet("/test/broker-tools", () => Results.Ok())
            .RequireAuthorization(policy => policy.RequireRole("Broker"));
        app.MapGet("/test/instructor-tools", () => Results.Ok())
            .RequireAuthorization(policy => policy.RequireRole("Instructor"));
        app.MapEmailJourney();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        Guid brokerId;
        try
        {
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureCreatedAsync();
                var broker = await scope.ServiceProvider.GetRequiredService<IUserAccountService>()
                    .CreateAsync("combined-broker@example.invalid", "Combined Broker", "BrokerTest123!", "Broker");
                brokerId = broker.Id;
                broker.EmailVerifiedAtUtc = DateTime.UtcNow;
                broker.LicenseNumber = "BK-ORIGINAL";
                var snapshot = new RegulatoryDataSnapshot
                {
                    State = "Florida", Status = RegulatorySnapshotStatuses.Approved,
                    SourceName = "Synthetic instructor roster", RetrievedAtUtc = DateTime.UtcNow
                };
                var brokerLicense = new RegulatoryLicensee
                {
                    SnapshotId = snapshot.Id, State = "Florida", Name = broker.DisplayName,
                    LicenseNumber = "BK-ORIGINAL", LicenseType = "Broker", LicenseStatus = "Active"
                };
                db.RegulatoryDataSnapshots.Add(snapshot);
                db.RegulatoryLicensees.AddRange(brokerLicense, new RegulatoryLicensee
                {
                    SnapshotId = snapshot.Id, State = "Florida", Name = broker.DisplayName,
                    LicenseNumber = "ZH-ADDITIONAL", LicenseType = "Real Estate Instructor",
                    LicenseStatus = "Current,Active", LicenseRenewalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)
                });
                db.RosterAccountRequests.Add(new()
                {
                    UserAccountId = broker.Id, RegulatoryLicenseeId = brokerLicense.Id, Status = "Confirmed"
                });
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{url}/test/sign-in");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "License Profile", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Instructor Records", Exact = true })).ToHaveCountAsync(0);
            Assert.Equal("Broker", await (await page.Context.APIRequest.GetAsync($"{url}/test/roles")).TextAsync());
            Assert.True((await page.Context.APIRequest.GetAsync($"{url}/test/broker-tools")).Ok);
            Assert.False((await page.Context.APIRequest.GetAsync($"{url}/test/instructor-tools",
                new() { MaxRedirects = 0 })).Ok);

            await page.GetByRole(AriaRole.Link, new() { Name = "Add another license", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByLabel("Roster state", new() { Exact = true })).ToBeEnabledAsync();
            await page.GetByLabel("Roster state", new() { Exact = true }).SelectOptionAsync("Florida");
            await page.GetByLabel("Roster license number", new() { Exact = true }).FillAsync("ZH-ADDITIONAL");
            await page.GetByRole(AriaRole.Button, new() { Name = "Find license", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "This is my license record", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Confirm", Exact = true })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Confirm", Exact = true }).ClickAsync();
            await page.WaitForURLAsync($"{url}/account");
            await Assertions.Expect(page.GetByRole(AriaRole.Article, new() { Name = "Florida license ZH-ADDITIONAL", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Article, new() { Name = "Florida license BK-ORIGINAL", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Instructor Records", Exact = true })).ToBeVisibleAsync();
            Assert.Equal("Broker,Instructor", await (await page.Context.APIRequest.GetAsync($"{url}/test/roles")).TextAsync());
            Assert.True((await page.Context.APIRequest.GetAsync($"{url}/test/broker-tools")).Ok);
            Assert.True((await page.Context.APIRequest.GetAsync($"{url}/test/instructor-tools")).Ok);
            await page.GetByRole(AriaRole.Link, new() { Name = "Instructor Records", Exact = true }).ClickAsync();
            await page.WaitForURLAsync($"{url}/instructor/records");
            var records = page.Locator(".instructor-records");
            await Assertions.Expect(records.GetByRole(AriaRole.Heading, new() { Name = "Combined Broker", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(records.GetByRole(AriaRole.Heading, new() { Name = "Florida license ZH-ADDITIONAL", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save owner", Exact = true })).ToHaveCountAsync(0);
            foreach (var width in new[] { 1440, 390 })
            {
                await page.SetViewportSizeAsync(width, 1000);
                await page.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth", null,
                    new() { Timeout = 5000 });
            }
            await using var returningContext = await browser.NewContextAsync();
            var returningPage = await returningContext.NewPageAsync();
            await returningPage.GotoAsync($"{url}/test/sign-in");
            Assert.Equal("Broker,Instructor", await (await returningContext.APIRequest.GetAsync($"{url}/test/roles")).TextAsync());
            var pilot = app.Services.GetRequiredService<EmailJourneyOptions>();
            pilot.Enabled = false;
            Assert.Equal("Broker", await (await returningContext.APIRequest.GetAsync($"{url}/test/roles")).TextAsync());
            pilot.Enabled = true;
            Assert.Equal("Broker,Instructor", await (await returningContext.APIRequest.GetAsync($"{url}/test/roles")).TextAsync());

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var broker = await db.UserAccounts.SingleAsync(user => user.Id == brokerId);
                Assert.Equal("Broker", broker.Role);
                Assert.Equal("BK-ORIGINAL", broker.LicenseNumber);
                Assert.Equal(2, await db.RosterAccountRequests.CountAsync(request => request.UserAccountId == brokerId));
                Assert.False(await db.SchoolStaffMembers.AnyAsync(member => member.UserAccountId == brokerId));
                var instructorSelection = await db.RosterAccountRequests.SingleAsync(request =>
                    request.UserAccountId == brokerId && request.RegulatoryLicensee!.LicenseNumber == "ZH-ADDITIONAL");
                instructorSelection.Status = "Rejected";
                await db.SaveChangesAsync();
            }
            Assert.Equal("Broker", await (await page.Context.APIRequest.GetAsync($"{url}/test/roles")).TextAsync());
            Assert.True((await page.Context.APIRequest.GetAsync($"{url}/test/broker-tools")).Ok);
            Assert.False((await page.Context.APIRequest.GetAsync($"{url}/test/instructor-tools",
                new() { MaxRedirects = 0 })).Ok);
            await page.ReloadAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Instructor Records", Exact = true })).ToHaveCountAsync(0);
        }
        finally
        {
            await app.StopAsync();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                File.Delete(database + suffix);
        }
    }

    private sealed class BlockedEmailService : IEmailService
    {
        public Task<bool> SendAsync(EmailMessage message) =>
            throw new InvalidOperationException("This profile test must not send email.");

        public Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames) =>
            throw new InvalidOperationException("This profile test must not send receipts.");
    }
}
