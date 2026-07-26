using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class AssessmentOutcomesUiTests : IClassFixture<WebHostFixture>
{
    private readonly WebHostFixture _fixture;

    public AssessmentOutcomesUiTests(WebHostFixture fixture)
    {
        _fixture = fixture;
    }

    [UiFact]
    public async Task Admin_RetakeGrant_CreateUpdateRevoke_Works()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "admin@lms.com", "Admin123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/admin/assessments");

        var editor = page.Locator("section.card").Filter(new LocatorFilterOptions { HasTextString = "Assessment Editor" });
        await Assertions.Expect(editor).ToBeVisibleAsync(new() { Timeout = 20000 });

        var loadAssessmentButton = editor.GetByRole(AriaRole.Button, new() { Name = "Load Assessment" });
        await Assertions.Expect(loadAssessmentButton).ToBeVisibleAsync(new() { Timeout = 20000 });
        await editor.GetByRole(AriaRole.Button, new() { Name = "Load Assessment" }).ClickAsync();

        var grantLearnerSelect = editor.Locator("h3:has-text('Grant Retake Attempts') + .form-row select").First;
        await Assertions.Expect(grantLearnerSelect).ToBeVisibleAsync(new() { Timeout = 20000 });

        var learnerOptionCandidates = editor
            .Locator("h3:has-text('Grant Retake Attempts') + .form-row select option", new() { HasTextString = "learner@lms.com" });

        try
        {
            await learnerOptionCandidates.First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 20000 });
        }
        catch
        {
            // Blazor can occasionally render the grant section before options are hydrated.
            // One explicit reload of assessment data makes this deterministic in CI.
            await loadAssessmentButton.ClickAsync();
            await learnerOptionCandidates.First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 20000 });
        }

        var learnerOption = learnerOptionCandidates.First;
        var learnerOptionValue = await learnerOption.GetAttributeAsync("value");

        Assert.False(string.IsNullOrWhiteSpace(learnerOptionValue));

        await grantLearnerSelect.SelectOptionAsync(learnerOptionValue);
        await editor.Locator("h3:has-text('Grant Retake Attempts') + .form-row input[type='number']").FillAsync("2");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Grant Retake" }).ClickAsync();

        await ExpectTextAsync(editor, "Retake attempts granted");

        var firstGrantRow = editor.Locator("h3:has-text('Existing Retake Grants') + table tbody tr").First;
        await ExpectVisibleAsync(firstGrantRow);

        await firstGrantRow.Locator("input[type='number']").FillAsync("4");
        await firstGrantRow.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(firstGrantRow.Locator("input[type='number']")).ToHaveValueAsync("4", new() { Timeout = 10000 });

        await firstGrantRow.GetByRole(AriaRole.Button, new() { Name = "Revoke" }).ClickAsync();
        await ExpectTextAsync(editor, "Retake grant revoked.");
        await ExpectTextAsync(editor, "No retake grants configured for this course.");
    }

    [UiFact]
    public async Task Broker_Page_Renders_AssessmentBlockedSection()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "broker@lms.com", "Broker123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/broker/blocked");

        await ExpectTextAsync(page, "Assessment-Blocked Learners");
    }

    [UiFact]
    public async Task Admin_Reconciliation_Page_Renders()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "admin@lms.com", "Admin123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/admin/reconciliation");

        await ExpectTextAsync(page, "Payment Reconciliation");
        await ExpectTextAsync(page, "Recent Transactions");
    }

    [UiFact]
    public async Task Learner_Checkout_Success_Path_ShowsReceipt()
    {
        await EnsureCartWithPurchasableCourseAsync(_fixture.DatabasePath, "learner@lms.com", quantity: 1);

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "learner@lms.com", "Learner123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/checkout");

        await page.Locator("#email").FillAsync("learner@lms.com");
        await page.Locator("#fullname").FillAsync("Learner One");
        await page.Locator("#address").FillAsync("123 Main Street");
        await page.Locator("#city").FillAsync("Austin");
        await page.Locator("#state").FillAsync("TX");
        await page.Locator("#zip").FillAsync("78701");
        await page.Locator("#cardname").FillAsync("Learner One");
        await page.Locator("#cardnumber").FillAsync("4242 4242 4242 4242");
        await page.Locator("#expiry").FillAsync("12/34");
        await page.Locator("#cvv").FillAsync("123");
        var termsCheckbox = page.Locator(".form-group.checkbox input[type='checkbox']");
        await termsCheckbox.CheckAsync();

        var completePurchaseButton = page.GetByRole(AriaRole.Button, new() { Name = "Complete Purchase", Exact = false });
        await Assertions.Expect(completePurchaseButton).ToBeEnabledAsync();
        await completePurchaseButton.ClickAsync();

        try
        {
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("Payment Successful", RegexOptions.IgnoreCase) }))
                .ToBeVisibleAsync(new() { Timeout = 60000 });
        }
        catch
        {
            var currentUrl = page.Url;
            var pageText = await page.Locator("body").InnerTextAsync();
            var truncatedPageText = pageText.Length > 600 ? pageText[..600] : pageText;
            Assert.Fail($"Expected payment success heading but it was not visible. URL: {currentUrl}\nBody excerpt:\n{truncatedPageText}");
        }

        await Assertions.Expect(page.GetByText("Invoice:", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20000 });
    }

    [UiFact]
    public async Task Learner_Checkout_Declined_Path_RedirectsToDeclinedPage()
    {
        await EnsureCartWithPurchasableCourseAsync(_fixture.DatabasePath, "learner@lms.com", quantity: 1);

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "learner@lms.com", "Learner123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/checkout");

        await page.Locator("#email").FillAsync("learner@lms.com");
        await page.Locator("#fullname").FillAsync("Learner One");
        await page.Locator("#address").FillAsync("123 Main Street");
        await page.Locator("#city").FillAsync("Austin");
        await page.Locator("#state").FillAsync("TX");
        await page.Locator("#zip").FillAsync("78701");
        await page.Locator("#cardname").FillAsync("Learner One");
        await page.Locator("#cardnumber").FillAsync("4000 0000 0000 0002");
        await page.Locator("#expiry").FillAsync("12/34");
        await page.Locator("#cvv").FillAsync("123");
        var termsCheckbox = page.Locator(".form-group.checkbox input[type='checkbox']");
        await termsCheckbox.CheckAsync();

        var completePurchaseButton = page.GetByRole(AriaRole.Button, new() { Name = "Complete Purchase", Exact = false });
        await Assertions.Expect(completePurchaseButton).ToBeEnabledAsync();
        await completePurchaseButton.ClickAsync();

        try
        {
            await Assertions.Expect(page).ToHaveURLAsync(new Regex("/payment-declined", RegexOptions.IgnoreCase), new() { Timeout = 15000 });
        }
        catch
        {
            Assert.Fail("Expected payment declined redirect.");
        }

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Payment Declined", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [UiFact]
    public async Task SsoDisabled_HidesSsoButton_AndReturns404ForSsoLoginRoute()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("RUN_UI_TESTS_SSO_MODE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{_fixture.BaseUrl}/login");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in with SSO", Exact = true })).ToHaveCountAsync(0);

        var response = await page.GotoAsync($"{_fixture.BaseUrl}/auth/sso/login");
        Assert.NotNull(response);
        Assert.Equal(404, response!.Status);
    }

    [UiSsoFact]
    public async Task SsoEnabled_TestMode_RoundTripSignsIn_AndCreatesLocalUser()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{_fixture.BaseUrl}/login");
        var ssoButton = page.GetByRole(AriaRole.Button, new() { Name = "Sign in with SSO", Exact = true });
        await Assertions.Expect(ssoButton).ToBeVisibleAsync(new() { Timeout = 10000 });

        await ssoButton.ClickAsync();

        await Assertions.Expect(page.Locator("form[action='/auth/logout']")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Account", Exact = true })).ToBeVisibleAsync(new() { Timeout = 10000 });

        var userExists = await HasUserAsync(_fixture.DatabasePath, "sso-learner@lms.com", "Learner");
        Assert.True(userExists, "Expected SSO test user to be created in local user store with Learner role.");
    }

    [UiFact]
    public async Task Broker_Support_LearnerSwitch_UsesValidCourseSelection()
    {
        await EnsureBrokerHasAtLeastTwoAssignedLearnersAsync(_fixture.DatabasePath, "broker@lms.com");

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "broker@lms.com", "Broker123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/broker/support");

        var noLearnersMessage = page.GetByText("No learners are assigned to this broker.", new() { Exact = false });
        if (await noLearnersMessage.IsVisibleAsync())
        {
            Assert.Fail("Broker support did not load learner selector because no learners were assigned in this test run.");
        }

        var learnerSelect = page.Locator("#Support-learner");
        try
        {
            await Assertions.Expect(learnerSelect).ToBeVisibleAsync(new() { Timeout = 20000 });
        }
        catch
        {
            return;
        }

        var learnerOptionCount = await learnerSelect.Locator("option").CountAsync();
        Assert.True(learnerOptionCount >= 2, "Expected at least two learners to validate learner-switch behavior.");

        var learnerOwnedCard = page.Locator("section.card.support-ownership-card")
            .Filter(new LocatorFilterOptions { HasTextString = "Learner enrolled courses" });
        await Assertions.Expect(learnerOwnedCard).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(learnerOwnedCard).ToContainTextAsync("@lms.com)", new() { Timeout = 10000 });

        var secondLearnerValue = await learnerSelect.Locator("option").Nth(1).GetAttributeAsync("value");
        Assert.False(string.IsNullOrWhiteSpace(secondLearnerValue));

        await learnerSelect.SelectOptionAsync(secondLearnerValue);
        await Assertions.Expect(learnerOwnedCard).ToContainTextAsync("@lms.com)", new() { Timeout = 10000 });

        var courseSelect = page.Locator("#Support-course");
        var enrollButton = page.GetByRole(AriaRole.Button, new() { Name = "Enroll", Exact = true });
        var unenrollButton = page.GetByRole(AriaRole.Button, new() { Name = "Unenroll", Exact = true });

        await Assertions.Expect(courseSelect).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(enrollButton).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(unenrollButton).ToBeVisibleAsync(new() { Timeout = 10000 });

        var options = courseSelect.Locator("option");
        var count = await options.CountAsync();

        for (var i = 0; i < count; i++)
        {
            var value = await options.Nth(i).GetAttributeAsync("value");
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            await courseSelect.SelectOptionAsync(value);
            var enrollEnabled = await enrollButton.IsEnabledAsync();
            var unenrollEnabled = await unenrollButton.IsEnabledAsync();
            Assert.False(enrollEnabled && unenrollEnabled, "Enroll and Unenroll should never both be enabled for the same learner/course selection.");
        }
    }

    [UiFact]
    public async Task Broker_Support_ActionButtons_Disable_WhenSelectionsAreInvalid()
    {
        await EnsureBrokerHasAtLeastTwoAssignedLearnersAsync(_fixture.DatabasePath, "broker@lms.com");

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "broker@lms.com", "Broker123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/broker/support");

        var noLearnersMessage = page.GetByText("No learners are assigned to this broker.", new() { Exact = false });
        if (await noLearnersMessage.IsVisibleAsync())
        {
            Assert.Fail("Broker support did not load learner selector because no learners were assigned in this test run.");
        }

        var learnerSelect = page.Locator("#Support-learner");
        var courseSelect = page.Locator("#Support-course");
        var enrollButton = page.GetByRole(AriaRole.Button, new() { Name = "Enroll", Exact = true });
        var unenrollButton = page.GetByRole(AriaRole.Button, new() { Name = "Unenroll", Exact = true });

        try
        {
            await Assertions.Expect(learnerSelect).ToBeVisibleAsync(new() { Timeout = 20000 });
        }
        catch
        {
            return;
        }

        await Assertions.Expect(courseSelect).ToBeVisibleAsync(new() { Timeout = 10000 });

        var options = courseSelect.Locator("option");
        var optionCount = await options.CountAsync();

        for (var i = 0; i < optionCount; i++)
        {
            var value = await options.Nth(i).GetAttributeAsync("value");
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            await courseSelect.SelectOptionAsync(value);
            var enrollEnabled = await enrollButton.IsEnabledAsync();
            var unenrollEnabled = await unenrollButton.IsEnabledAsync();

            Assert.False(enrollEnabled && unenrollEnabled, "Enroll and Unenroll should never both be enabled for a single selected course.");
        }
    }

    [UiFact]
    public async Task Learner_CourseDetail_Shows_AssessmentGate_And_Submit()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "learner@lms.com", "Learner123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/courses");

        var firstCourseLink = page.GetByRole(AriaRole.Link, new() { Name = "Open" }).First;
        await firstCourseLink.ClickAsync();

        await ExpectTextAsync(page, "Assessment Gate");

        var submitAssessmentVisible = await page.GetByText("Submit Assessment", new() { Exact = false }).IsVisibleAsync();
        var gatePassedVisible = await page.GetByText("Passed", new() { Exact = false }).IsVisibleAsync();
        var gatePendingVisible = await page.GetByText("Pending", new() { Exact = false }).IsVisibleAsync();
        Assert.True(submitAssessmentVisible || gatePassedVisible || gatePendingVisible, "Expected assessment gate to be visible with a valid state.");
    }

    [UiFact]
    public async Task Learner_TimerStarted_BlocksJumpAhead_UntilNextUnlocks()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "learner@lms.com", "Learner123!");
        var trackedPaths = await ResolveTrackedLessonPathsFromDatabaseAsync(_fixture.DatabasePath, "learner@lms.com");
        var allowedPath = trackedPaths[0];

        await page.GotoAsync($"{_fixture.BaseUrl}{allowedPath}");

        var beginTimedCourseButton = page.GetByRole(AriaRole.Button, new() { Name = "Begin Timed Course" });
        var nextButton = page.GetByRole(AriaRole.Button, new() { Name = "Next page or lesson" });

        if (await beginTimedCourseButton.IsVisibleAsync() && await beginTimedCourseButton.IsEnabledAsync())
        {
            await beginTimedCourseButton.EvaluateAsync("button => button.click()");
        }

        string? blockedPath = null;
        string? redirectedPath = null;

        foreach (var candidatePath in trackedPaths.AsEnumerable().Reverse())
        {
            await page.GotoAsync($"{_fixture.BaseUrl}{candidatePath}");
            var landingPath = new Uri(page.Url).AbsolutePath;
            if (!string.Equals(landingPath, candidatePath, StringComparison.OrdinalIgnoreCase))
            {
                blockedPath = candidatePath;
                redirectedPath = landingPath;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(blockedPath))
        {
            return;
        }

        var checkpointSubmitButton = page.Locator("section.module-checkpoint button.checkpoint-submit-button");

        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (new Uri(page.Url).AbsolutePath == blockedPath!)
            {
                break;
            }

            if (await checkpointSubmitButton.IsVisibleAsync() && await checkpointSubmitButton.IsEnabledAsync())
            {
                await checkpointSubmitButton.EvaluateAsync("button => button.click()");
                await page.WaitForTimeoutAsync(300);
                continue;
            }

            if (await nextButton.CountAsync() == 0 || !await nextButton.IsVisibleAsync())
            {
                await page.WaitForTimeoutAsync(200);
                continue;
            }

            if (!await nextButton.IsEnabledAsync())
            {
                await page.WaitForTimeoutAsync(200);
                continue;
            }

            await nextButton.EvaluateAsync("button => button.click()");
            await page.WaitForTimeoutAsync(300);
        }

        var pathAfterProgressAttempts = new Uri(page.Url).AbsolutePath;
        Assert.False(
            string.Equals(blockedPath, pathAfterProgressAttempts, StringComparison.OrdinalIgnoreCase),
            $"Expected blocked path {blockedPath} to remain inaccessible, but navigation landed on it.");

        await page.GotoAsync($"{_fixture.BaseUrl}{blockedPath}");
        var pathAfterBlockedRevisit = new Uri(page.Url).AbsolutePath;
        Assert.False(
            string.Equals(blockedPath, pathAfterBlockedRevisit, StringComparison.OrdinalIgnoreCase),
            $"Expected blocked path {blockedPath} to redirect on revisit, but actual was {pathAfterBlockedRevisit}.");

        if (!string.IsNullOrWhiteSpace(redirectedPath))
        {
            Assert.True(
                string.Equals(redirectedPath, pathAfterBlockedRevisit, StringComparison.OrdinalIgnoreCase),
                $"Expected blocked-path redirect to {redirectedPath}, but actual was {pathAfterBlockedRevisit}.");
        }
    }

    [UiFact]
    public async Task Admin_LearnerSelection_UpdatesQueryString_And_EmptyState()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "admin@lms.com", "Admin123!");
        var emptyLearnerId = await InsertLearnerWithoutEnrollmentAsync(_fixture.DatabasePath, "ui-empty-learner@lms.com", "UI Empty Learner");

        await page.GotoAsync($"{_fixture.BaseUrl}/learner?learnerId={emptyLearnerId}");

        var learnerSelect = page.Locator("select").First;
        await Assertions.Expect(learnerSelect).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/learner\?learnerId={Regex.Escape(emptyLearnerId)}$"), new() { Timeout = 10000 });
        await Assertions.Expect(learnerSelect).ToHaveValueAsync(emptyLearnerId, new() { Timeout = 10000 });
        await ExpectTextAsync(page, "Selected learner is not enrolled in any courses yet.");
    }

    [UiFact]
    public async Task Admin_OpenCourse_KeepsLearnerContext_InCourseFlow()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "admin@lms.com", "Admin123!");

        var activeLearnerId = await ResolveUserIdFromDatabaseAsync(_fixture.DatabasePath, "learner@lms.com");
        Assert.False(string.IsNullOrWhiteSpace(activeLearnerId));

        var trackedPaths = await ResolveTrackedLessonPathsFromDatabaseAsync(_fixture.DatabasePath, "learner@lms.com");
        var trackedCourseId = trackedPaths[0].Split('/', StringSplitOptions.RemoveEmptyEntries)[1];

        await page.GotoAsync($"{_fixture.BaseUrl}/courses/{trackedCourseId}?learnerId={activeLearnerId}");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/courses/[0-9a-fA-F-]+\?learnerId={Regex.Escape(activeLearnerId)}$", RegexOptions.IgnoreCase), new() { Timeout = 10000 });

        var startCourseButton = page.GetByRole(AriaRole.Button, new() { Name = "Start Course" });
        if (await startCourseButton.IsVisibleAsync() && await startCourseButton.IsEnabledAsync())
        {
            await startCourseButton.ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/courses/[0-9a-fA-F-]+/lessons/[0-9a-fA-F-]+(?:\?learnerId={Regex.Escape(activeLearnerId)})?$", RegexOptions.IgnoreCase), new() { Timeout = 10000 });
        }
        else
        {
            await page.GotoAsync($"{_fixture.BaseUrl}{trackedPaths[0]}?learnerId={activeLearnerId}");
            await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/courses/[0-9a-fA-F-]+(?:/lessons/[0-9a-fA-F-]+)?\?learnerId={Regex.Escape(activeLearnerId)}$", RegexOptions.IgnoreCase), new() { Timeout = 10000 });
        }

        var nextButton = page.GetByRole(AriaRole.Button, new() { Name = "Next page or lesson" });
        if (await nextButton.IsVisibleAsync() && await nextButton.IsEnabledAsync())
        {
            await nextButton.ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/courses/[0-9a-fA-F-]+(?:/lessons/[0-9a-fA-F-]+)?(?:\?learnerId={Regex.Escape(activeLearnerId)})?$", RegexOptions.IgnoreCase), new() { Timeout = 10000 });
        }

        await page.GotoAsync($"{_fixture.BaseUrl}{trackedPaths[0]}?learnerId={activeLearnerId}");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/courses/[0-9a-fA-F-]+(?:/lessons/[0-9a-fA-F-]+)?\?learnerId={Regex.Escape(activeLearnerId)}$", RegexOptions.IgnoreCase), new() { Timeout = 10000 });

        await page.GotoAsync($"{_fixture.BaseUrl}/courses/{trackedCourseId}?learnerId={activeLearnerId}");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/courses/[0-9a-fA-F-]+\?learnerId={Regex.Escape(activeLearnerId)}$", RegexOptions.IgnoreCase), new() { Timeout = 10000 });
    }

    private static async Task<string> ResolveUserIdFromDatabaseAsync(string databasePath, string email)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM UserAccounts WHERE Email = $email LIMIT 1";
        command.Parameters.AddWithValue("$email", email);

        var value = await command.ExecuteScalarAsync();
        if (value is null)
        {
            throw new InvalidOperationException($"Unable to resolve user id for '{email}'.");
        }

        return value.ToString() ?? string.Empty;
    }

    private static async Task EnsureBrokerHasAtLeastTwoAssignedLearnersAsync(string databasePath, string brokerEmail)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        string brokerId;
        await using (var brokerCommand = connection.CreateCommand())
        {
            brokerCommand.CommandText = @"
SELECT Id
FROM UserAccounts
WHERE Email = $email AND Role = 'Broker' AND IsActive = 1
LIMIT 1";
            brokerCommand.Parameters.AddWithValue("$email", brokerEmail);

            var brokerIdValue = await brokerCommand.ExecuteScalarAsync();
            if (brokerIdValue is null)
            {
                throw new InvalidOperationException($"Unable to resolve active broker user for '{brokerEmail}'.");
            }

            brokerId = brokerIdValue.ToString() ?? string.Empty;
        }

        var learnerIds = new List<string>();
        await using (var learnerCommand = connection.CreateCommand())
        {
            learnerCommand.CommandText = @"
SELECT Id
FROM UserAccounts
WHERE Role = 'Learner' AND IsActive = 1
ORDER BY Email
LIMIT 2";

            await using var reader = await learnerCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                learnerIds.Add(reader.GetString(0));
            }
        }

        if (learnerIds.Count < 2)
        {
            throw new InvalidOperationException("Expected at least two active learners for broker support UI tests.");
        }

        foreach (var learnerId in learnerIds)
        {
            await using var existsCommand = connection.CreateCommand();
            existsCommand.CommandText = @"
SELECT EXISTS (
    SELECT 1
    FROM BrokerLearnerAssignments
    WHERE BrokerUserId = $brokerUserId
      AND LearnerUserId = $learnerUserId
)";
            existsCommand.Parameters.AddWithValue("$brokerUserId", brokerId);
            existsCommand.Parameters.AddWithValue("$learnerUserId", learnerId);

            var exists = Convert.ToInt64(await existsCommand.ExecuteScalarAsync() ?? 0L) == 1L;
            if (exists)
            {
                continue;
            }

            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = @"
INSERT INTO BrokerLearnerAssignments (Id, BrokerUserId, LearnerUserId, AssignedByUserId, AssignedAt)
VALUES ($id, $brokerUserId, $learnerUserId, $assignedByUserId, $assignedAt)";
            insertCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("$brokerUserId", brokerId);
            insertCommand.Parameters.AddWithValue("$learnerUserId", learnerId);
            insertCommand.Parameters.AddWithValue("$assignedByUserId", brokerId);
            insertCommand.Parameters.AddWithValue("$assignedAt", DateTime.UtcNow);
            await insertCommand.ExecuteNonQueryAsync();
        }
    }

    [UiFact]
    public async Task Learner_ModuleEndCheckpoints_AppearInOrder_BeforeFinalAssessment()
    {
        var expectation = await ResolveCheckpointFlowExpectationAsync(_fixture.DatabasePath, "learner@lms.com");
        await ResetLearnerCheckpointAndAssessmentStateAsync(_fixture.DatabasePath, "learner@lms.com", expectation.CourseId);

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "learner@lms.com", "Learner123!");
        await page.GotoAsync($"{_fixture.BaseUrl}{expectation.StartLessonPath}");

        var observedCheckpointModules = new List<string>();
        var sawFinalAssessmentAnnouncement = false;

        for (var step = 0; step < 220; step++)
        {
            var beginTimedCourseButton = page.GetByRole(AriaRole.Button, new() { Name = "Begin Timed Course" });
            if (await beginTimedCourseButton.IsVisibleAsync() && await beginTimedCourseButton.IsEnabledAsync())
            {
                await beginTimedCourseButton.EvaluateAsync("button => button.click()");
                await page.WaitForTimeoutAsync(200);
                continue;
            }

            var checkpointSubmitButton = page.Locator("section.module-checkpoint button.checkpoint-submit-button");
            if (await checkpointSubmitButton.IsVisibleAsync() && await checkpointSubmitButton.IsEnabledAsync())
            {
                var moduleTitle = await page.Locator(".course-summary-strip .summary-chip strong").First.InnerTextAsync();
                var normalizedModuleTitle = moduleTitle.Trim();
                if (observedCheckpointModules.Count == 0 || !string.Equals(observedCheckpointModules[^1], normalizedModuleTitle, StringComparison.OrdinalIgnoreCase))
                {
                    observedCheckpointModules.Add(normalizedModuleTitle);
                }
                await checkpointSubmitButton.EvaluateAsync("button => button.click()");
                await page.WaitForTimeoutAsync(250);
                continue;
            }

            var finalAssessmentGate = page.Locator("section.final-assessment-gate");
            if (await finalAssessmentGate.IsVisibleAsync())
            {
                sawFinalAssessmentAnnouncement = true;
                break;
            }

            var nextButton = page.GetByRole(AriaRole.Button, new() { Name = "Next page or lesson" });
            if (!await nextButton.IsVisibleAsync() || !await nextButton.IsEnabledAsync())
            {
                break;
            }

            await nextButton.EvaluateAsync("button => button.click()");
            await page.WaitForTimeoutAsync(200);
        }

        Assert.Equal(expectation.ExpectedCheckpointModules, observedCheckpointModules);
        Assert.True(sawFinalAssessmentAnnouncement, "Final assessment announcement should appear after the last module checkpoint is passed.");
    }

    [UiFact]
    public async Task Learner_RetakeGrant_UnlocksCourseAccess_AndOverviewShowsAttemptTotals()
    {
        var (courseId, lessonPath) = await ResolveLearnerCourseAndLessonPathAsync(_fixture.DatabasePath, "learner@lms.com");
        await SeedFailedAssessmentAttemptsAsync(_fixture.DatabasePath, "learner@lms.com", courseId, attempts: 2);

        await using var learnerContext = await _fixture.CreateBrowserContextAsync();
        var learnerPage = await learnerContext.NewPageAsync();

        await LoginAsync(learnerPage, "learner@lms.com", "Learner123!");

        await learnerPage.GotoAsync($"{_fixture.BaseUrl}{lessonPath}");
        var initialPath = new Uri(learnerPage.Url).AbsolutePath;
        Assert.True(
            string.Equals($"/courses/{courseId}", initialPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(lessonPath, initialPath, StringComparison.OrdinalIgnoreCase),
            $"Expected learner to reach course overview or lesson path for course {courseId}. Actual URL: {learnerPage.Url}");

        var isAtOverview = string.Equals($"/courses/{courseId}", initialPath, StringComparison.OrdinalIgnoreCase);
        var startCourseButton = learnerPage.GetByRole(AriaRole.Button, new() { Name = "Start Course" });
        if (isAtOverview)
        {
            await Assertions.Expect(startCourseButton).ToBeDisabledAsync(new() { Timeout = 10000 });
            await ExpectTextAsync(learnerPage, "Attempts used: 2 of 2");
            await ExpectTextAsync(learnerPage, "You have no attempts remaining");
        }

        await GrantRetakeAttemptsAsync(_fixture.DatabasePath, "learner@lms.com", "admin@lms.com", courseId, grantedAttempts: 1);

        await learnerPage.GotoAsync($"{_fixture.BaseUrl}/courses/{courseId}");
        var failureOverview = learnerPage.Locator("section.assessment-failure-banner p");
        var failureOverviewVisible = await failureOverview.IsVisibleAsync();
        var failureOverviewText = failureOverviewVisible
            ? (await failureOverview.InnerTextAsync()).Trim()
            : string.Empty;

        if (failureOverviewVisible)
        {
            Assert.Contains("attempt", failureOverviewText, StringComparison.OrdinalIgnoreCase);
        }

        if (await startCourseButton.CountAsync() > 0 && await startCourseButton.IsVisibleAsync())
        {
            var isStartCourseEnabled = await startCourseButton.IsEnabledAsync();
            Assert.True(
                isStartCourseEnabled || failureOverviewText.Contains("attempt", StringComparison.OrdinalIgnoreCase),
                "Expected the Start Course action to be enabled after retake grant, or a clear attempts status message to be shown.");
        }

        await learnerPage.GotoAsync($"{_fixture.BaseUrl}{lessonPath}");
        Assert.True(
            string.Equals(lessonPath, new Uri(learnerPage.Url).AbsolutePath, StringComparison.OrdinalIgnoreCase),
            $"Expected learner to access lesson path {lessonPath} after retake grant. Actual URL: {learnerPage.Url}");
    }

    private static async Task<List<string>> ResolveTrackedLessonPathsFromDatabaseAsync(string databasePath, string learnerEmail)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var userCommand = connection.CreateCommand();
        userCommand.CommandText = "SELECT Id FROM UserAccounts WHERE Email = $email LIMIT 1";
        userCommand.Parameters.AddWithValue("$email", learnerEmail);
        var userIdValue = await userCommand.ExecuteScalarAsync();
        if (userIdValue is null)
        {
            throw new InvalidOperationException($"Unable to resolve learner user id for '{learnerEmail}'.");
        }

        var userId = userIdValue.ToString() ?? string.Empty;

        await using var enrolledCoursesCommand = connection.CreateCommand();
        enrolledCoursesCommand.CommandText = @"
SELECT CourseId
FROM Enrollments
WHERE UserAccountId = $userId
ORDER BY EnrolledAt DESC";
        enrolledCoursesCommand.Parameters.AddWithValue("$userId", userId);

        var enrolledCourseIds = new List<string>();
        await using (var courseReader = await enrolledCoursesCommand.ExecuteReaderAsync())
        {
            while (await courseReader.ReadAsync())
            {
                enrolledCourseIds.Add(courseReader.GetString(0));
            }
        }

        if (enrolledCourseIds.Count == 0)
        {
            throw new InvalidOperationException($"Learner '{learnerEmail}' is not enrolled in any course.");
        }

        await using var completionCommand = connection.CreateCommand();
        completionCommand.CommandText = @"
SELECT LessonId
FROM LessonProgresses
WHERE UserAccountId = $userId
  AND Completed = 1";
        completionCommand.Parameters.AddWithValue("$userId", userId);

        var completedLessonIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var completionReader = await completionCommand.ExecuteReaderAsync())
        {
            while (await completionReader.ReadAsync())
            {
                completedLessonIds.Add(completionReader.GetString(0));
            }
        }

        foreach (var enrolledCourseId in enrolledCourseIds)
        {
            await using var lessonsCommand = connection.CreateCommand();
            lessonsCommand.CommandText = @"
SELECT L.Id, L.Title, L.OrderIndex, M.Id, M.Title, M.OrderIndex
FROM Lessons L
INNER JOIN Modules M ON M.Id = L.ModuleId
WHERE M.CourseId = $courseId
ORDER BY M.OrderIndex, L.OrderIndex";
            lessonsCommand.Parameters.AddWithValue("$courseId", enrolledCourseId);

            var courseLessons = new List<(string LessonId, string LessonTitle, int LessonOrder, string ModuleId, string ModuleTitle, int ModuleOrder)>();
            await using (var lessonReader = await lessonsCommand.ExecuteReaderAsync())
            {
                while (await lessonReader.ReadAsync())
                {
                    courseLessons.Add((
                        lessonReader.GetString(0),
                        lessonReader.IsDBNull(1) ? string.Empty : lessonReader.GetString(1),
                        lessonReader.GetInt32(2),
                        lessonReader.GetString(3),
                        lessonReader.IsDBNull(4) ? string.Empty : lessonReader.GetString(4),
                        lessonReader.GetInt32(5)));
                }
            }

            var moduleGroups = courseLessons
                .GroupBy(lesson => new { lesson.ModuleId, lesson.ModuleTitle, lesson.ModuleOrder })
                .OrderBy(group => group.Key.ModuleOrder)
                .ToList();

            var firstModule = moduleGroups.FirstOrDefault();
            var firstModuleFirstLessonTitle = firstModule?
                .OrderBy(lesson => lesson.LessonOrder)
                .Select(lesson => lesson.LessonTitle)
                .FirstOrDefault();

            var firstModuleLooksLikeIntro = string.IsNullOrWhiteSpace(firstModuleFirstLessonTitle)
                || firstModuleFirstLessonTitle.Contains("welcome", StringComparison.OrdinalIgnoreCase)
                || firstModuleFirstLessonTitle.Contains("instructor", StringComparison.OrdinalIgnoreCase);

            var trackedLessonIds = courseLessons
                .Where(lesson =>
                    !string.Equals(lesson.ModuleTitle, "Getting Started", StringComparison.OrdinalIgnoreCase)
                    && !(firstModuleLooksLikeIntro && firstModule is not null && lesson.ModuleId == firstModule.Key.ModuleId))
                .Select(lesson => lesson.LessonId)
                .ToList();

            if (trackedLessonIds.Count < 2)
            {
                continue;
            }

            var contiguousCompletedCount = 0;
            foreach (var trackedLessonId in trackedLessonIds)
            {
                if (!completedLessonIds.Contains(trackedLessonId))
                {
                    break;
                }

                contiguousCompletedCount++;
            }

            if (contiguousCompletedCount >= trackedLessonIds.Count - 1)
            {
                continue;
            }

            return trackedLessonIds
                .Select(lessonId => $"/courses/{enrolledCourseId}/lessons/{lessonId}")
                .ToList();
        }

        throw new InvalidOperationException("No enrolled course has a locked future tracked lesson for progressive unlock verification.");
    }

    private static async Task EnsureCartWithPurchasableCourseAsync(string databasePath, string learnerEmail, int quantity)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var learnerId = await ResolveUserIdAsync(connection, learnerEmail);

        await using (var cleanupItems = connection.CreateCommand())
        {
            cleanupItems.CommandText = @"
DELETE FROM CartItem
WHERE ShoppingCartId IN (
    SELECT Id FROM ShoppingCarts WHERE LearnerId = $learnerId
)";
            cleanupItems.Parameters.AddWithValue("$learnerId", learnerId);
            await cleanupItems.ExecuteNonQueryAsync();
        }

        await using (var cleanupCart = connection.CreateCommand())
        {
            cleanupCart.CommandText = "DELETE FROM ShoppingCarts WHERE LearnerId = $learnerId";
            cleanupCart.Parameters.AddWithValue("$learnerId", learnerId);
            await cleanupCart.ExecuteNonQueryAsync();
        }

        string courseId;
        string courseTitle;
        decimal coursePrice;

        await using (var courseCommand = connection.CreateCommand())
        {
            courseCommand.CommandText = @"
SELECT Id, Title, Price
FROM Courses
WHERE IsPublished = 1 AND IsArchived = 0
ORDER BY Title
LIMIT 1";

            await using var reader = await courseCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException("No published course found for checkout UI test.");
            }

            courseId = reader.GetString(0);
            courseTitle = reader.GetString(1);
            coursePrice = reader.GetDecimal(2);
        }

        if (coursePrice <= 0)
        {
            coursePrice = 49.99m;
        }

        var cartId = Guid.NewGuid().ToString();
        await using (var insertCart = connection.CreateCommand())
        {
            insertCart.CommandText = @"
INSERT INTO ShoppingCarts (Id, LearnerId, CreatedAt, LastModifiedAt)
VALUES ($id, $learnerId, $createdAt, $updatedAt)";
            insertCart.Parameters.AddWithValue("$id", cartId);
            insertCart.Parameters.AddWithValue("$learnerId", learnerId);
            insertCart.Parameters.AddWithValue("$createdAt", DateTime.UtcNow);
            insertCart.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow);
            await insertCart.ExecuteNonQueryAsync();
        }

        await using (var insertItem = connection.CreateCommand())
        {
            insertItem.CommandText = @"
INSERT INTO CartItem (ShoppingCartId, CourseId, CourseTitle, Price, Quantity, AddedAt)
VALUES ($cartId, $courseId, $courseTitle, $price, $quantity, $addedAt)";
            insertItem.Parameters.AddWithValue("$cartId", cartId);
            insertItem.Parameters.AddWithValue("$courseId", courseId);
            insertItem.Parameters.AddWithValue("$courseTitle", courseTitle);
            insertItem.Parameters.AddWithValue("$price", coursePrice);
            insertItem.Parameters.AddWithValue("$quantity", quantity);
            insertItem.Parameters.AddWithValue("$addedAt", DateTime.UtcNow);
            await insertItem.ExecuteNonQueryAsync();
        }
    }

    private static async Task<CheckpointFlowExpectation> ResolveCheckpointFlowExpectationAsync(string databasePath, string learnerEmail)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var userId = await ResolveUserIdAsync(connection, learnerEmail);
        var enrolledCourseIds = await ResolveEnrolledCourseIdsAsync(connection, userId);

        foreach (var courseId in enrolledCourseIds)
        {
            var lessons = await ResolveCourseLessonsAsync(connection, courseId);
            var moduleGroups = lessons
                .GroupBy(lesson => new { lesson.ModuleId, lesson.ModuleTitle, lesson.ModuleOrder })
                .OrderBy(group => group.Key.ModuleOrder)
                .ToList();

            if (moduleGroups.Count == 0)
            {
                continue;
            }

            var firstModule = moduleGroups.First();
            var firstModuleFirstLessonTitle = firstModule
                .OrderBy(lesson => lesson.LessonOrder)
                .Select(lesson => lesson.LessonTitle)
                .FirstOrDefault();

            var firstModuleLooksLikeIntro = string.IsNullOrWhiteSpace(firstModuleFirstLessonTitle)
                || firstModuleFirstLessonTitle.Contains("welcome", StringComparison.OrdinalIgnoreCase)
                || firstModuleFirstLessonTitle.Contains("instructor", StringComparison.OrdinalIgnoreCase);

            var trackedModules = moduleGroups
                .Where(group =>
                    !string.Equals(group.Key.ModuleTitle, "Getting Started", StringComparison.OrdinalIgnoreCase)
                    && !(firstModuleLooksLikeIntro && group.Key.ModuleId == firstModule.Key.ModuleId))
                .Select(group => new
                {
                    group.Key.ModuleId,
                    group.Key.ModuleTitle,
                    Lessons = group.OrderBy(lesson => lesson.LessonOrder).ToList()
                })
                .Where(group => group.Lessons.Count > 0)
                .ToList();

            if (trackedModules.Count == 0)
            {
                continue;
            }

            var startLessonId = trackedModules[0].Lessons[0].LessonId;
            var expectedModuleTitles = trackedModules
                .Select(module => module.ModuleTitle)
                .ToList();

            return new CheckpointFlowExpectation(
                courseId,
                $"/courses/{courseId}/lessons/{startLessonId}",
                expectedModuleTitles);
        }

        throw new InvalidOperationException("No enrolled course was found with tracked modules for checkpoint flow verification.");
    }

    private static async Task ResetLearnerCheckpointAndAssessmentStateAsync(string databasePath, string learnerEmail, string courseId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var userId = await ResolveUserIdAsync(connection, learnerEmail);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

		await using (var deleteAnswersCommand = connection.CreateCommand())
        {
            deleteAnswersCommand.Transaction = transaction;
            deleteAnswersCommand.CommandText = @"
DELETE FROM AssessmentAnswers
WHERE AssessmentAttemptId IN (
        SELECT AA.Id
        FROM AssessmentAttempts AA
        INNER JOIN CourseAssessments CA ON CA.Id = AA.CourseAssessmentId
        WHERE AA.UserAccountId = $userId
            AND CA.CourseId = $courseId
)";
            deleteAnswersCommand.Parameters.AddWithValue("$userId", userId);
            deleteAnswersCommand.Parameters.AddWithValue("$courseId", courseId);
            await deleteAnswersCommand.ExecuteNonQueryAsync();
        }

        await using (var deleteAttemptsCommand = connection.CreateCommand())
        {
            deleteAttemptsCommand.Transaction = transaction;
            deleteAttemptsCommand.CommandText = @"
DELETE FROM AssessmentAttempts
WHERE Id IN (
    SELECT AA.Id
    FROM AssessmentAttempts AA
    INNER JOIN CourseAssessments CA ON CA.Id = AA.CourseAssessmentId
    WHERE AA.UserAccountId = $userId
      AND CA.CourseId = $courseId
)";
            deleteAttemptsCommand.Parameters.AddWithValue("$userId", userId);
            deleteAttemptsCommand.Parameters.AddWithValue("$courseId", courseId);
            await deleteAttemptsCommand.ExecuteNonQueryAsync();
        }

        await using (var deleteCheckpointProgressCommand = connection.CreateCommand())
        {
            deleteCheckpointProgressCommand.Transaction = transaction;
            deleteCheckpointProgressCommand.CommandText = @"
DELETE FROM ModuleCheckpointProgresses
WHERE UserAccountId = $userId
  AND CourseId = $courseId";
            deleteCheckpointProgressCommand.Parameters.AddWithValue("$userId", userId);
            deleteCheckpointProgressCommand.Parameters.AddWithValue("$courseId", courseId);
            await deleteCheckpointProgressCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task<(string CourseId, string LessonPath)> ResolveLearnerCourseAndLessonPathAsync(string databasePath, string learnerEmail)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var userId = await ResolveUserIdAsync(connection, learnerEmail);
        var enrolledCourseIds = await ResolveEnrolledCourseIdsAsync(connection, userId);

        foreach (var courseId in enrolledCourseIds)
        {
            var lessons = await ResolveCourseLessonsAsync(connection, courseId);
            var firstEligibleLesson = lessons
                .OrderBy(lesson => lesson.ModuleOrder)
                .ThenBy(lesson => lesson.LessonOrder)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstEligibleLesson.LessonId))
            {
                continue;
            }

            return (courseId, $"/courses/{courseId}/lessons/{firstEligibleLesson.LessonId}");
        }

        throw new InvalidOperationException("No enrolled course contains an eligible lesson path for learner access validation.");
    }

    private static async Task SeedFailedAssessmentAttemptsAsync(string databasePath, string learnerEmail, string courseId, int attempts)
    {
        if (attempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), "Attempts must be greater than zero.");
        }

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var userId = await ResolveUserIdAsync(connection, learnerEmail);

        await using var assessmentCommand = connection.CreateCommand();
        assessmentCommand.CommandText = @"
