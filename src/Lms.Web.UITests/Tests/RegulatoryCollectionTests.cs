using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class RegulatoryCollectionTests
{
    [Theory]
    [InlineData("Florida", "Current, Active", "Active")]
    [InlineData("Florida", "CURRENT,INACTIVE", "Inactive")]
    [InlineData("Florida", "Current", "Current (activity unspecified)")]
    [InlineData("Florida", "Revoked", "Revoked")]
    [InlineData("Idaho", "Current, Active", "Current, Active")]
    public void StatusDisplaySimplifiesOnlyKnownFloridaActivityLabels(string state, string raw, string expected) =>
        Assert.Equal(expected, RegulatoryStatusDisplay.Format(state, raw));

    [Fact]
    public void CitySearchMatchesAddressTextIgnoringCaseAndWhitespace_WithoutInventingMissingCities()
    {
        var sample = new CollectedLicenseSample("Sample", "SL1", "Sales Associate", "Active", "", "Dade", "",
            Addresses: [new("Main", "123 MAIN ST MIAMI, FL 33101")]);
        Assert.True(FloridaCollectionFilters.MatchesCity(sample, " miami "));
        Assert.False(FloridaCollectionFilters.MatchesCity(sample, "Orlando"));
        Assert.False(FloridaCollectionFilters.MatchesCity(sample with { Addresses = null }, "Miami"));
        Assert.True(FloridaCollectionFilters.MatchesCity(sample with { Addresses = null }, " "));
    }

    [Fact]
    public void FloridaCatalogHasAllOfficialTypes_AndConfigEnablesOnlyBrokerSalesAndInstructorInitially()
    {
        Assert.Equal(12, FloridaCollectionFilters.LicenseTypes.Count);
        Assert.Equal(12, FloridaCollectionFilters.LicenseTypes.Select(type => type.Code).Distinct().Count());
        var options = new RegulatoryCollectionOptions();
        Assert.Equal(["2501", "2505"], options.EnabledFloridaLicenseTypes);
        options.Validate();
        options.EnabledFloridaLicenseTypes = ["2501", "2505", "2513"];
        options.Validate();
        options.EnabledFloridaLicenseTypes = ["invalid"];
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(1, 144466, 50)]
    [InlineData(1800, 144466, 90000)]
    [InlineData(2890, 144466, 144466)]
    [InlineData(1, 18, 18)]
    [InlineData(1, 0, 0)]
    public void DbprSavedEntryProgressUsesSavedPagesAndCapsLastPage(int page, int total, long expected)
    {
        var partition = new CollectionPartition("Real Estate Broker or Sales", "Dade", page, total, (total + 49) / 50, false);
        Assert.Equal(expected, partition.DbprEntriesSaved);
    }

    [Fact]
    public void CurrentSearchMatchesCountyAndCategory_NotInFlightPageOrAnotherPartition()
    {
        using var handler = new SyntheticCollectionHandler();
        var status = handler.Status with
        {
            Page = 4,
            Partitions = [
                new("Real Estate Instructor", "Dade", 1, 18, 1, true),
                new("Real Estate Broker or Sales", "Dade", 3, 144466, 2890, false)]
        };
        Assert.Equal(150, status.CurrentSearch!.DbprEntriesSaved);
        Assert.Null((status with { County = "Unstarted" }).CurrentSearch);
    }

    [Fact]
    public async Task ReadsExactCollectorSchemaAndValidatesReconciledCounters()
    {
        using var handler = new SyntheticCollectionHandler();
        using var http = new HttpClient(handler);
        var client = Client(http);
        var data = await client.GetAsync("Florida", CancellationToken.None);
        Assert.Equal(2, data.Status.PagesSaved);
        Assert.Equal(5, data.Status.RawSearchEntries);
        Assert.Equal(2, data.Status.DistinctNumberedLicenses);
        Assert.Equal(1, data.Status.StagedLicenses);
        Assert.Equal(67, data.Status.CountyTotal);
        Assert.Equal(2, Assert.Single(data.Status.Professionals).Licenses);
        Assert.Equal("SL123", Assert.Single(data.Sample).LicenseNumber);
        handler.Status = handler.Status with { DistinctNumberedLicenses = 3 };
        await Assert.ThrowsAsync<JsonException>(() => client.GetAsync("Florida", CancellationToken.None));
        handler.Status = handler.Status with { DistinctNumberedLicenses = 2, Scope = "" };
        await Assert.ThrowsAsync<JsonException>(() => client.GetAsync("Florida", CancellationToken.None));
    }

    [Fact]
    public async Task MissingInvalidAndUnavailableResponsesAreNotZeroCountSuccesses()
    {
        using var handler = new SyntheticCollectionHandler();
        using var http = new HttpClient(handler);
        var client = Client(http);
        handler.StatusJsonOverride = "{}";
        await Assert.ThrowsAsync<JsonException>(() => client.GetAsync("Florida", CancellationToken.None));
        handler.StatusJsonOverride = "not json";
        await Assert.ThrowsAsync<JsonException>(() => client.GetAsync("Florida", CancellationToken.None));
        handler.Failing = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("Florida", CancellationToken.None));
        Assert.False(client.IsConfigured("Idaho"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("Idaho", CancellationToken.None));
    }

    [Fact]
    public async Task ExportsAreAllowlistedAndUseTheConfiguredSource()
    {
        using var handler = new SyntheticCollectionHandler();
        using var http = new HttpClient(handler);
        var client = Client(http);
        foreach (var resource in new[] { "licenses.csv", "licenses.json", "entries.json" })
        {
            using var response = await client.OpenExportAsync("Florida", resource, CancellationToken.None);
            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal($"http://127.0.0.1:5011/{resource}", handler.LastUri!.AbsoluteUri);
        }
        await Assert.ThrowsAsync<ArgumentException>(() => client.OpenExportAsync("Florida", "../status.json", CancellationToken.None));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://127.0.0.1:5011/path")]
    [InlineData("http://user:password@localhost:5011/")]
    [InlineData("http://localhost:5011/?target=x")]
    public void RejectsNonLoopbackOrNonRootSources(string source)
    {
        var options = new RegulatoryCollectionOptions();
        options.Sources["Florida"] = source;
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [UiFact]
    public async Task AdminSeesFullLiveDashboard_AutoUpdatesAndDownloads_WithoutChangingOtherStateComparisons()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lms.Web"));
        var testRoot = Path.Combine(Directory.GetCurrentDirectory(), ".florida-bulk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var database = Path.Combine(testRoot, "collection.db");
        using var handler = new BlockingBulkHandler();
        using var lookupHandler = new SyntheticCollectionHandler();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(App).Assembly.FullName, ContentRootPath = root,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0").UseStaticWebAssets();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddScoped<INotificationService, NotificationService>();
        builder.Services.AddScoped<RegulatoryStatisticsService>();
        builder.Services.AddSingleton(new RegulatoryCollectionOptions());
        builder.Services.AddSingleton<IRegulatoryCollectionClient>(Client(new HttpClient(lookupHandler, disposeHandler: false)));
        var bulkOptions = new FloridaBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = Path.Combine(testRoot, "stored") };
        builder.Services.AddSingleton(bulkOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<FloridaBulkCollectionOptions>>(Microsoft.Extensions.Options.Options.Create(bulkOptions));
        builder.Services.AddScoped<FloridaBulkImportService>();
        builder.Services.AddSingleton<FloridaBulkCollectionService>();
        builder.Services.AddSingleton<FloridaBulkBrowseService>();
        var texasOptions = new TexasBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = Path.Combine(testRoot, "texas-stored") };
        builder.Services.AddSingleton(texasOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<TexasBulkCollectionOptions>>(Microsoft.Extensions.Options.Options.Create(texasOptions));
        builder.Services.AddScoped<TexasBulkImportService>();
        builder.Services.AddSingleton<TexasBulkCollectionService>();
        builder.Services.AddScoped<TexasBulkBrowseService>();
        var californiaOptions = new CaliforniaBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = Path.Combine(testRoot, "california-stored") };
        builder.Services.AddSingleton(californiaOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<CaliforniaBulkCollectionOptions>>(Microsoft.Extensions.Options.Options.Create(californiaOptions));
        builder.Services.AddScoped<CaliforniaBulkImportService>();
        builder.Services.AddSingleton<CaliforniaBulkCollectionService>();
        builder.Services.AddScoped<CaliforniaBulkBrowseService>();
        var coloradoOptions = new ColoradoDoraBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = Path.Combine(testRoot, "colorado-stored") };
        builder.Services.AddSingleton(coloradoOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<ColoradoDoraBulkCollectionOptions>>(Microsoft.Extensions.Options.Options.Create(coloradoOptions));
        builder.Services.AddSingleton<ColoradoDoraBulkCollectionService>();
        var nevadaOptions = new NevadaBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = Path.Combine(testRoot, "nevada-stored") };
        builder.Services.AddSingleton(nevadaOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<NevadaBulkCollectionOptions>>(Microsoft.Extensions.Options.Options.Create(nevadaOptions));
        builder.Services.AddSingleton<NevadaBulkCollectionService>();
        var newYorkOptions = new NewYorkBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = Path.Combine(testRoot, "new-york-stored") };
        builder.Services.AddSingleton(newYorkOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<NewYorkBulkCollectionOptions>>(Microsoft.Extensions.Options.Options.Create(newYorkOptions));
        builder.Services.AddSingleton<NewYorkBulkCollectionService>();
        var arizonaOptions = new ArizonaBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = Path.Combine(testRoot, "arizona-stored") };
        builder.Services.AddSingleton(arizonaOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<ArizonaBulkCollectionOptions>>(Microsoft.Extensions.Options.Options.Create(arizonaOptions));
        builder.Services.AddSingleton<ArizonaBulkCollectionService>();
        var connecticutOptions = new ConnecticutBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = Path.Combine(testRoot, "connecticut-stored") };
        builder.Services.AddSingleton(connecticutOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<ConnecticutBulkCollectionOptions>>(Microsoft.Extensions.Options.Options.Create(connecticutOptions));
        builder.Services.AddSingleton<ConnecticutBulkCollectionService>();
        builder.Services.AddHostedService<FloridaBulkRefreshBackgroundService>();
        builder.Services.AddHttpClient("FloridaBulk").ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var app = builder.Build();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapGet("/Lms.Web.styles.css", () => Results.File(
            Path.Combine(root, "obj", "Debug", "net8.0", "scopedcss", "bundle", "Lms.Web.styles.css"), "text/css"));
        app.MapGet("/test/sign-in/{role}", async (string role, HttpContext context) =>
        {
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.LocalRedirect("/admin/regulatory-statistics");
        });
        app.MapFloridaBulkCollectionExports();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        try
        {
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureCreatedAsync();
            }
            await app.StartAsync();
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            await using var admin = await browser.NewContextAsync();
            var page = await admin.NewPageAsync();
            await page.GotoAsync($"{url}/test/sign-in/Admin");
            var bulk = page.GetByRole(AriaRole.Region, new() { Name = "Florida bulk collection", Exact = true });
            await Assertions.Expect(bulk).ToBeVisibleAsync();
            await Assertions.Expect(bulk).ToContainTextAsync("Idle");
            await Assertions.Expect(bulk).ToContainTextAsync("Disabled");
            await Assertions.Expect(bulk.GetByRole(AriaRole.Button, new() { Name = "Collect now", Exact = true })).ToBeEnabledAsync();
            await Assertions.Expect(page.GetByLabel("Statistics state")).ToBeEnabledAsync();
            Assert.Equal(0, handler.Calls);
            Assert.Equal(404, (await admin.APIRequest.GetAsync($"{url}/admin/florida-bulk/exports/manifest.json")).Status);
            var archive = await FloridaBulkTestFixture.CreateArchiveAsync(testRoot);
            await app.Services.GetRequiredService<FloridaBulkCollectionService>().ImportArchiveAsync(archive);
            await Assertions.Expect(bulk).ToContainTextAsync("Completed", new() { Timeout = 15000 });
            await Assertions.Expect(bulk.GetByRole(AriaRole.Table, new() { Name = "Florida bulk files", Exact = true }).Locator("tbody tr")).ToHaveCountAsync(6);
            await Assertions.Expect(bulk).ToContainTextAsync("Imported archive (not downloaded)");
            foreach (var resource in FloridaBulkImportService.Definitions.Select(d => d.Name).Append("manifest.json"))
            {
                var response = await admin.APIRequest.GetAsync($"{url}/admin/florida-bulk/exports/{resource}");
                Assert.Equal(200, response.Status);
                Assert.Contains("attachment", response.Headers["content-disposition"]);
                Assert.Equal("no-store", response.Headers["cache-control"]);
                Assert.Equal("nosniff", response.Headers["x-content-type-options"]);
                Assert.NotEmpty(await response.BodyAsync());
            }
            Assert.Equal(404, (await admin.APIRequest.GetAsync($"{url}/admin/florida-bulk/exports/unknown.json")).Status);
            Assert.Equal(0, handler.Calls);
            await Assertions.Expect(bulk.GetByRole(AriaRole.Link, new() { Name = "Real Estate Commission - bulk downloads", Exact = true }))
                .ToHaveAttributeAsync("href", "https://www2.myfloridalicense.com/real-estate-commission/public-records/");
            await Assertions.Expect(bulk.GetByRole(AriaRole.Link, new() { Name = "Real Estate Appraisal Board - bulk downloads", Exact = true })).ToBeVisibleAsync();
            await bulk.GetByRole(AriaRole.Button, new() { Name = "Collect now", Exact = true }).ClickAsync();
            var stop = bulk.GetByRole(AriaRole.Button, new() { Name = "Cancel collection", Exact = true });
            await Assertions.Expect(bulk.GetByRole(AriaRole.Button)).ToHaveCountAsync(1);
            await Assertions.Expect(stop).ToBeEnabledAsync(new() { Timeout = 15000 });
            await Assertions.Expect(bulk.GetByRole(AriaRole.Progressbar, new() { Name = "Bulk collection phase progress", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(bulk).ToContainTextAsync("Elapsed time");
            await Assertions.Expect(bulk).ToContainTextAsync("Remaining time is not yet known");
            await Assertions.Expect(stop).ToHaveTextAsync("Cancel collection");
            await stop.ClickAsync();
            await Assertions.Expect(bulk).ToContainTextAsync("Cancelled", new() { Timeout = 15000 });
            var collectAgain = bulk.GetByRole(AriaRole.Button, new() { Name = "Collect now", Exact = true });
            await Assertions.Expect(collectAgain).ToBeEnabledAsync();
            await Assertions.Expect(bulk.GetByRole(AriaRole.Button)).ToHaveCountAsync(1);
            var retained = await admin.APIRequest.GetAsync($"{url}/admin/florida-bulk/exports/manifest.json");
            Assert.Equal(200, retained.Status);
            foreach (var width in new[] { 1440, 390 })
            {
                await page.SetViewportSizeAsync(width, 1000);
                await page.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth", null, new() { Timeout = 5000 });
            }
            await page.GetByLabel("Statistics state").SelectOptionAsync("Idaho");
            await Assertions.Expect(bulk).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Idaho", Exact = true })).ToBeVisibleAsync();
            await using var denied = await browser.NewContextAsync();
            var deniedPage = await denied.NewPageAsync();
            var exportUrl = $"{url}/admin/florida-bulk/exports/REALESTATE2501LICENSE_1.csv";
            Assert.Equal(302, (await denied.APIRequest.GetAsync(exportUrl, new() { MaxRedirects = 0 })).Status);
            await deniedPage.GotoAsync($"{url}/test/sign-in/Learner");
            Assert.Equal(302, (await denied.APIRequest.GetAsync(exportUrl, new() { MaxRedirects = 0 })).Status);
            await Assertions.Expect(deniedPage.GetByRole(AriaRole.Region, new() { Name = "Florida bulk collection", Exact = true })).ToHaveCountAsync(0);
        }
        finally
        {
            await app.StopAsync();
            Directory.Delete(testRoot, true);
            var parent = Path.GetDirectoryName(testRoot)!;
            if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
    }

    private static RegulatoryCollectionClient Client(HttpClient http)
    {
        var options = new RegulatoryCollectionOptions();
        options.Sources["Florida"] = "http://127.0.0.1:5011/";
        options.Validate();
        return new(http, options);
    }
}

internal sealed class SyntheticCollectionHandler : HttpMessageHandler
{
    public RegulatoryCollectionStatus Status { get; set; } = new()
    {
        State = "collecting", SnapshotId = Guid.NewGuid(), Scope = "67 Florida counties; all statuses",
        ExcludedGeographies = ["Out of State", "Foreign", "Unknown"], StageStatus = "PendingReview",
        Started = DateTimeOffset.UtcNow.AddHours(-1), Updated = DateTimeOffset.UtcNow, LastExportAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        PagesSaved = 2, RawSearchEntries = 5, DistinctEntries = 4, DistinctNumberedLicenses = 2,
        StagedLicenses = 1, PartitionsComplete = 0, PartitionsTotal = 67,
        County = "Dade", Category = "Real Estate Broker or Sales", Page = 2,
        Professionals = [new("Sales associates", "2501", 2, 1, 0, 1,
            new() { ["Current,Active"] = 1, ["Null and Void"] = 1 }, 0, "In progress / partial")],
        Partitions = [new("Real Estate Broker or Sales", "Dade", 2, 100, 2, false)]
    };
    public bool Failing { get; set; }
    public bool ExportMissing { get; set; }
    public string? StatusJsonOverride { get; set; }
    public Uri? LastUri { get; private set; }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastUri = request.RequestUri;
        if (Failing) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var path = request.RequestUri!.AbsolutePath;
        if (ExportMissing && path is "/licenses.csv" or "/licenses.json" or "/entries.json")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var content = path switch
        {
            "/status.json" => StatusJsonOverride ?? JsonSerializer.Serialize(Status, JsonOptions),
            "/sample.json" => JsonSerializer.Serialize(new[] {
                new CollectedLicenseSample("Synthetic sample", "SL123", "Sales Associate", "Current,Active",
                    "10/30/2028", "Dade", "javascript:alert(1)",
                    Addresses: [new("Main Address*:", "123 MAIN ST MIAMI, FL 33101")]) }, JsonOptions),
            "/licenses.csv" => "name,license_number\nsynthetic,SL123\n",
            _ => "[{\"name\":\"synthetic\"}]"
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, path.EndsWith(".csv", StringComparison.Ordinal) ? "text/csv" : "application/json")
        });
    }
}
