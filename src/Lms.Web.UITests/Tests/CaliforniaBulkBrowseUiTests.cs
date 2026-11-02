using Lms.Application.Tests.Services;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class CaliforniaBulkBrowseUiTests : IAsyncLifetime
{
    private CaliforniaArchive? _archive;
    private WebHostFixture? _fixture;

    public async Task InitializeAsync()
    {
        _archive = await CaliforniaArchive.CreateAsync(additionalLicenseCount: 51);
        _fixture = new WebHostFixture();
        _fixture.ImportCaliforniaArchiveBeforeStartup(_archive.Path);
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }

        _archive?.Dispose();
    }

    [UiFact]
    public async Task CaliforniaBulkBrowserDisplaysRe776DatesAndFlagMeanings()
    {
        var fixture = _fixture ?? throw new InvalidOperationException("The UI fixture was not initialized.");
        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, fixture);
        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
        await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("California");

        var browse = page.GetByRole(AriaRole.Region, new() { Name = "California bulk license browser", Exact = true });
        await browse.GetByRole(AriaRole.Button, new() { Name = "Search California bulk licenses", Exact = true }).ClickAsync();

        var table = browse.GetByRole(AriaRole.Table, new() { Name = "Filtered California bulk licenses", Exact = true });
        await Assertions.Expect(table).ToBeVisibleAsync();
        await Assertions.Expect(table.Locator("thead th")).ToHaveTextAsync(
            ["Name", "License", "Role", "DRE status", "Effective date", "Expiration date", "Original license date",
                "Related license", "Broker-associate relationships", "Licensee record marker", "Restriction flag", "Next-renewal CE scope"]);

        var salesperson = table.Locator("tbody tr")
            .Filter(new() { HasText = "00001234" })
            .Filter(new() { HasText = "Salesperson" });
        await Assertions.Expect(salesperson).ToHaveCountAsync(1);
        var salespersonCells = salesperson.Locator("td, th");
        await Assertions.Expect(salespersonCells.Nth(3)).ToHaveTextAsync("Licensed NBA");
        await Assertions.Expect(salespersonCells.Nth(4)).ToHaveTextAsync("2020-01-01");
        await Assertions.Expect(salespersonCells.Nth(5)).ToHaveTextAsync("2027-12-31");
        await Assertions.Expect(salespersonCells.Nth(6)).ToHaveTextAsync("2019-01-01");
        await Assertions.Expect(salespersonCells.Nth(7)).ToContainTextAsync("Broker 00009999");
        await Assertions.Expect(salespersonCells.Nth(9)).ToHaveTextAsync("First/only record (N)");
        await Assertions.Expect(salespersonCells.Nth(10)).ToHaveTextAsync("Flagged (Y)");
        await Assertions.Expect(salespersonCells.Nth(11))
            .ToHaveTextAsync("Ethics, Agency, Trust Fund & Fair Housing only (Y)");

        var officer = table.Locator("tbody tr")
            .Filter(new() { HasText = "00001234" })
            .Filter(new() { Has = page.GetByRole(AriaRole.Cell, new() { Name = "Officer", Exact = true }) })
            .First;
        await Assertions.Expect(officer.Locator("td, th").Nth(9)).ToHaveTextAsync("Earlier record exists (Y)");

        var fieldGuide = browse.GetByText("DRE field meanings (RE 776)", new() { Exact = true });
        await fieldGuide.ClickAsync();
        await Assertions.Expect(browse).ToContainTextAsync("at least one earlier record for that licensee was already written in the file");
        await Assertions.Expect(browse).ToContainTextAsync("the next renewal requires only Ethics, Agency, Trust Fund, and Fair Housing");

        await browse.GetByLabel("California bulk license role", new() { Exact = true }).SelectOptionAsync("Officer");
        await browse.GetByLabel("California bulk name or license", new() { Exact = true }).FillAsync("00001234");
        await browse.GetByLabel("California bulk published status", new() { Exact = true }).FillAsync("nba");
        await browse.GetByRole(AriaRole.Button, new() { Name = "Search California bulk licenses", Exact = true }).ClickAsync();
        await Assertions.Expect(browse.GetByRole(AriaRole.Status)).ToContainTextAsync("2 matching role records");
        await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(2);
        await Assertions.Expect(table.Locator("tbody tr").First.Locator("td, th").Nth(3)).ToHaveTextAsync("Licensed NBA");
        await Assertions.Expect(table.Locator("tbody tr").Last.Locator("td, th").Nth(3)).ToHaveTextAsync("Licensed NBA");

        await browse.GetByLabel("California bulk license role", new() { Exact = true }).SelectOptionAsync("Broker");
        await browse.GetByLabel("California bulk name or license", new() { Exact = true }).FillAsync("Smith");
        await browse.GetByRole(AriaRole.Button, new() { Name = "Search California bulk licenses", Exact = true }).ClickAsync();
        await Assertions.Expect(browse.GetByRole(AriaRole.Status)).ToContainTextAsync("1 matching role records");
        var brokerAssociate = table.Locator("tbody tr").Filter(new() { HasText = "00005678" });
        await Assertions.Expect(brokerAssociate.Locator("td, th").Nth(2)).ToContainTextAsync("associate");
        await Assertions.Expect(brokerAssociate.Locator("td, th").Nth(8))
            .ToContainTextAsync("Corporation 00009012 Example Company (since 2023-01-10)");

        await browse.GetByLabel("California bulk license role", new() { Exact = true }).SelectOptionAsync("");
        await browse.GetByLabel("California bulk name or license", new() { Exact = true }).FillAsync("");
        await browse.GetByLabel("California bulk published status", new() { Exact = true }).FillAsync("");
        await browse.GetByRole(AriaRole.Button, new() { Name = "Search California bulk licenses", Exact = true }).ClickAsync();
        await Assertions.Expect(browse.GetByRole(AriaRole.Status)).ToContainTextAsync("58 matching role records");
        var pages = browse.GetByRole(AriaRole.Navigation, new() { Name = "California bulk pages", Exact = true });
        await Assertions.Expect(pages).ToContainTextAsync("Page 1 of 2");
        await pages.GetByRole(AriaRole.Button, new() { Name = "Next California bulk records", Exact = true }).ClickAsync();
        await Assertions.Expect(pages).ToContainTextAsync("Page 2 of 2");
        await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(8);
        await pages.GetByRole(AriaRole.Button, new() { Name = "Previous California bulk records", Exact = true }).ClickAsync();
        await Assertions.Expect(pages).ToContainTextAsync("Page 1 of 2");

        await browse.GetByLabel("California bulk license role", new() { Exact = true }).SelectOptionAsync("");
        await browse.GetByLabel("California bulk name or license", new() { Exact = true }).FillAsync("No matching synthetic license");
        await browse.GetByRole(AriaRole.Button, new() { Name = "Search California bulk licenses", Exact = true }).ClickAsync();
        await Assertions.Expect(browse.GetByRole(AriaRole.Status)).ToContainTextAsync("0 matching role records");
        await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(1);
        await Assertions.Expect(table.Locator("tbody tr")).ToContainTextAsync("No licenses match these filters.");
    }

    [UiFact]
    public async Task CaliforniaBulkBrowserShowsSearchErrorWhenAssociateArchiveIsMissing()
    {
        var fixture = _fixture ?? throw new InvalidOperationException("The UI fixture was not initialized.");
        var associateFile = Directory.GetFiles(fixture.CaliforniaArchiveDirectory, "broker_associates_list.xls",
            SearchOption.AllDirectories).Single();
        File.Delete(associateFile);

        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, fixture);
        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
        await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("California");

        var browse = page.GetByRole(AriaRole.Region, new() { Name = "California bulk license browser", Exact = true });
        await browse.GetByRole(AriaRole.Button, new() { Name = "Search California bulk licenses", Exact = true }).ClickAsync();
        await Assertions.Expect(browse.GetByRole(AriaRole.Alert))
            .ToHaveTextAsync("California roster could not be searched. Try again or inspect collection status.");
    }

    private static async Task LoginAsync(IPage page, WebHostFixture fixture)
    {
        var form = page.Context.APIRequest.CreateFormData();
        form.Set("email", "admin@wlr.com");
        form.Set("password", "Admin123!");
        form.Set("returnUrl", "/admin/regulatory-statistics");
        var response = await page.Context.APIRequest.PostAsync($"{fixture.BaseUrl}/auth/login", new() { Form = form });
        Assert.Equal(200, response.Status);
    }
}