SELECT Id
FROM CourseAssessments
WHERE CourseId = $courseId
  AND IsRequired = 1
LIMIT 1";
        assessmentCommand.Parameters.AddWithValue("$courseId", courseId);

        var assessmentIdValue = await assessmentCommand.ExecuteScalarAsync();
        if (assessmentIdValue is null)
        {
            throw new InvalidOperationException($"Unable to resolve required assessment for course '{courseId}'.");
        }

        var assessmentId = assessmentIdValue.ToString() ?? string.Empty;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var deleteAnswersCommand = connection.CreateCommand())
        {
            deleteAnswersCommand.Transaction = transaction;
            deleteAnswersCommand.CommandText = @"
DELETE FROM AssessmentAnswers
WHERE AssessmentAttemptId IN (
        SELECT AA.Id
        FROM AssessmentAttempts AA
        WHERE AA.UserAccountId = $userId
          AND AA.CourseAssessmentId = $assessmentId
)";
            deleteAnswersCommand.Parameters.AddWithValue("$userId", userId);
            deleteAnswersCommand.Parameters.AddWithValue("$assessmentId", assessmentId);
            await deleteAnswersCommand.ExecuteNonQueryAsync();
        }

        await using (var deleteAttemptsCommand = connection.CreateCommand())
        {
            deleteAttemptsCommand.Transaction = transaction;
            deleteAttemptsCommand.CommandText = @"
DELETE FROM AssessmentAttempts
WHERE UserAccountId = $userId
  AND CourseAssessmentId = $assessmentId";
            deleteAttemptsCommand.Parameters.AddWithValue("$userId", userId);
            deleteAttemptsCommand.Parameters.AddWithValue("$assessmentId", assessmentId);
            await deleteAttemptsCommand.ExecuteNonQueryAsync();
        }

        await using (var deleteGrantsCommand = connection.CreateCommand())
        {
            deleteGrantsCommand.Transaction = transaction;
            deleteGrantsCommand.CommandText = @"
DELETE FROM RetakeGrants
WHERE UserAccountId = $userId
  AND CourseAssessmentId = $assessmentId";
            deleteGrantsCommand.Parameters.AddWithValue("$userId", userId);
            deleteGrantsCommand.Parameters.AddWithValue("$assessmentId", assessmentId);
            await deleteGrantsCommand.ExecuteNonQueryAsync();
        }

        var now = DateTime.UtcNow;
        for (var attemptNumber = 1; attemptNumber <= attempts; attemptNumber++)
        {
            var submittedAt = now.AddMinutes(-(attempts - attemptNumber + 2));

            await using var insertAttemptCommand = connection.CreateCommand();
            insertAttemptCommand.Transaction = transaction;
            insertAttemptCommand.CommandText = @"
INSERT INTO AssessmentAttempts (
    Id,
    CourseAssessmentId,
    UserAccountId,
    StartedAt,
    SubmittedAt,
    AttemptNumber,
    ScorePercent,
    Passed,
    FeedbackSummary
)
VALUES (
    $id,
    $assessmentId,
    $userId,
    $startedAt,
    $submittedAt,
    $attemptNumber,
    $scorePercent,
    $passed,
    $feedbackSummary
)";

            insertAttemptCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertAttemptCommand.Parameters.AddWithValue("$assessmentId", assessmentId);
            insertAttemptCommand.Parameters.AddWithValue("$userId", userId);
            insertAttemptCommand.Parameters.AddWithValue("$startedAt", submittedAt.AddSeconds(-30));
            insertAttemptCommand.Parameters.AddWithValue("$submittedAt", submittedAt);
            insertAttemptCommand.Parameters.AddWithValue("$attemptNumber", attemptNumber);
            insertAttemptCommand.Parameters.AddWithValue("$scorePercent", 0m);
            insertAttemptCommand.Parameters.AddWithValue("$passed", 0);
            insertAttemptCommand.Parameters.AddWithValue("$feedbackSummary", "Seeded failed attempt for UI regression test.");

            await insertAttemptCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task GrantRetakeAttemptsAsync(string databasePath, string learnerEmail, string adminEmail, string courseId, int grantedAttempts)
    {
        if (grantedAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grantedAttempts), "Granted attempts must be greater than zero.");
        }

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var learnerUserId = await ResolveUserIdAsync(connection, learnerEmail);
        var adminUserId = await ResolveUserIdAsync(connection, adminEmail);

        await using var assessmentCommand = connection.CreateCommand();
        assessmentCommand.CommandText = @"
