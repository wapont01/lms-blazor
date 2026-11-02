using Microsoft.Playwright;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class InstructorMailboxAccessUiTests(WebHostFixture fixture) : IClassFixture<WebHostFixture>
{
    [UiFact]
    public async Task Mailbox_RequiresInstructorRole_AndReportsDisconnectedSetup()
    {
        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "william.guest@wlr.com", "Learner123!");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Mail", Exact = true })).ToHaveCountAsync(0);
        await page.GotoAsync($"{fixture.BaseUrl}/instructor/mail");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "+ Compose" })).ToHaveCountAsync(0);
        await using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE UserAccounts SET Role='Instructor' WHERE Email='william.guest@wlr.com'";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        await using var instructorContext = await fixture.CreateBrowserContextAsync();
        page = await instructorContext.NewPageAsync();
        await LoginAsync(page, "william.guest@wlr.com", "Learner123!");
        await page.GetByRole(AriaRole.Link, new() { Name = "Mail", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Mail", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("not connected");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "+ Compose" })).ToBeDisabledAsync();
    }

    private async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/login");
        await page.Locator("input[name='email']").FillAsync(email);
        await page.Locator("input[name='password']").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("form.auth-form")).Not.ToBeVisibleAsync(new() { Timeout = 15000 });
    }
}