public sealed class CaliforniaBulkBrowseUnavailableUiTests : IAsyncLifetime
{
    private WebHostFixture? _fixture;

    public async Task InitializeAsync()
    {
        _fixture = new WebHostFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [UiFact]
    public async Task CaliforniaBulkBrowserExplainsWhenNoCompletedArchiveIsAvailable()
    {
        var fixture = _fixture ?? throw new InvalidOperationException("The UI fixture was not initialized.");
        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        var form = page.Context.APIRequest.CreateFormData();
        form.Set("email", "admin@wlr.com");
        form.Set("password", "Admin123!");
        form.Set("returnUrl", "/admin/regulatory-statistics");
        var response = await page.Context.APIRequest.PostAsync($"{fixture.BaseUrl}/auth/login", new() { Form = form });
        Assert.Equal(200, response.Status);

        await page.GotoAsync($"{fixture.BaseUrl}/admin/regulatory-statistics");
        await page.GetByLabel("Statistics state", new() { Exact = true }).SelectOptionAsync("California");
        var browse = page.GetByRole(AriaRole.Region, new() { Name = "California bulk license browser", Exact = true });
        await browse.GetByRole(AriaRole.Button, new() { Name = "Search California bulk licenses", Exact = true }).ClickAsync();
        await Assertions.Expect(browse).ToContainTextAsync("No completed California bulk collection is available.");
        await Assertions.Expect(browse.GetByRole(AriaRole.Table, new() { Name = "Filtered California bulk licenses", Exact = true }))
            .ToHaveCountAsync(0);
    }
}