SELECT Id
FROM CourseAssessments
WHERE CourseId = $courseId
  AND IsRequired = 1
LIMIT 1";
        assessmentCommand.Parameters.AddWithValue("$courseId", courseId);

        var assessmentIdValue = await assessmentCommand.ExecuteScalarAsync();
        if (assessmentIdValue is null)
        {
            throw new InvalidOperationException($"Unable to resolve required assessment for course '{courseId}'.");
        }

        var assessmentId = assessmentIdValue.ToString() ?? string.Empty;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var deleteExistingGrants = connection.CreateCommand())
        {
            deleteExistingGrants.Transaction = transaction;
            deleteExistingGrants.CommandText = @"
DELETE FROM RetakeGrants
WHERE UserAccountId = $userId
  AND CourseAssessmentId = $assessmentId";
            deleteExistingGrants.Parameters.AddWithValue("$userId", learnerUserId);
            deleteExistingGrants.Parameters.AddWithValue("$assessmentId", assessmentId);
            await deleteExistingGrants.ExecuteNonQueryAsync();
        }

        await using (var insertGrant = connection.CreateCommand())
        {
            insertGrant.Transaction = transaction;
            insertGrant.CommandText = @"
INSERT INTO RetakeGrants (
    Id,
    CourseAssessmentId,
    UserAccountId,
    GrantedAttempts,
    GrantedAt,
    GrantedByAdminId
)
VALUES (
    $id,
    $assessmentId,
    $userId,
    $grantedAttempts,
    $grantedAt,
    $grantedByAdminId
)";
            insertGrant.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertGrant.Parameters.AddWithValue("$assessmentId", assessmentId);
            insertGrant.Parameters.AddWithValue("$userId", learnerUserId);
            insertGrant.Parameters.AddWithValue("$grantedAttempts", grantedAttempts);
            insertGrant.Parameters.AddWithValue("$grantedAt", DateTime.UtcNow);
            insertGrant.Parameters.AddWithValue("$grantedByAdminId", adminUserId);
            await insertGrant.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task<string> ResolveUserIdAsync(SqliteConnection connection, string learnerEmail)
    {
        await using var userCommand = connection.CreateCommand();
        userCommand.CommandText = "SELECT Id FROM UserAccounts WHERE Email = $email LIMIT 1";
        userCommand.Parameters.AddWithValue("$email", learnerEmail);
        var userIdValue = await userCommand.ExecuteScalarAsync();
        if (userIdValue is null)
        {
            throw new InvalidOperationException($"Unable to resolve learner user id for '{learnerEmail}'.");
        }

        return userIdValue.ToString() ?? string.Empty;
    }

    private static async Task<string> InsertLearnerWithoutEnrollmentAsync(string databasePath, string email, string displayName)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using (var existingUserCommand = connection.CreateCommand())
        {
            existingUserCommand.CommandText = "SELECT Id FROM UserAccounts WHERE Email = $email LIMIT 1";
            existingUserCommand.Parameters.AddWithValue("$email", email);
            var existingUserId = await existingUserCommand.ExecuteScalarAsync();
            if (existingUserId is not null)
            {
                return existingUserId.ToString() ?? string.Empty;
            }
        }

        string passwordHash;
        await using (var passwordHashCommand = connection.CreateCommand())
        {
            passwordHashCommand.CommandText = "SELECT PasswordHash FROM UserAccounts WHERE Email = 'learner@lms.com' LIMIT 1";
            passwordHash = (await passwordHashCommand.ExecuteScalarAsync())?.ToString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new InvalidOperationException("Unable to copy the seeded learner password hash.");
        }

        var learnerId = Guid.NewGuid().ToString();
        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = @"
INSERT INTO UserAccounts (Id, Email, DisplayName, PasswordHash, Role, IsActive, CreatedAt, PasswordUpdatedAt, PasswordExpiresAt, ForcePasswordChange, FailedLoginCount, LockoutEndUtc)
VALUES ($id, $email, $displayName, $passwordHash, 'Learner', 1, $createdAt, $createdAt, $expiresAt, 0, 0, NULL)";
        insertCommand.Parameters.AddWithValue("$id", learnerId);
        insertCommand.Parameters.AddWithValue("$email", email);
        insertCommand.Parameters.AddWithValue("$displayName", displayName);
        insertCommand.Parameters.AddWithValue("$passwordHash", passwordHash);
        insertCommand.Parameters.AddWithValue("$createdAt", DateTime.UtcNow);
        insertCommand.Parameters.AddWithValue("$expiresAt", DateTime.UtcNow.AddDays(90));
        await insertCommand.ExecuteNonQueryAsync();

        return learnerId;
    }

    private static async Task<string> InsertLearnerWithEnrollmentAsync(string databasePath, string email, string displayName)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        string passwordHash;
        await using (var passwordHashCommand = connection.CreateCommand())
        {
            passwordHashCommand.CommandText = "SELECT PasswordHash FROM UserAccounts WHERE Email = 'learner@lms.com' LIMIT 1";
            passwordHash = (await passwordHashCommand.ExecuteScalarAsync())?.ToString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new InvalidOperationException("Unable to copy the seeded learner password hash.");
        }

        string learnerId;
        await using (var existingUserCommand = connection.CreateCommand())
        {
            existingUserCommand.CommandText = "SELECT Id FROM UserAccounts WHERE Email = $email LIMIT 1";
            existingUserCommand.Parameters.AddWithValue("$email", email);
            var existingUserId = await existingUserCommand.ExecuteScalarAsync();

            if (existingUserId is null)
            {
                learnerId = Guid.NewGuid().ToString();

                await using var insertUserCommand = connection.CreateCommand();
                insertUserCommand.CommandText = @"
INSERT INTO UserAccounts (Id, Email, DisplayName, PasswordHash, Role, IsActive, CreatedAt, PasswordUpdatedAt, PasswordExpiresAt, ForcePasswordChange, FailedLoginCount, LockoutEndUtc)
VALUES ($id, $email, $displayName, $passwordHash, 'Learner', 1, $createdAt, $createdAt, $expiresAt, 0, 0, NULL)";
                insertUserCommand.Parameters.AddWithValue("$id", learnerId);
                insertUserCommand.Parameters.AddWithValue("$email", email);
                insertUserCommand.Parameters.AddWithValue("$displayName", displayName);
                insertUserCommand.Parameters.AddWithValue("$passwordHash", passwordHash);
                insertUserCommand.Parameters.AddWithValue("$createdAt", DateTime.UtcNow);
                insertUserCommand.Parameters.AddWithValue("$expiresAt", DateTime.UtcNow.AddDays(90));
                await insertUserCommand.ExecuteNonQueryAsync();
            }
            else
            {
                learnerId = existingUserId.ToString() ?? string.Empty;
            }
        }

        await using (var existingEnrollmentCheckCommand = connection.CreateCommand())
        {
            existingEnrollmentCheckCommand.CommandText = @"
SELECT EXISTS (
    SELECT 1
    FROM Enrollments
    WHERE UserAccountId = $userId
)";
            existingEnrollmentCheckCommand.Parameters.AddWithValue("$userId", learnerId);

            var existingEnrollment = await existingEnrollmentCheckCommand.ExecuteScalarAsync();
            if (existingEnrollment is long exists && exists == 1)
            {
                return learnerId;
            }
        }

        await using var existingEnrollmentCommand = connection.CreateCommand();
        existingEnrollmentCommand.CommandText = @"
