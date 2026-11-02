using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class InstructorRecordsUiTests(WebHostFixture fixture) : IClassFixture<WebHostFixture>
{
    [UiFact]
    public async Task AdminSeesAllAndAssignsOwnership_InstructorsSeeOnlyOwnRecords()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}").Options);
        var passwordHash = await db.UserAccounts.Where(u => u.Email == "william.guest@wlr.com")
            .Select(u => u.PasswordHash).SingleAsync();
        var suffix = Guid.NewGuid().ToString("N");
        UserAccount Account(string name, string role) => new()
        {
            DisplayName = name, Role = role, Email = $"{name}-{suffix}@example.invalid".ToLowerInvariant(),
            PasswordHash = passwordHash, EmailVerifiedAtUtc = DateTime.UtcNow
        };
        var first = Account("RecordInstructorOne", "Instructor");
        var second = Account("RecordInstructorTwo", "Instructor");
        var admin = Account("RecordAdmin", "Admin");
        var school = new SchoolProfile { State = "RecordTestState" };
        var faculty = new SchoolStaffMember
        {
            SchoolProfileId = school.Id, Name = "PrivateFacultyOne", Role = "Instructor",
            LicenseNumber = "PRIVATE-LICENSE-ONE", LastObservationAtUtc = new DateTime(2026, 4, 9)
        };
        db.UserAccounts.AddRange(first, second, admin);
        db.SchoolProfiles.Add(school);
        db.SchoolStaffMembers.Add(faculty);
        await db.SaveChangesAsync();

        await using var adminContext = await fixture.CreateBrowserContextAsync();
        var adminPage = await adminContext.NewPageAsync();
        await LoginAsync(adminPage, admin.Email);
        await adminPage.GotoAsync($"{fixture.BaseUrl}/instructor/records");
        var stylesheet = await adminContext.APIRequest.GetAsync($"{fixture.BaseUrl}/Lms.Web.styles.css");
        Assert.True(stylesheet.Ok);
        Assert.Contains(".instructor-records", await stylesheet.TextAsync());
        await Assertions.Expect(adminPage.GetByRole(AriaRole.Heading, new() { Name = first.DisplayName, Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(adminPage.GetByRole(AriaRole.Heading, new() { Name = second.DisplayName, Exact = true })).ToBeVisibleAsync();
        var facultyCard = adminPage.Locator("article").Filter(new() { HasText = "PrivateFacultyOne" });
        await Assertions.Expect(facultyCard.Locator("select")).ToBeEnabledAsync();
        await facultyCard.Locator("select").SelectOptionAsync(first.Id.ToString());
        await facultyCard.GetByRole(AriaRole.Button, new() { Name = "Save owner", Exact = true }).ClickAsync();
        await Assertions.Expect(adminPage.GetByRole(AriaRole.Status)).ToHaveTextAsync("Instructor record ownership saved.");

        await using var firstContext = await fixture.CreateBrowserContextAsync();
        var firstPage = await firstContext.NewPageAsync();
        await LoginAsync(firstPage, first.Email);
        await firstPage.GotoAsync($"{fixture.BaseUrl}/instructor/records");
        Assert.True(await firstPage.GetByRole(AriaRole.Link, new() { Name = "Instructor Records", Exact = true }).CountAsync() > 0,
            $"URL: {firstPage.Url}; Page: {await firstPage.Locator("body").InnerTextAsync()}");
        await Assertions.Expect(firstPage.GetByRole(AriaRole.Link, new() { Name = "Instructor Records", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(firstPage.GetByRole(AriaRole.Heading, new() { Name = first.DisplayName, Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(firstPage.GetByText("PRIVATE-LICENSE-ONE", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(firstPage.GetByText(faculty.LastObservationAtUtc!.Value.ToString("MMM d, yyyy"), new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(firstPage.GetByRole(AriaRole.Heading, new() { Name = second.DisplayName, Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(firstPage.GetByRole(AriaRole.Button, new() { Name = "Save owner", Exact = true })).ToHaveCountAsync(0);

        await using var secondContext = await fixture.CreateBrowserContextAsync();
        var secondPage = await secondContext.NewPageAsync();
        await LoginAsync(secondPage, second.Email);
        await secondPage.GotoAsync($"{fixture.BaseUrl}/instructor/records");
        await Assertions.Expect(secondPage.GetByRole(AriaRole.Heading, new() { Name = second.DisplayName, Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(secondPage.GetByText("PRIVATE-LICENSE-ONE", new() { Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(secondPage.GetByText("No faculty records are linked", new() { Exact = false })).ToBeVisibleAsync();
        foreach (var width in new[] { 1440, 390 })
        {
            await firstPage.SetViewportSizeAsync(width, 1000);
            await firstPage.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth", null,
                new() { Timeout = 5000 });
            await adminPage.SetViewportSizeAsync(width, 1000);
            await adminPage.WaitForFunctionAsync("document.documentElement.scrollWidth <= innerWidth", null,
                new() { Timeout = 5000 });
        }
        await firstPage.SetViewportSizeAsync(1440, 1000);
        var tops = await firstPage.EvaluateAsync<double[]>("""
            () => {
                const top = selector => {
                    const range = document.createRange();
                    range.selectNodeContents(document.querySelector(selector));
                    return range.getBoundingClientRect().top;
                };
                return [top('.sidebar h3'), top('.instructor-records h1')];
            }
            """);
        Assert.True(Math.Abs(tops[0] - tops[1]) <= 4, $"Title text alignment: {string.Join(", ", tops)}");
    }

    private async Task LoginAsync(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/login");
        var form = page.Context.APIRequest.CreateFormData();
        form.Set("email", email);
        form.Set("password", "Learner123!");
        form.Set("returnUrl", "/instructor/records");
        var response = await page.Context.APIRequest.PostAsync($"{fixture.BaseUrl}/auth/login", new()
        {
            Form = form
        });
        Assert.True(response.Ok && !response.Url.Contains("/login", StringComparison.Ordinal),
            $"Test sign-in failed: {response.Status} {response.Url}");
    }
}
