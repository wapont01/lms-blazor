using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class AdminRegulatoryStatisticsUiTests(WebHostFixture fixture) : IClassFixture<WebHostFixture>
{
    // Statistics for manual collections reload automatically every 15 seconds.
    private static readonly LocatorAssertionsToHaveTextOptions AutoUpdate = new() { Timeout = 25_000 };

    [UiFact]
    public async Task NevadaShowsCollectDataAndSearchesCurrentImportedLicensesWithoutApproval()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}").Options);
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "Nevada", SourceName = "Synthetic Nevada current roster",
            ParserVersion = NevadaNredBulkImportService.ParserVersion,
            Status = RegulatorySnapshotStatuses.Approved, RetrievedAtUtc = DateTime.UtcNow
        };
        db.RegulatoryDataSnapshots.Add(snapshot);
        db.RegulatoryLicensees.Add(new()
        {
            State = "Nevada", SnapshotId = snapshot.Id, Name = "Synthetic Nevada licensee",
            LicenseNumber = "S.UI123", LicenseType = "Salesperson", LicenseStatus = "Active"
        });
        await db.SaveChangesAsync();
        try
        {
            await using var context = await fixture.CreateBrowserContextAsync();
            var page = await context.NewPageAsync();
            await LoginAsync(page, "admin@wlr.com", "Admin123!");
            await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
            await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("Nevada");
            var collector = page.GetByRole(AriaRole.Region, new() { Name = "Nevada bulk collection", Exact = true });
            await Assertions.Expect(collector.GetByRole(AriaRole.Button, new() { Name = "Collect data", Exact = true })).ToBeEnabledAsync();
            await Assertions.Expect(collector.GetByRole(AriaRole.Button)).ToHaveCountAsync(1);
            var lookup = page.GetByRole(AriaRole.Region, new() { Name = "Nevada license lookup", Exact = true });
            await lookup.GetByLabel("Nevada name or license", new() { Exact = true }).FillAsync("s.ui123");
            await lookup.GetByRole(AriaRole.Button, new() { Name = "Search Nevada licenses" }).ClickAsync();
            await Assertions.Expect(lookup.GetByRole(AriaRole.Table)).ToContainTextAsync("Synthetic Nevada licensee");
            await Assertions.Expect(lookup.GetByRole(AriaRole.Status)).ToContainTextAsync("1 matching licenses");
            var comparison = page.GetByRole(AriaRole.Region, new() { Name = "Comparison scope", Exact = true });
            await Assertions.Expect(comparison).ToContainTextAsync("Synthetic Nevada current roster");
            await Assertions.Expect(comparison).Not.ToContainTextAsync("Pending review");
            await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-directory?state=Nevada");
            await Assertions.Expect(page.GetByLabel("Bulk roster state", new() { Exact = true })).ToHaveValueAsync("Nevada");
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Nevada license lookup", Exact = true })).ToBeVisibleAsync();
            Assert.Empty(await db.UserRegulatoryLicenseLinks.Where(link => db.RegulatoryLicensees.Any(
                row => row.Id == link.RegulatoryLicenseeId && row.SnapshotId == snapshot.Id)).ToListAsync());
        }
        finally
        {
            db.RegulatoryLicensees.RemoveRange(db.RegulatoryLicensees.Where(row => row.SnapshotId == snapshot.Id));
            db.RegulatoryDataSnapshots.Remove(snapshot);
            await db.SaveChangesAsync();
        }
    }

    [UiFact]
    public async Task AdminCanConfigureIndependentStateCollectionSchedules()
    {
        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "admin@wlr.com", "Admin123!");
        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-schedules");

        var schedules = page.GetByRole(AriaRole.Table, new() { Name = "State collection schedules", Exact = true });
        await Assertions.Expect(schedules.Locator("tbody tr")).ToHaveCountAsync(6);
        var nevadaCadence = schedules.GetByLabel("Nevada publication cadence", new() { Exact = true });
        await nevadaCadence.Locator("summary").ClickAsync();
        await Assertions.Expect(nevadaCadence).ToContainTextAsync("Monthly");
        var nevadaRow = schedules.Locator("tbody tr").Filter(new() { HasText = "Nevada" });
        await Assertions.Expect(nevadaRow).ToContainTextAsync("Recommended collection: 1 Month, not every 7 Days");
        await Assertions.Expect(schedules.Locator("tbody tr").Filter(new() { HasText = "Florida" }))
            .ToContainTextAsync("Recommended collection: 1 Week");
        await Assertions.Expect(schedules.Locator("tbody tr").Filter(new() { HasText = "Texas" }))
            .ToContainTextAsync("Recommended collection: 1 Day");
        await Assertions.Expect(schedules.Locator("tbody tr").Filter(new() { HasText = "California" }))
            .ToContainTextAsync("Recommended collection: 1 Day");
        await Assertions.Expect(schedules.Locator("tbody tr").Filter(new() { HasText = "Colorado" }))
            .ToContainTextAsync("Recommended collection: not established");
        await Assertions.Expect(nevadaCadence).ToContainTextAsync("Active Licensee Lists PORTAL.txt");
        // The link is inside <details>; an interactive re-render can collapse it, so check the anchor directly.
        await Assertions.Expect(nevadaCadence.Locator("a")).ToHaveAttributeAsync("href",
            "https://red.nv.gov/Content/Administration/Active_Licensee_Lists/");
        await Assertions.Expect(schedules.GetByLabel("Colorado publication cadence", new() { Exact = true }).Locator("summary"))
            .ToContainTextAsync("Not verified");
        await Assertions.Expect(schedules.GetByLabel("Texas publication cadence", new() { Exact = true }).Locator("summary"))
            .ToContainTextAsync("Daily");
        await Assertions.Expect(schedules.GetByLabel("Nevada time zone")).ToHaveValueAsync("America/Los_Angeles");
        await Assertions.Expect(schedules).ToContainTextAsync("Colorado");
        await Assertions.Expect(schedules.GetByLabel("Colorado time zone")).ToHaveValueAsync("America/Denver");
        await Assertions.Expect(schedules.GetByLabel("Texas time zone")).ToHaveValueAsync("America/Chicago");
        await Assertions.Expect(schedules.GetByLabel("New York time zone")).ToHaveValueAsync("America/New_York");
        await Assertions.Expect(schedules.Locator("tbody tr").Filter(new() { HasText = "New York" }))
            .ToContainTextAsync("Recommended collection: 1 Day");
        await Assertions.Expect(schedules.GetByLabel("California time zone")).ToHaveValueAsync("America/Los_Angeles");
        await Assertions.Expect(schedules.GetByLabel("Florida time zone")).ToHaveValueAsync("America/New_York");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("New state collectors appear here when implemented");

        var colorado = schedules.Locator("tbody tr").Filter(new() { HasText = "Colorado" });
        await colorado.GetByLabel("Colorado automatic updates").CheckAsync();
        await colorado.GetByLabel("Colorado interval", new() { Exact = true }).FillAsync("2");
        await colorado.GetByLabel("Colorado interval unit").SelectOptionAsync("Weeks");
        await colorado.GetByLabel("Colorado local update time").FillAsync("03:45");
        await colorado.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(colorado.GetByLabel("Colorado interval", new() { Exact = true })).ToHaveValueAsync("2");
        await Assertions.Expect(colorado).ToContainTextAsync("(America/Denver)");
    }

    [UiFact]
    public async Task ColoradoShowsTheSelectedRealEstateDatasetAndManualRosterRequest()
    {
        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "admin@wlr.com", "Admin123!");
        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
        await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("Colorado");

        var notice = page.Locator("[aria-label='Colorado eLicense source']");
        await Assertions.Expect(notice).ToBeVisibleAsync();
        await Assertions.Expect(notice).ToContainTextAsync("Licensed Real Estate Professionals in Colorado");
        await Assertions.Expect(notice).ToContainTextAsync("dataset version 2468");
        await Assertions.Expect(notice).ToContainTextAsync("This is the source selected for Colorado real-estate imports");
        await Assertions.Expect(notice).ToContainTextAsync("an admin must provide the files for import");
        await Assertions.Expect(notice.GetByRole(AriaRole.Link, new() { Name = "Open the DORA real-estate license dataset" }))
            .ToHaveAttributeAsync("href", "https://data.colorado.gov/Regulations/Licensed-Real-Estate-Professionals-in-Colorado/4zse-6bnw/about_data");
        await Assertions.Expect(notice.GetByRole(AriaRole.Link, new() { Name = "Open Colorado DRE eLicense roster generator" }))
            .ToHaveAttributeAsync("href", "https://apps2.colorado.gov/dre/licensing/Lookup/GenerateRoster.aspx");
        await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Colorado bulk collection", Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(notice).ToContainTextAsync("Colorado's existing eLicense snapshot is retained.");
    }

    [UiFact]
    public async Task FloridaActivatedRosterShowsSynchronizationWithoutApprovalControls()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}").Options);
        var previous = new RegulatoryDataSnapshot
        {
            State = "Florida", SourceName = "Previous synthetic roster", Status = RegulatorySnapshotStatuses.Approved,
            RetrievedAtUtc = DateTime.UtcNow.AddDays(-1), ParserVersion = FloridaBulkImportService.ParserVersion
        };
        var current = new RegulatoryDataSnapshot
        {
            State = "Florida", SourceName = "Current synthetic roster", Status = RegulatorySnapshotStatuses.Approved,
            RetrievedAtUtc = DateTime.UtcNow, ParserVersion = FloridaBulkImportService.ParserVersion
        };
        db.RegulatoryDataSnapshots.AddRange(previous, current);
        db.RegulatoryLicensees.AddRange(
            new RegulatoryLicensee { SnapshotId = previous.Id, State = "Florida", LicenseNumber = "ZH-HISTORY", Name = "Previous synthetic instructor", LicenseType = "Real Estate Instructor" },
            new RegulatoryLicensee { SnapshotId = current.Id, State = "Florida", LicenseNumber = "ZH-HISTORY", Name = "Current synthetic instructor", LicenseType = "Real Estate Instructor" });
        await db.SaveChangesAsync();
        try
        {
            await using var context = await fixture.CreateBrowserContextAsync();
            var page = await context.NewPageAsync();
            await LoginAsync(page, "admin@wlr.com", "Admin123!");
            await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
            var scope = page.GetByRole(AriaRole.Region, new() { Name = "Comparison scope", Exact = true });
            await Assertions.Expect(scope).ToContainTextAsync("Previous synthetic roster");
            await Assertions.Expect(scope).ToContainTextAsync("Current synthetic roster");
            await Assertions.Expect(scope).ToContainTextAsync("Synchronized");
            await Assertions.Expect(scope).Not.ToContainTextAsync("Pending review");
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Record comparison", Exact = true }))
                .ToContainTextAsync("Previous and current roster");
            await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-directory");
            var row = page.Locator("tr").Filter(new() { HasText = "Current synthetic roster" });
            await Assertions.Expect(row).ToContainTextAsync("Synchronized");
            await Assertions.Expect(row.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true })).ToHaveCountAsync(0);
            var history = page.GetByRole(AriaRole.Region, new() { Name = "Florida roster history", Exact = true });
            await Assertions.Expect(history.Locator("tr").Filter(new() { HasText = current.Id.ToString() })).ToContainTextAsync("Current roster");
            await page.SetViewportSizeAsync(390, 1000);
            await history.GetByRole(AriaRole.Button, new() { Name = $"Restore version {previous.Id}", Exact = true }).ClickAsync();
            var confirm = history.GetByRole(AriaRole.Group, new() { Name = "Confirm roster restore", Exact = true });
            await Assertions.Expect(history.GetByRole(AriaRole.Table, new() { Name = "Historical roster licensees", Exact = true }).Locator("tbody tr")).ToHaveCountAsync(1);
            await page.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth", null, new() { Timeout = 5000 });
            await Assertions.Expect(confirm.GetByRole(AriaRole.Button, new() { Name = "Confirm restore", Exact = true })).ToBeDisabledAsync();
            await confirm.GetByLabel("Restore reason", new() { Exact = true }).FillAsync("Synthetic UI recovery test");
            await confirm.GetByRole(AriaRole.Checkbox).CheckAsync();
            await confirm.GetByRole(AriaRole.Button, new() { Name = "Confirm restore", Exact = true }).ClickAsync();
            await Assertions.Expect(history).ToContainTextAsync("Roster restored.");
            await Assertions.Expect(history.Locator("tr").Filter(new() { HasText = previous.Id.ToString() })).ToContainTextAsync("Current roster");
            await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
            await Assertions.Expect(scope).ToContainTextAsync("Previous synthetic roster");
            var currentSource = page.Locator("dt").Filter(new() { HasText = "Current roster" }).Locator("xpath=following-sibling::dd[1]");
            await Assertions.Expect(currentSource).ToContainTextAsync("Previous synthetic roster");
        }
        finally
        {
            db.RegulatoryDataSnapshots.RemoveRange(previous, current);
            await db.SaveChangesAsync();
        }
    }

    [UiFact]
    public async Task FloridaUsesBulkCollectionAndFullArchiveFilters_WithoutChangingApprovedSearch()
    {
        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "admin@wlr.com", "Admin123!");
        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
        var selection = page.GetByRole(AriaRole.Region, new() { Name = "Statistics selection", Exact = true });
        await Assertions.Expect(selection.GetByRole(AriaRole.Combobox)).ToHaveCountAsync(1);
        await Assertions.Expect(selection.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);
        await Assertions.Expect(selection.GetByRole(AriaRole.Link)).ToHaveCountAsync(0);
        var scope = page.GetByRole(AriaRole.Region, new() { Name = "Comparison scope", Exact = true });
        await Assertions.Expect(scope.GetByRole(AriaRole.Combobox)).ToHaveCountAsync(0);
        await Assertions.Expect(scope.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);
        await Assertions.Expect(scope.GetByRole(AriaRole.Link)).ToHaveCountAsync(0);
        Assert.True(await scope.EvaluateAsync<bool>(
            "element => element.previousElementSibling?.getAttribute('aria-label') === 'Florida bulk license browser'"));
        var bulk = page.GetByRole(AriaRole.Region, new() { Name = "Florida bulk collection", Exact = true });
        await Assertions.Expect(bulk).ToBeVisibleAsync();
        await Assertions.Expect(bulk).Not.ToContainTextAsync("bytes");
        await Assertions.Expect(bulk.GetByRole(AriaRole.Columnheader, new() { Name = "Size", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(bulk.GetByRole(AriaRole.Link, new() { Name = "Lookup for individual verification", Exact = true })).ToHaveCountAsync(0);
        var action = bulk.GetByRole(AriaRole.Button, new() { Name = "Collect now", Exact = true });
        await Assertions.Expect(action).ToBeEnabledAsync();
        await Assertions.Expect(bulk.GetByRole(AriaRole.Button)).ToHaveCountAsync(1);
        var collectBounds = await action.BoundingBoxAsync();
        var cancelBounds = collectBounds;
        Assert.NotNull(collectBounds);
        Assert.NotNull(cancelBounds);
        Assert.True(cancelBounds!.X + cancelBounds.Width <= page.ViewportSize!.Width);
        Assert.True(Math.Abs(collectBounds!.Y - cancelBounds.Y) < 2);
        await Assertions.Expect(bulk).ToContainTextAsync("Disabled");
        await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Live collection progress", Exact = true })).ToHaveCountAsync(0);
        var browse = page.GetByRole(AriaRole.Region, new() { Name = "Florida bulk license browser", Exact = true });
        await Assertions.Expect(browse).ToBeVisibleAsync();
        await page.GetByLabel("Bulk license type", new() { Exact = true }).SelectOptionAsync("2501");
        await Assertions.Expect(page.GetByLabel("Bulk special qualification", new() { Exact = true })).ToBeEnabledAsync();
        await page.GetByLabel("Bulk special qualification", new() { Exact = true }).SelectOptionAsync("BL");
        await page.GetByLabel("Bulk city", new() { Exact = true }).FillAsync("MIAMI");
        await page.GetByLabel("Bulk county", new() { Exact = true }).FillAsync("Dade");
        await page.GetByRole(AriaRole.Button, new() { Name = "Search bulk licenses", Exact = true }).ClickAsync();
        await Assertions.Expect(browse).ToContainTextAsync("No completed bulk collection is available.");
        await page.GetByLabel("Bulk license type", new() { Exact = true }).SelectOptionAsync("appraisal");
        await Assertions.Expect(page.GetByLabel("Bulk special qualification", new() { Exact = true })).ToBeDisabledAsync();
        await Assertions.Expect(page.GetByLabel("Bulk special qualification", new() { Exact = true })).ToHaveValueAsync("");
        Assert.Equal(404, (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/admin/florida-bulk/exports/unknown.csv")).Status);
        Assert.Equal(404, (await context.APIRequest.GetAsync($"{fixture.BaseUrl}/admin/florida-bulk/exports/manifest.json")).Status);
        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}").Options))
        {
            Assert.False(await db.RegulatoryDataSnapshots.AnyAsync(snapshot =>
                snapshot.State == "Florida" && snapshot.ParserVersion != null && snapshot.ParserVersion.StartsWith("florida-bulk")));
        }
        foreach (var width in new[] { 1440, 390 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            await page.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth", null, new() { Timeout = 5000 });
        }
        await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("Idaho");
        await Assertions.Expect(bulk).ToHaveCountAsync(0);
        await Assertions.Expect(browse).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Live collection progress", Exact = true }))
            .ToContainTextAsync("No live collection feed is configured for Idaho");
        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-directory");
        await Assertions.Expect(bulk).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Other sources: import normalized snapshot", Exact = true })).ToBeVisibleAsync();
        await using var denied = await fixture.CreateBrowserContextAsync();
        var deniedPage = await denied.NewPageAsync();
        await LoginAsync(deniedPage, "william.guest@wlr.com", "Learner123!");
        await deniedPage.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
        await Assertions.Expect(deniedPage.GetByRole(AriaRole.Region, new() { Name = "Florida bulk collection", Exact = true })).ToHaveCountAsync(0);
        var deniedExport = await denied.APIRequest.GetAsync($"{fixture.BaseUrl}/admin/florida-bulk/exports/manifest.json", new() { MaxRedirects = 0 });
        Assert.NotEqual(200, deniedExport.Status);
    }

    [UiFact]
    public async Task AdminComparesAndRefreshesStateRecords_AndOtherRolesCannotAccess()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}").Options);
        const string state = "Statistics test jurisdiction";
        var baseline = new RegulatoryDataSnapshot
        {
            State = state, SourceName = "Approved synthetic baseline", Status = RegulatorySnapshotStatuses.Approved,
            RetrievedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var incoming = new RegulatoryDataSnapshot
        {
            State = state, SourceName = "Synthetic collection - INCOMPLETE - DO NOT APPROVE",
            RetrievedAtUtc = DateTime.UtcNow
        };
        RegulatoryLicensee License(RegulatoryDataSnapshot snapshot, string number, string status = "Active") =>
            new() { Snapshot = snapshot, State = state, LicenseNumber = number, Name = "Synthetic licensee", LicenseType = "Salesperson", LicenseStatus = status };
        db.RegulatoryDataSnapshots.AddRange(baseline, incoming);
        db.RegulatoryLicensees.AddRange(
            License(baseline, "unchanged"), License(baseline, "changed"), License(baseline, "not-collected"),
            License(incoming, "unchanged"), License(incoming, "changed", "Inactive"), License(incoming, "new"));
        await db.SaveChangesAsync();

        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "State collection statistics", Exact = true })).ToHaveCountAsync(0);
        await LoginAsync(page, "admin@wlr.com", "Admin123!");
        await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync(state);
        var comparison = page.GetByRole(AriaRole.Region, new() { Name = "Record comparison", Exact = true });
        var row = comparison.Locator("tbody tr").Filter(new() { HasText = "Salesperson" });
        await Assertions.Expect(row.Locator("td, th")).ToHaveTextAsync(["Licensees", "Salesperson", "3", "3", "1", "1", "1"]);
        var statuses = page.GetByRole(AriaRole.Region, new() { Name = "Published license statuses", Exact = true });
        var transitions = page.GetByRole(AriaRole.Table, new() { Name = "Source status transitions", Exact = true });
        await Assertions.Expect(transitions.Locator("tbody tr").Locator("td, th")).ToHaveTextAsync(
            ["Licensees", "ACTIVE → INACTIVE", "1"]);
        var active = statuses.Locator("tbody tr").Filter(new() { Has = page.GetByRole(AriaRole.Rowheader, new() { Name = "Active", Exact = true }) });
        var inactive = statuses.Locator("tbody tr").Filter(new() { Has = page.GetByRole(AriaRole.Rowheader, new() { Name = "Inactive", Exact = true }) });
        await Assertions.Expect(active.Locator("td, th")).ToHaveTextAsync(["Licensees", "Active", "3", "2", "1", "0", "- 1"]);
        await Assertions.Expect(inactive.Locator("td, th")).ToHaveTextAsync(["Licensees", "Inactive", "0", "1", "0", "+ 1", "0"]);
        await Assertions.Expect(active.Locator(".increment-negative")).ToHaveCSSAsync("color", "rgb(220, 38, 38)");
        await Assertions.Expect(inactive.Locator(".increment-positive").Last).ToHaveCSSAsync("color", "rgb(37, 99, 235)");
        var details = page.GetByRole(AriaRole.Table, new() { Name = "Reconciled licenses", Exact = true });
        await Assertions.Expect(details).ToContainTextAsync("CHANGED");
        await Assertions.Expect(details).ToContainTextAsync("ACTIVE → INACTIVE");
        await page.GetByLabel("Reconciliation classification").SelectOptionAsync("Not observed");
        await Assertions.Expect(details).ToContainTextAsync("NOT-COLLECTED");
        await page.GetByLabel("Reconciliation classification").SelectOptionAsync("New license");
        await Assertions.Expect(details).ToContainTextAsync("NEW");
        await Assertions.Expect(page.GetByText("Incomplete collection.", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByLabel("Incoming snapshot")).ToHaveValueAsync(incoming.Id.ToString());
        var stylesheet = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/Lms.Web.styles.css");
        Assert.True(stylesheet.Ok);
        Assert.Contains(".state-statistics", await stylesheet.TextAsync());

        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Refresh statistics", Exact = true })).ToHaveCountAsync(0);
        db.RegulatoryLicensees.Add(License(incoming, "new-batch"));
        await db.SaveChangesAsync();
        await Assertions.Expect(row.Locator("td, th")).ToHaveTextAsync(["Licensees", "Salesperson", "3", "4", "2", "1", "1"], AutoUpdate);
        db.RegulatoryLicensees.AddRange(License(baseline, "baseline-only-1"), License(baseline, "baseline-only-2"));
        await db.SaveChangesAsync();
        await Assertions.Expect(row.Locator("td, th")).ToHaveTextAsync(["Licensees", "Salesperson", "5", "4", "2", "1", "1"], AutoUpdate);
        await Assertions.Expect(active.Locator(".increment-negative")).ToHaveTextAsync("- 1");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview,
            await db.RegulatoryDataSnapshots.Where(snapshot => snapshot.Id == incoming.Id).Select(snapshot => snapshot.Status).SingleAsync());
        for (var index = 0; index < 51; index++)
            db.RegulatoryLicensees.Add(License(incoming, $"page-test-{index:D2}"));
        await db.SaveChangesAsync();
        await Assertions.Expect(details.Locator("tbody tr")).ToHaveCountAsync(50, new() { Timeout = AutoUpdate.Timeout });
        await page.GetByRole(AriaRole.Button, new() { Name = "Next records", Exact = true }).ClickAsync();
        await Assertions.Expect(details.Locator("tbody tr")).ToHaveCountAsync(3);
        await page.GetByLabel("Reconciliation classification").SelectOptionAsync("Status changed");
        await Assertions.Expect(details.Locator("tbody tr")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Previous records", Exact = true })).ToBeDisabledAsync();

        foreach (var width in new[] { 1440, 390 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            await page.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth", null, new() { Timeout = 5000 });
        }
        await page.SetViewportSizeAsync(1440, 1000);
        var tops = await page.EvaluateAsync<double[]>("""
            () => {
                const top = selector => {
                    const range = document.createRange();
                    range.selectNodeContents(document.querySelector(selector));
                    return range.getBoundingClientRect().top;
                };
                return [top('.sidebar h3'), top('.state-statistics h1')];
            }
            """);
        Assert.True(Math.Abs(tops[0] - tops[1]) <= 4, $"Title text alignment: {string.Join(", ", tops)}");
        await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("Idaho");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Idaho", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Synthetic collection", new() { Exact = false })).ToHaveCountAsync(0);

        foreach (var account in new[]
        {
            ("william.guest@wlr.com", "Learner123!"),
            ("william.broker@wlr.com", "Broker123!"),
            ("jordan.lee@wlr.com", "Instructor123!")
        })
        {
            await using var deniedContext = await fixture.CreateBrowserContextAsync();
            var deniedPage = await deniedContext.NewPageAsync();
            await LoginAsync(deniedPage, account.Item1, account.Item2);
            await Assertions.Expect(deniedPage.GetByRole(AriaRole.Heading, new() { Name = "State collection statistics", Exact = true })).ToHaveCountAsync(0);
            await Assertions.Expect(deniedPage.GetByLabel("Statistics state")).ToHaveCountAsync(0);
        }
    }

    [UiFact]
    public async Task TexasUsesSharedCollectionCurrentComparisonAndStateScopedRecoveryWithAdminOnlyExports()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}").Options);
        var previous = new RegulatoryDataSnapshot { State = "Texas", SourceName = "Previous Texas synthetic",
            Status = RegulatorySnapshotStatuses.Approved, ParserVersion = TexasBulkImportService.ParserVersion,
            RetrievedAtUtc = DateTime.UtcNow.AddDays(-1), ActivatedAtUtc = DateTime.UtcNow.AddDays(-1) };
        var current = new RegulatoryDataSnapshot { State = "Texas", SourceName = "Current Texas synthetic",
            Status = RegulatorySnapshotStatuses.Approved, ParserVersion = TexasBulkImportService.ParserVersion,
            RetrievedAtUtc = DateTime.UtcNow, ActivatedAtUtc = DateTime.UtcNow };
        db.AddRange(previous, current);
        foreach (var snapshot in new[] { previous, current })
        {
            db.RegulatoryFirms.Add(new() { State = "Texas", SnapshotId = snapshot.Id, LicenseNumber = "001-BB", LegalName = "Texas example company" });
            db.RegulatoryLicensees.Add(new() { State = "Texas", SnapshotId = snapshot.Id, LicenseNumber = "001-SA",
                Name = "Texas example agent", LicenseType = "Sales Agent", LicenseStatus = "Suspended" });
        }
        await db.SaveChangesAsync();
        try
        {
            await using var context = await fixture.CreateBrowserContextAsync();
            var page = await context.NewPageAsync();
            await LoginAsync(page, "admin@wlr.com", "Admin123!");
            await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("Texas");
            var bulk = page.GetByRole(AriaRole.Region, new() { Name = "Texas bulk collection", Exact = true });
            await Assertions.Expect(bulk).ToBeVisibleAsync();
            await Assertions.Expect(bulk.GetByRole(AriaRole.Button, new() { Name = "Collect now", Exact = true })).ToBeEnabledAsync();
            await Assertions.Expect(bulk.GetByRole(AriaRole.Link, new() { Name = "Texas Real Estate Commission - bulk downloads" }))
                .ToHaveAttributeAsync("href", "https://www.trec.texas.gov/public/high-value-data-sets");
            await Assertions.Expect(bulk).Not.ToContainTextAsync("bytes");
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Florida bulk collection", Exact = true })).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Texas bulk license browser", Exact = true })).ToBeVisibleAsync();
            var scope = page.GetByRole(AriaRole.Region, new() { Name = "Comparison scope", Exact = true });
            await Assertions.Expect(scope).ToContainTextAsync("Current Texas synthetic");
            await Assertions.Expect(scope).ToContainTextAsync("Previous Texas synthetic");
            await Assertions.Expect(scope.GetByRole(AriaRole.Combobox)).ToHaveCountAsync(0);
            await Assertions.Expect(scope).Not.ToContainTextAsync("Pending review");
            var response = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/admin/texas-bulk/exports/unknown.csv");
            Assert.Equal(404, response.Status);
            await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-directory?state=Texas");
            var history = page.GetByRole(AriaRole.Region, new() { Name = "Texas roster history", Exact = true });
            await Assertions.Expect(history).ToBeVisibleAsync();
            await page.SetViewportSizeAsync(390, 1000);
            await history.GetByRole(AriaRole.Button, new() { Name = $"Restore version {previous.Id}", Exact = true }).ClickAsync();
            await Assertions.Expect(history.GetByRole(AriaRole.Table, new() { Name = "Historical roster licensees" }))
                .ToContainTextAsync("001-SA");
            await page.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth", null, new() { Timeout = 5000 });
            await history.GetByLabel("Restore reason", new() { Exact = true }).FillAsync("Texas synthetic UI recovery");
            await history.GetByRole(AriaRole.Checkbox).CheckAsync();
            await history.GetByRole(AriaRole.Button, new() { Name = "Confirm restore", Exact = true }).ClickAsync();
            await Assertions.Expect(history).ToContainTextAsync("Roster restored.");
            await Assertions.Expect(history.Locator("tr").Filter(new() { HasText = previous.Id.ToString() })).ToContainTextAsync("Current roster");
            await using var denied = await fixture.CreateBrowserContextAsync();
            var deniedResponse = await denied.APIRequest.GetAsync($"{fixture.BaseUrl}/admin/texas-bulk/exports/license-holders.csv",
                new() { MaxRedirects = 0 });
            Assert.Contains(deniedResponse.Status, new[] { 302, 401, 403 });
        }
        finally
        {
            db.RemoveRange(previous, current);
            await db.SaveChangesAsync();
        }
    }

    [UiFact]
    public async Task CaliforniaPreservesMultipleOfficerRelationshipsAndSharedRecoveryControls()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}").Options);
        var previous = new RegulatoryDataSnapshot { State = "California", SourceName = "Previous California synthetic",
            ParserVersion = CaliforniaBulkImportService.ParserVersion, Status = RegulatorySnapshotStatuses.Approved,
            RetrievedAtUtc = DateTime.UtcNow.AddDays(-1), ActivatedAtUtc = DateTime.UtcNow.AddDays(-1) };
        var current = new RegulatoryDataSnapshot { State = "California", SourceName = "Current California synthetic",
            ParserVersion = CaliforniaBulkImportService.ParserVersion, Status = RegulatorySnapshotStatuses.Approved,
            RetrievedAtUtc = DateTime.UtcNow, ActivatedAtUtc = DateTime.UtcNow };
        db.AddRange(previous, current);
        foreach (var snapshot in new[] { previous, current })
            foreach (var corporation in new[] { "00009012", "00009013" })
                db.RegulatoryLicensees.Add(new() { State = "California", SnapshotId = snapshot.Id,
                    LicenseNumber = "00001234", Name = "California synthetic officer", LicenseType = "Officer",
                    LicenseStatus = "Licensed NBA", RelatedLicenseNumber = corporation, RelatedLicenseType = "Corporation",
                    IsAdditionalLicenseRecord = true });
        await db.SaveChangesAsync();
        try
        {
            await using var context = await fixture.CreateBrowserContextAsync();
            var page = await context.NewPageAsync();
            await LoginAsync(page, "admin@wlr.com", "Admin123!");
            await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("California");
            var bulk = page.GetByRole(AriaRole.Region, new() { Name = "California bulk collection", Exact = true });
            await Assertions.Expect(bulk).ToBeVisibleAsync();
            await Assertions.Expect(bulk).ToContainTextAsync("The examinee list is separate and excluded.");
            await Assertions.Expect(bulk).Not.ToContainTextAsync("Education information only");
            await Assertions.Expect(bulk).Not.ToContainTextAsync("bytes");
            await Assertions.Expect(bulk.GetByRole(AriaRole.Button, new() { Name = "Collect now", Exact = true })).ToBeEnabledAsync();
            var action = bulk.GetByRole(AriaRole.Button, new() { Name = "Collect now", Exact = true });
            await Assertions.Expect(action).ToBeEnabledAsync();
            await Assertions.Expect(bulk.GetByRole(AriaRole.Button)).ToHaveCountAsync(1);
            var comparison = page.GetByRole(AriaRole.Region, new() { Name = "Record comparison", Exact = true });
            await Assertions.Expect(comparison.Locator("tbody tr").Filter(new() { HasText = "Officer" }).Locator("td, th"))
                .ToHaveTextAsync(["Licensees", "Officer", "2", "2", "0", "0", "2"]);
            var browse = page.GetByRole(AriaRole.Region, new() { Name = "California bulk license browser", Exact = true });
            await Assertions.Expect(browse.GetByRole(AriaRole.Combobox)).ToBeEnabledAsync();
            await browse.GetByRole(AriaRole.Combobox).SelectOptionAsync("Officer");
            await browse.GetByRole(AriaRole.Button, new() { Name = "Search California bulk licenses", Exact = true }).ClickAsync();
            await Assertions.Expect(browse).ToContainTextAsync("No completed California bulk collection is available.");
            var unknown = await context.APIRequest.GetAsync($"{fixture.BaseUrl}/admin/california-bulk/exports/ExamineeList.zip");
            Assert.Equal(404, unknown.Status);
            await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-directory?state=California");
            var history = page.GetByRole(AriaRole.Region, new() { Name = "California roster history", Exact = true });
            await Assertions.Expect(history).ToBeVisibleAsync();
            await page.SetViewportSizeAsync(390, 1000);
            await history.GetByRole(AriaRole.Button, new() { Name = $"Restore version {previous.Id}", Exact = true }).ClickAsync();
            await Assertions.Expect(history.GetByRole(AriaRole.Table, new() { Name = "Historical roster licensees" }))
                .ToContainTextAsync("00001234");
            await page.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth");
            await history.GetByLabel("Restore reason", new() { Exact = true }).FillAsync("California synthetic recovery");
            await history.GetByRole(AriaRole.Checkbox).CheckAsync();
            await history.GetByRole(AriaRole.Button, new() { Name = "Confirm restore", Exact = true }).ClickAsync();
            await Assertions.Expect(history).ToContainTextAsync("Roster restored.");
            await using var denied = await fixture.CreateBrowserContextAsync();
            var response = await denied.APIRequest.GetAsync($"{fixture.BaseUrl}/admin/california-bulk/exports/CurrList.zip",
                new() { MaxRedirects = 0 });
            Assert.Contains(response.Status, new[] { 302, 401, 403 });
        }
        finally
        {
            db.RemoveRange(previous, current);
            await db.SaveChangesAsync();
        }
    }

    private async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/login");
        var form = page.Context.APIRequest.CreateFormData();
        form.Set("email", email);
        form.Set("password", password);
        form.Set("returnUrl", "/admin/regulatory-statistics");
        var response = await page.Context.APIRequest.PostAsync($"{fixture.BaseUrl}/auth/login", new() { Form = form });
        if (response.Status == 429)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            response = await page.Context.APIRequest.PostAsync($"{fixture.BaseUrl}/auth/login", new() { Form = form });
        }
        Assert.True(response.Ok && !response.Url.Contains("/login", StringComparison.Ordinal),
            $"Test sign-in failed: {response.Status} {response.Url}");
        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
    }
}