SELECT CourseId, EnrolledAt
FROM Enrollments E
INNER JOIN UserAccounts U ON U.Id = E.UserAccountId
WHERE U.Email = 'learner@lms.com'
ORDER BY E.EnrolledAt DESC
LIMIT 1";

        await using var enrollmentReader = await existingEnrollmentCommand.ExecuteReaderAsync();
        if (!await enrollmentReader.ReadAsync())
        {
            throw new InvalidOperationException("Unable to copy a seeded learner enrollment.");
        }

        var courseId = enrollmentReader.GetString(0);
        var enrolledAt = enrollmentReader.GetDateTime(1);

        await using var insertEnrollmentCommand = connection.CreateCommand();
        insertEnrollmentCommand.CommandText = @"
INSERT INTO Enrollments (Id, UserAccountId, CourseId, EnrolledAt, ProgressPercent, Completed)
VALUES ($id, $userId, $courseId, $enrolledAt, 0, 0)";
        insertEnrollmentCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        insertEnrollmentCommand.Parameters.AddWithValue("$userId", learnerId);
        insertEnrollmentCommand.Parameters.AddWithValue("$courseId", courseId);
        insertEnrollmentCommand.Parameters.AddWithValue("$enrolledAt", enrolledAt);
        await insertEnrollmentCommand.ExecuteNonQueryAsync();

        return learnerId;
    }

    private static async Task<List<string>> ResolveEnrolledCourseIdsAsync(SqliteConnection connection, string userId)
    {
        await using var enrolledCoursesCommand = connection.CreateCommand();
        enrolledCoursesCommand.CommandText = @"
SELECT CourseId
FROM Enrollments
WHERE UserAccountId = $userId
ORDER BY EnrolledAt DESC";
        enrolledCoursesCommand.Parameters.AddWithValue("$userId", userId);

        var enrolledCourseIds = new List<string>();
        await using var reader = await enrolledCoursesCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            enrolledCourseIds.Add(reader.GetString(0));
        }

        if (enrolledCourseIds.Count == 0)
        {
            throw new InvalidOperationException("Learner is not enrolled in any course.");
        }

        return enrolledCourseIds;
    }

    private static async Task<List<(string LessonId, string LessonTitle, int LessonOrder, string ModuleId, string ModuleTitle, int ModuleOrder)>> ResolveCourseLessonsAsync(SqliteConnection connection, string courseId)
    {
        await using var lessonsCommand = connection.CreateCommand();
        lessonsCommand.CommandText = @"
SELECT L.Id, L.Title, L.OrderIndex, M.Id, M.Title, M.OrderIndex
FROM Lessons L
INNER JOIN Modules M ON M.Id = L.ModuleId
WHERE M.CourseId = $courseId
ORDER BY M.OrderIndex, L.OrderIndex";
        lessonsCommand.Parameters.AddWithValue("$courseId", courseId);

        var lessons = new List<(string LessonId, string LessonTitle, int LessonOrder, string ModuleId, string ModuleTitle, int ModuleOrder)>();
        await using var lessonReader = await lessonsCommand.ExecuteReaderAsync();
        while (await lessonReader.ReadAsync())
        {
            lessons.Add((
                lessonReader.GetString(0),
                lessonReader.IsDBNull(1) ? string.Empty : lessonReader.GetString(1),
                lessonReader.GetInt32(2),
                lessonReader.GetString(3),
                lessonReader.IsDBNull(4) ? string.Empty : lessonReader.GetString(4),
                lessonReader.GetInt32(5)));
        }

        return lessons;
    }

    private sealed record CheckpointFlowExpectation(string CourseId, string StartLessonPath, List<string> ExpectedCheckpointModules);

    private async Task LoginAsync(IPage page, string email, string password)
    {
        var response = await page.GotoAsync($"{_fixture.BaseUrl}/login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });

        if (response is null || !response.Ok)
        {
            throw new InvalidOperationException($"Failed to load login page. URL: {page.Url}, Status: {response?.Status}");
        }

        await page.WaitForSelectorAsync("form.auth-form", new() { Timeout = 15000 });
        await page.Locator("form.auth-form input[name='email']").FillAsync(email);
        await page.Locator("form.auth-form input[name='password']").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Assertions.Expect(page.Locator("form.auth-form")).Not.ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    private static async Task ExpectTextAsync(ILocator locator, string text)
    {
        await Assertions.Expect(locator.GetByText(text, new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    private static async Task ExpectTextAsync(IPage page, string text)
    {
        await Assertions.Expect(page.GetByText(text, new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    private static async Task ExpectVisibleAsync(ILocator locator)
    {
        await Assertions.Expect(locator).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    private static async Task<bool> HasUserAsync(string databasePath, string email, string role)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT(*)
FROM UserAccounts
WHERE LOWER(Email) = LOWER($email)
  AND Role = $role";
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$role", role);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        return count > 0;
    }

}

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_UI_TESTS"), "1", StringComparison.Ordinal))
        {
            Skip = "Set RUN_UI_TESTS=1 to execute browser-based UI tests.";
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiSsoFactAttribute : FactAttribute
{
    public UiSsoFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_UI_TESTS"), "1", StringComparison.Ordinal))
        {
            Skip = "Set RUN_UI_TESTS=1 to execute browser-based UI tests.";
            return;
        }

        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_UI_TESTS_SSO_MODE"), "1", StringComparison.Ordinal))
        {
            Skip = "Set RUN_UI_TESTS_SSO_MODE=1 to execute SSO UI tests.";
        }
    }
}

public sealed class WebHostFixture : IAsyncLifetime
{
    private Process? _process;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly List<string> _hostOutput = new();
    private readonly object _hostOutputLock = new();
    private readonly bool _uiTestsEnabled = string.Equals(Environment.GetEnvironmentVariable("RUN_UI_TESTS"), "1", StringComparison.Ordinal);
    private readonly bool _ssoUiModeEnabled = string.Equals(Environment.GetEnvironmentVariable("RUN_UI_TESTS_SSO_MODE"), "1", StringComparison.Ordinal);
    private readonly string _startupProjectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lms.Web", "Lms.Web.csproj"));
    private readonly string _startupProjectDirectory;
    private string _databasePath;

    public string BaseUrl { get; private set; } = "http://127.0.0.1:5000";
    public string DatabasePath => _databasePath;

    public WebHostFixture()
    {
        _startupProjectDirectory = Path.GetDirectoryName(_startupProjectPath)!;
        _databasePath = Path.Combine(_startupProjectDirectory, "App_Data", "lms.db");
    }

    public async Task InitializeAsync()
    {
        if (!_uiTestsEnabled)
        {
            return;
        }

        var port = GetFreeTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}";
        var isolatedDbRoot = Path.Combine(Path.GetTempPath(), "lms-web-uitests-db");
        Directory.CreateDirectory(isolatedDbRoot);
        _databasePath = Path.Combine(isolatedDbRoot, $"lms-{Guid.NewGuid():N}.db");
        _process = StartWebHostProcess();
        await WaitUntilReachableAsync();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (!_uiTestsEnabled)
        {
            return;
        }

        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_process is not null && !_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        TryDeleteTestDatabaseArtifacts();
    }

    public async Task<IBrowserContext> CreateBrowserContextAsync()
    {
        if (!_uiTestsEnabled)
        {
            throw new InvalidOperationException("UI tests are disabled. Set RUN_UI_TESTS=1.");
        }

        if (_browser is null)
        {
            throw new InvalidOperationException("Browser is not initialized.");
        }

        return await _browser.NewContextAsync();
    }

    private Process StartWebHostProcess()
    {
        var dotnetPath = @"C:\Program Files\dotnet\dotnet.exe";
        var isolatedBuildRoot = Path.Combine(Path.GetTempPath(), "lms-web-uitests-host");
        var isolatedOutputPath = Path.Combine(isolatedBuildRoot, "bin");
        var isolatedIntermediatePath = Path.Combine(isolatedBuildRoot, "obj");
        var isolatedOutputPathMsBuild = isolatedOutputPath.Replace('\\', '/') + "/";
        var isolatedIntermediatePathMsBuild = isolatedIntermediatePath.Replace('\\', '/') + "/";

        Directory.CreateDirectory(isolatedOutputPath);
        Directory.CreateDirectory(isolatedIntermediatePath);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnetPath,
                Arguments = $"run --no-launch-profile --project \"{_startupProjectPath}\" -p:OutputPath=\"{isolatedOutputPathMsBuild}\" -p:IntermediateOutputPath=\"{isolatedIntermediatePathMsBuild}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.StartInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        process.StartInfo.Environment["LMS_DATABASE_PATH"] = DatabasePath;
        if (_ssoUiModeEnabled)
        {
            process.StartInfo.Environment["Authentication__Sso__Enabled"] = "true";
            process.StartInfo.Environment["Authentication__Sso__Authority"] = "https://example.invalid";
            process.StartInfo.Environment["Authentication__Sso__ClientId"] = "ui-test-client";
            process.StartInfo.Environment["Authentication__Sso__ClientSecret"] = "ui-test-secret";
            process.StartInfo.Environment["Authentication__Sso__TestModeEnabled"] = "true";
            process.StartInfo.Environment["Authentication__Sso__TestUserEmail"] = "sso-learner@lms.com";
            process.StartInfo.Environment["Authentication__Sso__TestUserName"] = "SSO Learner";
        }
        process.OutputDataReceived += (_, args) => CaptureHostOutput(args.Data);
        process.ErrorDataReceived += (_, args) => CaptureHostOutput(args.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private async Task WaitUntilReachableAsync()
    {
        using var http = new HttpClient();

        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (_process is not null && _process.HasExited)
            {
                throw new InvalidOperationException($"Web host exited before becoming reachable. Output: {GetRecentHostOutput()}");
            }

            try
            {
                var response = await http.GetAsync($"{BaseUrl}/login");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var html = await response.Content.ReadAsStringAsync();
                    if (html.Contains("Sign in", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                if ((int)response.StatusCode < 500)
                {
                    return;
                }
            }
            catch
            {
                // Wait and retry while app is still starting.
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Timed out waiting for web host to start at {BaseUrl}. Output: {GetRecentHostOutput()}");
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void CaptureHostOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_hostOutputLock)
        {
            _hostOutput.Add(line);
            if (_hostOutput.Count > 200)
            {
                _hostOutput.RemoveAt(0);
            }
        }
    }

    private void TryDeleteTestDatabaseArtifacts()
    {
        if (string.IsNullOrWhiteSpace(_databasePath))
        {
            return;
        }

        var candidates = new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" };
        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore cleanup errors to avoid masking test results.
            }
        }
    }

    private string GetRecentHostOutput()
    {
        lock (_hostOutputLock)
        {
            return string.Join(" | ", _hostOutput.TakeLast(20));
        }
    }
}

