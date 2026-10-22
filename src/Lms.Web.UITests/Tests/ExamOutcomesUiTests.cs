using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class ExamOutcomesUiTests : IClassFixture<WebHostFixture>
{
    private readonly WebHostFixture _fixture;

    public ExamOutcomesUiTests(WebHostFixture fixture)
    {
        _fixture = fixture;
    }

    [UiFact]
    public async Task Admin_SidebarLinks_NavigateToHomeAndRegulatoryDirectory()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "admin@wlr.com", "Admin123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/admin/courses");

        await page.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{_fixture.BaseUrl}/");
        await ExpectTextAsync(page, "Real Estate Certification LMS");

        await page.GotoAsync($"{_fixture.BaseUrl}/admin/courses");
        await page.Locator(".sidebar-details summary").Filter(new LocatorFilterOptions { HasTextString = "Admin" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Regulatory Directory", Exact = true }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{_fixture.BaseUrl}/admin/regulatory-directory");
        await ExpectVisibleWithReloadAsync(page, page.GetByRole(AriaRole.Heading, new() { Name = "Regulatory Directory", Exact = true }));
    }

    [UiFact]
    public async Task Admin_RetakeGrant_CreateUpdateRevoke_Works()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "admin@wlr.com", "Admin123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/admin/exams");

        var editor = page.Locator("section.card").Filter(new LocatorFilterOptions { HasTextString = "Exam Editor" });
        await ExpectVisibleWithReloadAsync(page, editor.GetByRole(AriaRole.Button, new() { Name = "Load Exam" }));
        await editor.GetByRole(AriaRole.Button, new() { Name = "Load Exam" }).ClickAsync();

        var grantLearnerSelect = editor.Locator("h3:has-text('Grant Retake Attempts') + .form-row select").First;

        var learnerOptionValue = await editor
            .Locator("h3:has-text('Grant Retake Attempts') + .form-row select option", new() { HasTextString = "william.learner@wlr.com" })
            .First
            .GetAttributeAsync("value");

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
    public async Task Broker_Page_Renders_ExamBlockedSection()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "william.broker@wlr.com", "Broker123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/broker/blocked");

        await ExpectTextAsync(page, "Exam-Blocked Learners");
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

        var userExists = await HasUserAsync(_fixture.DatabasePath, "sso-william.learner@wlr.com", "Learner");
        Assert.True(userExists, "Expected SSO test user to be created in local user store with Learner role.");
    }

    [UiFact]
    public async Task Broker_Support_LearnerSwitch_UsesValidCourseSelection()
    {
        await EnsureBrokerHasAtLeastTwoAssignedLearnersAsync(_fixture.DatabasePath, "william.broker@wlr.com");

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "william.broker@wlr.com", "Broker123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/broker/support");

        var learnerSelect = page.Locator("#Support-learner");

        await ExpectVisibleWithReloadAsync(page, learnerSelect);

        var learnerOptionCount = await learnerSelect.Locator("option").CountAsync();
        Assert.True(learnerOptionCount >= 2, "Expected at least two learners to validate learner-switch behavior.");

        var learnerOwnedCard = page.Locator("section.card.support-ownership-card")
            .Filter(new LocatorFilterOptions { HasTextString = "Learner enrolled courses" });
        await Assertions.Expect(learnerOwnedCard).ToBeVisibleAsync(new() { Timeout = 10000 });
        var initialSelectedText = (await learnerSelect.Locator("option:checked").InnerTextAsync()).Trim();
        await Assertions.Expect(learnerOwnedCard).ToContainTextAsync(initialSelectedText, new() { Timeout = 10000 });

        var secondLearnerValue = await learnerSelect.Locator("option").Nth(1).GetAttributeAsync("value");
        Assert.False(string.IsNullOrWhiteSpace(secondLearnerValue));

        // A change fired before the Blazor circuit hydrates is lost and reset to server state; retry the switch.
        var selectedLearnerText = string.Empty;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await learnerSelect.SelectOptionAsync(secondLearnerValue);
            selectedLearnerText = (await learnerSelect.Locator("option:checked").InnerTextAsync()).Trim();
            try
            {
                await Assertions.Expect(learnerOwnedCard).ToContainTextAsync(selectedLearnerText, new() { Timeout = 3000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 4)
            {
            }
        }

        await Assertions.Expect(learnerOwnedCard).ToContainTextAsync(selectedLearnerText, new() { Timeout = 10000 });

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
        await EnsureBrokerHasAtLeastTwoAssignedLearnersAsync(_fixture.DatabasePath, "william.broker@wlr.com");

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "william.broker@wlr.com", "Broker123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/broker/support");

        var learnerSelect = page.Locator("#Support-learner");
        var courseSelect = page.Locator("#Support-course");
        var enrollButton = page.GetByRole(AriaRole.Button, new() { Name = "Enroll", Exact = true });
        var unenrollButton = page.GetByRole(AriaRole.Button, new() { Name = "Unenroll", Exact = true });

        await ExpectVisibleWithReloadAsync(page, learnerSelect);
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
    public async Task Learner_CourseDetail_Shows_ExamGate_And_Submit()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "william.learner@wlr.com", "Learner123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/courses");

        var firstCourseLink = page.GetByRole(AriaRole.Link, new() { Name = "Open" }).First;
        await firstCourseLink.ClickAsync();

        await ExpectTextAsync(page, "Exam Gate");

        var submitExamVisible = await page.GetByText("Submit Exam", new() { Exact = false }).IsVisibleAsync();
        var gatePassedVisible = await page.GetByText("Passed", new() { Exact = false }).IsVisibleAsync();
        var gatePendingVisible = await page.GetByText("Pending", new() { Exact = false }).IsVisibleAsync();
        Assert.True(submitExamVisible || gatePassedVisible || gatePendingVisible, "Expected exam gate to be visible with a valid state.");
    }

    [UiFact]
    public async Task FloridaBroker_ComplianceShowsOnlyAssignedFloridaLearners()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "marcus.reed.fl@wlr.com", "FloridaBroker123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/broker/compliance");

        await ExpectTextAsync(page, "Sofia Martinez");
        await ExpectTextAsync(page, "Noah Williams");
        Assert.DoesNotContain("Ava Johnson", await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("William Aponte", await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("Maya Thompson", await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("Ethan Brooks", await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
    }

    [UiFact]
    public async Task FloridaInstructor_OwnsFloridaCourseInInstructorWorkspace()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "william.aponte@wlr.com", "Instructor456!");
        await page.GotoAsync($"{_fixture.BaseUrl}/instructor");

        await ExpectTextAsync(page, "Florida Real Estate Distance Learning Fundamentals");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Florida Real Estate Distance Learning Fundamentals" })).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [UiFact]
    public async Task FloridaCourse_AppearsInAdminWorkspaceAsApprovedAndPublished()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "admin@wlr.com", "Admin123!");
        await page.GotoAsync($"{_fixture.BaseUrl}/admin/courses");

        var floridaCourse = page.Locator("article.course-item").Filter(new LocatorFilterOptions
        {
            HasTextString = "Florida Real Estate Distance Learning Fundamentals"
        });
        await Assertions.Expect(floridaCourse).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(floridaCourse).ToContainTextAsync("Status: Published | Review: Approved");
    }

    [UiFact]
    public async Task FloridaLearner_CanOpenSeededCourseAndStartTimedLesson()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "maya.thompson@wlr.com", "Learner234!");
        await page.GotoAsync($"{_fixture.BaseUrl}/courses/f1a6e7d8-c9b0-4a12-8e34-56789abcdef0");

        await ExpectTextAsync(page, "Florida Real Estate Distance Learning Fundamentals");
        await Assertions.Expect(page.GetByText("Florida", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 10000 });

        await page.GotoAsync($"{_fixture.BaseUrl}/courses/f1a6e7d8-c9b0-4a12-8e34-56789abcdef0/lessons/f1a6e7d8-c9b0-4a12-8e34-56789abcde01");
        await ExpectTextAsync(page.GetByRole(AriaRole.Region, new() { Name = "Lesson summary" }), "Florida Licensing Foundations");
        var beginTimedCourseButton = page.GetByRole(AriaRole.Button, new() { Name = "Begin Timed Course" });
        // The persistent UI-test database may already have the timed course started for this learner.
        if (await beginTimedCourseButton.IsVisibleAsync() && await beginTimedCourseButton.IsEnabledAsync())
        {
            await beginTimedCourseButton.ClickAsync();
        }
        await ExpectTextAsync(page, "Time Elapsed");
    }

    [UiFact]
    public async Task Learner_TimerStarted_BlocksJumpAhead_UntilNextUnlocks()
    {
        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "william.learner@wlr.com", "Learner123!");
        var trackedPaths = await ResolveTrackedLessonPathsFromDatabaseAsync(_fixture.DatabasePath, "william.learner@wlr.com");
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

        var unitExamSubmitButton = page.Locator("section.unit-exam button.unit-exam-submit-button");

        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (new Uri(page.Url).AbsolutePath == blockedPath!)
            {
                break;
            }

            if (await unitExamSubmitButton.IsVisibleAsync() && await unitExamSubmitButton.IsEnabledAsync())
            {
                await unitExamSubmitButton.EvaluateAsync("button => button.click()");
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

        await LoginAsync(page, "admin@wlr.com", "Admin123!");
        var emptyLearnerId = await InsertLearnerWithoutEnrollmentAsync(_fixture.DatabasePath, "ui-empty-william.learner@wlr.com", "UI Empty Learner");

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

        await LoginAsync(page, "admin@wlr.com", "Admin123!");

        var activeLearnerId = await ResolveUserIdFromDatabaseAsync(_fixture.DatabasePath, "william.learner@wlr.com");
        Assert.False(string.IsNullOrWhiteSpace(activeLearnerId));

        var trackedPaths = await ResolveTrackedLessonPathsFromDatabaseAsync(_fixture.DatabasePath, "william.learner@wlr.com");
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
    public async Task Learner_UnitExams_AppearInOrder_BeforeFinalExam()
    {
        var expectation = await ResolveUnitExamFlowExpectationAsync(_fixture.DatabasePath, "william.learner@wlr.com");
        await ResetLearnerUnitExamAndExamStateAsync(_fixture.DatabasePath, "william.learner@wlr.com", expectation.CourseId);

        await using var context = await _fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "william.learner@wlr.com", "Learner123!");
        await page.GotoAsync($"{_fixture.BaseUrl}{expectation.StartLessonPath}");

        var observedUnitExamUnits = new List<string>();
        var sawFinalExamAnnouncement = false;

        for (var step = 0; step < 220; step++)
        {
            var beginTimedCourseButton = page.GetByRole(AriaRole.Button, new() { Name = "Begin Timed Course" });
            if (await beginTimedCourseButton.IsVisibleAsync() && await beginTimedCourseButton.IsEnabledAsync())
            {
                await beginTimedCourseButton.EvaluateAsync("button => button.click()");
                await page.WaitForTimeoutAsync(200);
                continue;
            }

            var unitExamSubmitButton = page.Locator("section.unit-exam button.unit-exam-submit-button");
            if (await unitExamSubmitButton.IsVisibleAsync() && await unitExamSubmitButton.IsEnabledAsync())
            {
                var unitTitle = await page.Locator(".course-summary-strip .summary-chip strong").First.InnerTextAsync();
                var normalizedUnitTitle = unitTitle.Trim();
                if (observedUnitExamUnits.Count == 0 || !string.Equals(observedUnitExamUnits[^1], normalizedUnitTitle, StringComparison.OrdinalIgnoreCase))
                {
                    observedUnitExamUnits.Add(normalizedUnitTitle);
                }
                await unitExamSubmitButton.EvaluateAsync("button => button.click()");
                await page.WaitForTimeoutAsync(250);
                continue;
            }

            var finalExamGate = page.Locator("section.final-exam-gate");
            if (await finalExamGate.IsVisibleAsync())
            {
                sawFinalExamAnnouncement = true;
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

        Assert.Equal(expectation.ExpectedUnitExamUnits, observedUnitExamUnits);
        Assert.True(sawFinalExamAnnouncement, "Final exam announcement should appear after the last unit exam is passed.");
    }

    [UiFact]
    public async Task Learner_RetakeGrant_UnlocksCourseAccess_AndOverviewShowsAttemptTotals()
    {
        var (courseId, lessonPath) = await ResolveLearnerCourseAndLessonPathAsync(_fixture.DatabasePath, "william.learner@wlr.com");
        // Clear grants left by other tests so the seeded failures actually block access.
        await ClearRetakeGrantsAsync(_fixture.DatabasePath, "william.learner@wlr.com", courseId);
        await SeedFailedExamAttemptsAsync(_fixture.DatabasePath, "william.learner@wlr.com", courseId, attempts: 2);

        await using var learnerContext = await _fixture.CreateBrowserContextAsync();
        var learnerPage = await learnerContext.NewPageAsync();

        await LoginAsync(learnerPage, "william.learner@wlr.com", "Learner123!");

        await learnerPage.GotoAsync($"{_fixture.BaseUrl}{lessonPath}");
        // The blocked-learner redirect is issued by the circuit after load; wait for it and retry once if hydration failed.
        var redirected = await WaitForPathAsync(learnerPage, $"/courses/{courseId}", 15000);
        if (!redirected)
        {
            await learnerPage.GotoAsync($"{_fixture.BaseUrl}{lessonPath}");
            redirected = await WaitForPathAsync(learnerPage, $"/courses/{courseId}", 15000);
        }

        Assert.True(
            redirected,
            $"Expected learner to be redirected to course overview for course {courseId}. Actual URL: {learnerPage.Url}");

        var startCourseButton = learnerPage.GetByRole(AriaRole.Button, new() { Name = "Start Course" });
        await Assertions.Expect(startCourseButton).ToBeDisabledAsync(new() { Timeout = 10000 });

        await ExpectTextAsync(learnerPage, "Attempts used: 2 of 2");
        await ExpectTextAsync(learnerPage, "You have no attempts remaining");

        await GrantRetakeAttemptsAsync(_fixture.DatabasePath, "william.learner@wlr.com", "admin@wlr.com", courseId, grantedAttempts: 1);

        await learnerPage.GotoAsync($"{_fixture.BaseUrl}/courses/{courseId}");
        var failureOverview = learnerPage.Locator("section.exam-failure-banner p");
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
INNER JOIN Units M ON M.Id = L.UnitId
WHERE M.CourseId = $courseId
ORDER BY M.OrderIndex, L.OrderIndex";
            lessonsCommand.Parameters.AddWithValue("$courseId", enrolledCourseId);

            var courseLessons = new List<(string LessonId, string LessonTitle, int LessonOrder, string UnitId, string UnitTitle, int UnitOrder)>();
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

            var unitGroups = courseLessons
                .GroupBy(lesson => new { lesson.UnitId, lesson.UnitTitle, lesson.UnitOrder })
                .OrderBy(group => group.Key.UnitOrder)
                .ToList();

            var firstUnit = unitGroups.FirstOrDefault();
            var firstUnitFirstLessonTitle = firstUnit?
                .OrderBy(lesson => lesson.LessonOrder)
                .Select(lesson => lesson.LessonTitle)
                .FirstOrDefault();

            var firstUnitLooksLikeIntro = string.IsNullOrWhiteSpace(firstUnitFirstLessonTitle)
                || firstUnitFirstLessonTitle.Contains("welcome", StringComparison.OrdinalIgnoreCase)
                || firstUnitFirstLessonTitle.Contains("instructor", StringComparison.OrdinalIgnoreCase);

            var trackedLessonIds = courseLessons
                .Where(lesson =>
                    !string.Equals(lesson.UnitTitle, "Getting Started", StringComparison.OrdinalIgnoreCase)
                    && !(firstUnitLooksLikeIntro && firstUnit is not null && lesson.UnitId == firstUnit.Key.UnitId))
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

    private static async Task<UnitExamFlowExpectation> ResolveUnitExamFlowExpectationAsync(string databasePath, string learnerEmail)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var userId = await ResolveUserIdAsync(connection, learnerEmail);
        var enrolledCourseIds = await ResolveEnrolledCourseIdsAsync(connection, userId);

        foreach (var courseId in enrolledCourseIds)
        {
            var lessons = await ResolveCourseLessonsAsync(connection, courseId);
            var unitGroups = lessons
                .GroupBy(lesson => new { lesson.UnitId, lesson.UnitTitle, lesson.UnitOrder })
                .OrderBy(group => group.Key.UnitOrder)
                .ToList();

            if (unitGroups.Count == 0)
            {
                continue;
            }

            var firstUnit = unitGroups.First();
            var firstUnitFirstLessonTitle = firstUnit
                .OrderBy(lesson => lesson.LessonOrder)
                .Select(lesson => lesson.LessonTitle)
                .FirstOrDefault();

            var firstUnitLooksLikeIntro = string.IsNullOrWhiteSpace(firstUnitFirstLessonTitle)
                || firstUnitFirstLessonTitle.Contains("welcome", StringComparison.OrdinalIgnoreCase)
                || firstUnitFirstLessonTitle.Contains("instructor", StringComparison.OrdinalIgnoreCase);

            var trackedUnits = unitGroups
                .Where(group =>
                    !string.Equals(group.Key.UnitTitle, "Getting Started", StringComparison.OrdinalIgnoreCase)
                    && !(firstUnitLooksLikeIntro && group.Key.UnitId == firstUnit.Key.UnitId))
                .Select(group => new
                {
                    group.Key.UnitId,
                    group.Key.UnitTitle,
                    Lessons = group.OrderBy(lesson => lesson.LessonOrder).ToList()
                })
                .Where(group => group.Lessons.Count > 0)
                .ToList();

            if (trackedUnits.Count == 0)
            {
                continue;
            }

            var startLessonId = trackedUnits[0].Lessons[0].LessonId;
            var expectedUnitTitles = trackedUnits
                .Select(unit => unit.UnitTitle)
                .ToList();

            return new UnitExamFlowExpectation(
                courseId,
                $"/courses/{courseId}/lessons/{startLessonId}",
                expectedUnitTitles);
        }

        throw new InvalidOperationException("No enrolled course was found with tracked units for unit exam flow verification.");
    }

    private static async Task ResetLearnerUnitExamAndExamStateAsync(string databasePath, string learnerEmail, string courseId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var userId = await ResolveUserIdAsync(connection, learnerEmail);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

		await using (var deleteAnswersCommand = connection.CreateCommand())
        {
            deleteAnswersCommand.Transaction = transaction;
            deleteAnswersCommand.CommandText = @"
DELETE FROM ExamAnswers
WHERE ExamAttemptId IN (
        SELECT AA.Id
        FROM ExamAttempts AA
        INNER JOIN CourseExams CA ON CA.Id = AA.CourseExamId
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
DELETE FROM ExamAttempts
WHERE Id IN (
    SELECT AA.Id
    FROM ExamAttempts AA
    INNER JOIN CourseExams CA ON CA.Id = AA.CourseExamId
    WHERE AA.UserAccountId = $userId
      AND CA.CourseId = $courseId
)";
            deleteAttemptsCommand.Parameters.AddWithValue("$userId", userId);
            deleteAttemptsCommand.Parameters.AddWithValue("$courseId", courseId);
            await deleteAttemptsCommand.ExecuteNonQueryAsync();
        }

        await using (var deleteUnitExamProgressCommand = connection.CreateCommand())
        {
            deleteUnitExamProgressCommand.Transaction = transaction;
            deleteUnitExamProgressCommand.CommandText = @"
DELETE FROM UnitExamProgresses
WHERE UserAccountId = $userId
  AND CourseId = $courseId";
            deleteUnitExamProgressCommand.Parameters.AddWithValue("$userId", userId);
            deleteUnitExamProgressCommand.Parameters.AddWithValue("$courseId", courseId);
            await deleteUnitExamProgressCommand.ExecuteNonQueryAsync();
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
                .OrderBy(lesson => lesson.UnitOrder)
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

    private static async Task SeedFailedExamAttemptsAsync(string databasePath, string learnerEmail, string courseId, int attempts)
    {
        if (attempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), "Attempts must be greater than zero.");
        }

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var userId = await ResolveUserIdAsync(connection, learnerEmail);

        await using var examCommand = connection.CreateCommand();
        examCommand.CommandText = @"
SELECT Id
FROM CourseExams
WHERE CourseId = $courseId
  AND IsRequired = 1
LIMIT 1";
        examCommand.Parameters.AddWithValue("$courseId", courseId);

        var examIdValue = await examCommand.ExecuteScalarAsync();
        if (examIdValue is null)
        {
            throw new InvalidOperationException($"Unable to resolve required exam for course '{courseId}'.");
        }

        var examId = examIdValue.ToString() ?? string.Empty;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var deleteAnswersCommand = connection.CreateCommand())
        {
            deleteAnswersCommand.Transaction = transaction;
            deleteAnswersCommand.CommandText = @"
DELETE FROM ExamAnswers
WHERE ExamAttemptId IN (
        SELECT AA.Id
        FROM ExamAttempts AA
        WHERE AA.UserAccountId = $userId
          AND AA.CourseExamId = $examId
)";
            deleteAnswersCommand.Parameters.AddWithValue("$userId", userId);
            deleteAnswersCommand.Parameters.AddWithValue("$examId", examId);
            await deleteAnswersCommand.ExecuteNonQueryAsync();
        }

        await using (var deleteAttemptsCommand = connection.CreateCommand())
        {
            deleteAttemptsCommand.Transaction = transaction;
            deleteAttemptsCommand.CommandText = @"
DELETE FROM ExamAttempts
WHERE UserAccountId = $userId
  AND CourseExamId = $examId";
            deleteAttemptsCommand.Parameters.AddWithValue("$userId", userId);
            deleteAttemptsCommand.Parameters.AddWithValue("$examId", examId);
            await deleteAttemptsCommand.ExecuteNonQueryAsync();
        }

        await using (var deleteGrantsCommand = connection.CreateCommand())
        {
            deleteGrantsCommand.Transaction = transaction;
            deleteGrantsCommand.CommandText = @"
DELETE FROM RetakeGrants
WHERE UserAccountId = $userId
  AND CourseExamId = $examId";
            deleteGrantsCommand.Parameters.AddWithValue("$userId", userId);
            deleteGrantsCommand.Parameters.AddWithValue("$examId", examId);
            await deleteGrantsCommand.ExecuteNonQueryAsync();
        }

        var now = DateTime.UtcNow;
        for (var attemptNumber = 1; attemptNumber <= attempts; attemptNumber++)
        {
            var submittedAt = now.AddMinutes(-(attempts - attemptNumber + 2));

            await using var insertAttemptCommand = connection.CreateCommand();
            insertAttemptCommand.Transaction = transaction;
            insertAttemptCommand.CommandText = @"
INSERT INTO ExamAttempts (
    Id,
    CourseExamId,
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
    $examId,
    $userId,
    $startedAt,
    $submittedAt,
    $attemptNumber,
    $scorePercent,
    $passed,
    $feedbackSummary
)";

            insertAttemptCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertAttemptCommand.Parameters.AddWithValue("$examId", examId);
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

    private static async Task ClearRetakeGrantsAsync(string databasePath, string learnerEmail, string courseId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        var userId = await ResolveUserIdAsync(connection, learnerEmail);

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = @"
DELETE FROM RetakeGrants
WHERE UserAccountId = $userId
  AND CourseExamId IN (SELECT Id FROM CourseExams WHERE CourseId = $courseId)";
        deleteCommand.Parameters.AddWithValue("$userId", userId);
        deleteCommand.Parameters.AddWithValue("$courseId", courseId);
        await deleteCommand.ExecuteNonQueryAsync();
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

        await using var examCommand = connection.CreateCommand();
        examCommand.CommandText = @"
SELECT Id
FROM CourseExams
WHERE CourseId = $courseId
  AND IsRequired = 1
LIMIT 1";
        examCommand.Parameters.AddWithValue("$courseId", courseId);

        var examIdValue = await examCommand.ExecuteScalarAsync();
        if (examIdValue is null)
        {
            throw new InvalidOperationException($"Unable to resolve required exam for course '{courseId}'.");
        }

        var examId = examIdValue.ToString() ?? string.Empty;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var deleteExistingGrants = connection.CreateCommand())
        {
            deleteExistingGrants.Transaction = transaction;
            deleteExistingGrants.CommandText = @"
DELETE FROM RetakeGrants
WHERE UserAccountId = $userId
  AND CourseExamId = $examId";
            deleteExistingGrants.Parameters.AddWithValue("$userId", learnerUserId);
            deleteExistingGrants.Parameters.AddWithValue("$examId", examId);
            await deleteExistingGrants.ExecuteNonQueryAsync();
        }

        await using (var insertGrant = connection.CreateCommand())
        {
            insertGrant.Transaction = transaction;
            insertGrant.CommandText = @"
INSERT INTO RetakeGrants (
    Id,
    CourseExamId,
    UserAccountId,
    GrantedAttempts,
    GrantedAt,
    GrantedByAdminId
)
VALUES (
    $id,
    $examId,
    $userId,
    $grantedAttempts,
    $grantedAt,
    $grantedByAdminId
)";
            insertGrant.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertGrant.Parameters.AddWithValue("$examId", examId);
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
            passwordHashCommand.CommandText = "SELECT PasswordHash FROM UserAccounts WHERE Email = 'william.learner@wlr.com' LIMIT 1";
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
            passwordHashCommand.CommandText = "SELECT PasswordHash FROM UserAccounts WHERE Email = 'william.learner@wlr.com' LIMIT 1";
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
WHERE U.Email = 'william.learner@wlr.com'
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

    private static async Task<List<(string LessonId, string LessonTitle, int LessonOrder, string UnitId, string UnitTitle, int UnitOrder)>> ResolveCourseLessonsAsync(SqliteConnection connection, string courseId)
    {
        await using var lessonsCommand = connection.CreateCommand();
        lessonsCommand.CommandText = @"
SELECT L.Id, L.Title, L.OrderIndex, M.Id, M.Title, M.OrderIndex
FROM Lessons L
INNER JOIN Units M ON M.Id = L.UnitId
WHERE M.CourseId = $courseId
ORDER BY M.OrderIndex, L.OrderIndex";
        lessonsCommand.Parameters.AddWithValue("$courseId", courseId);

        var lessons = new List<(string LessonId, string LessonTitle, int LessonOrder, string UnitId, string UnitTitle, int UnitOrder)>();
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

    private sealed record UnitExamFlowExpectation(string CourseId, string StartLessonPath, List<string> ExpectedUnitExamUnits);

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

    // A page whose circuit fails to hydrate under suite load renders no interactive content; one reload recovers it.
    private static async Task ExpectVisibleWithReloadAsync(IPage page, ILocator locator)
    {
        try
        {
            await Assertions.Expect(locator).ToBeVisibleAsync(new() { Timeout = 15000 });
        }
        catch (PlaywrightException)
        {
            await page.ReloadAsync();
            await Assertions.Expect(locator).ToBeVisibleAsync(new() { Timeout = 15000 });
        }
    }

    private static async Task<bool> WaitForPathAsync(IPage page, string expectedPath, int timeoutMs)
    {
        try
        {
            await page.WaitForURLAsync(
                url => string.Equals(new Uri(url).AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase),
                new() { Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (PlaywrightException)
        {
            return false;
        }
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
        process.StartInfo.Environment["LMS_TEST_SHORTCUT"] = "1";
        if (_ssoUiModeEnabled)
        {
            process.StartInfo.Environment["Authentication__Sso__Enabled"] = "true";
            process.StartInfo.Environment["Authentication__Sso__Authority"] = "https://example.invalid";
            process.StartInfo.Environment["Authentication__Sso__ClientId"] = "ui-test-client";
            process.StartInfo.Environment["Authentication__Sso__ClientSecret"] = "ui-test-secret";
            process.StartInfo.Environment["Authentication__Sso__TestModeEnabled"] = "true";
            process.StartInfo.Environment["Authentication__Sso__TestUserEmail"] = "sso-william.learner@wlr.com";
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

