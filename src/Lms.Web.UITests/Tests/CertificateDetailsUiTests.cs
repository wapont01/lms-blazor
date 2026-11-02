using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class CertificateDetailsUiTests(WebHostFixture fixture) : IClassFixture<WebHostFixture>
{
    [UiFact]
    public async Task CertificateDetails_UsesCorrectStudentPath_AndFitsNarrowScreens()
    {
        await CheckStudentPathAsync("Prelicensing", false);
        await CheckStudentPathAsync("ContinuingEducation", false);
        await CheckStudentPathAsync("ContinuingEducation", true);
    }

    private async Task CheckStudentPathAsync(string courseType, bool hasRecord)
    {
        var courseId = Guid.NewGuid().ToString().ToUpperInvariant();
        var enrollmentId = Guid.NewGuid().ToString().ToUpperInvariant();
        var licenseNumber = $"SYNTHETIC-{Guid.NewGuid():N}"[..35];
        await using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using var userCommand = connection.CreateCommand();
        userCommand.CommandText = "SELECT Id FROM UserAccounts WHERE Email='william.guest@wlr.com'";
        var userId = (string)(await userCommand.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Seed learner missing."));
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE UserAccounts SET EmailVerifiedAtUtc=CURRENT_TIMESTAMP WHERE Id=$user;
                INSERT INTO Courses
                    (Id,Title,Slug,CreatedAt,CreditHours,DurationHours,IsArchived,IsPublished,Level,Price,RequiresProctoredExam,ComplianceType,State)
                VALUES ($course,'Synthetic certificate UI course',$slug,CURRENT_TIMESTAMP,1,1,0,1,'Beginner','0',0,$type,$state);
                INSERT INTO Enrollments
                    (Id,Completed,ConsentStatus,CourseId,EnrolledAt,EnrollmentSource,ProgressPercent,UserAccountId)
                VALUES ($enrollment,1,'NotRequired',$course,CURRENT_TIMESTAMP,'LearnerPurchase','100',$user);
                INSERT INTO Units (Id,CourseId,Title,OrderIndex) VALUES ($unit,$course,'Synthetic unit',1);
                INSERT INTO Lessons (Id,ContentType,DurationMinutes,IsRequired,UnitId,OrderIndex,TextContent,Title)
                VALUES ($lesson,'Text',1,1,$unit,1,'Synthetic content','Synthetic lesson');
                INSERT INTO LessonProgresses (Id,UserAccountId,LessonId,Completed)
                VALUES ($progress,$user,$lesson,1);
                INSERT INTO CourseExams (Id,CourseId,Title,PassPercent,RetakeCooldownMinutes,IsRequired)
                VALUES ($exam,$course,'Synthetic final','75',0,1);
                INSERT INTO ExamAttempts (Id,AttemptNumber,CourseExamId,Passed,ScorePercent,StartedAt,SubmittedAt,UserAccountId)
                VALUES ($attempt,1,$exam,1,'100',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,$user);
                """;
            command.Parameters.AddWithValue("$user", userId);
            command.Parameters.AddWithValue("$course", courseId);
            command.Parameters.AddWithValue("$slug", $"synthetic-certificate-ui-{courseId}");
            command.Parameters.AddWithValue("$enrollment", enrollmentId);
            command.Parameters.AddWithValue("$type", courseType);
            command.Parameters.AddWithValue("$state", !hasRecord && courseType == "ContinuingEducation" ? "Texas" : "Florida");
            foreach (var key in new[] { "$unit", "$lesson", "$progress", "$exam", "$attempt" })
                command.Parameters.AddWithValue(key, Guid.NewGuid().ToString().ToUpperInvariant());
            await command.ExecuteNonQueryAsync();
        }
        if (hasRecord)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO RegulatoryDataSnapshots
                    (Id,State,SourceName,SourceUrl,RetrievedAtUtc,ImportedAtUtc,Status)
                VALUES ($snapshot,'Florida','Synthetic commission fixture','https://example.invalid',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,'Approved');
                INSERT INTO RegulatoryLicensees (Id,SnapshotId,State,Name,LicenseNumber,LicenseType,IsBicEligible)
                VALUES ($license,$snapshot,'Florida','Licensee, Synthetic',$number,'BK',0);
                INSERT INTO RosterAccountRequests (Id,UserAccountId,RegulatoryLicenseeId,Status,SubmittedAtUtc,ReviewNote)
                VALUES ($request,$user,$license,'Confirmed',CURRENT_TIMESTAMP,'');
                """;
            command.Parameters.AddWithValue("$user", userId);
            command.Parameters.AddWithValue("$number", licenseNumber);
            foreach (var key in new[] { "$snapshot", "$license", "$request" })
                command.Parameters.AddWithValue(key, Guid.NewGuid().ToString().ToUpperInvariant());
            await command.ExecuteNonQueryAsync();
        }
        await using var context = await fixture.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/login");
        await page.Locator("input[name='email']").FillAsync("william.guest@wlr.com");
        await page.Locator("input[name='password']").FillAsync("Learner123!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.Ordinal));
        await page.GotoAsync($"{fixture.BaseUrl}/certification/{courseId}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Certificate Details", Exact = true })).ToBeVisibleAsync();
        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Confirm certificate details", Exact = true });
        if (courseType == "Prelicensing")
        {
            await Assertions.Expect(page.GetByText("No license record needed", new() { Exact = true })).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#certificate-license")).ToHaveCountAsync(0);
        }
        else if (!hasRecord)
        {
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Find my license record", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(submit).ToBeDisabledAsync();
        }
        else
        {
            await Assertions.Expect(page.GetByText(licenseNumber, new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Roster name: Licensee, Synthetic", new() { Exact = true })).ToBeVisibleAsync();
            Assert.Equal("Licensee, Synthetic", await page.GetByLabel("Full name as printed", new() { Exact = true }).InputValueAsync());
            await Assertions.Expect(page.Locator("#certificate-first-name")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#certificate-last-name")).ToHaveCountAsync(0);
        }
        foreach (var width in new[] { 1280, 768, 320 })
        {
            await page.SetViewportSizeAsync(width, 900);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= document.documentElement.clientWidth"));
        }
        if (courseType == "Prelicensing" || hasRecord)
        {
            if (courseType == "Prelicensing")
            {
                await page.GetByLabel("Full name as printed", new() { Exact = true }).FillAsync("Taylor Middle Student");
            }
            await page.GetByRole(AriaRole.Checkbox).CheckAsync();
            await submit.ClickAsync();
            try
            {
                await page.WaitForFunctionAsync("document.querySelector('.certificate-saved') || document.querySelector('[role=alert]')", null, new() { Timeout = 10000 });
            }
            catch (TimeoutException ex)
            {
                throw new InvalidOperationException(await page.Locator(".certification-page").InnerTextAsync(), ex);
            }
            var error = page.Locator(".auth-error");
            Assert.False(await error.IsVisibleAsync(), await error.CountAsync() > 0 ? await error.InnerTextAsync() : "");
            await Assertions.Expect(page.GetByText("Certificate details saved", new() { Exact = true })).ToBeVisibleAsync();
            await using var check = connection.CreateCommand();
            check.CommandText = "SELECT CertificationLicenseNumber FROM Enrollments WHERE Id=$id";
            check.Parameters.AddWithValue("$id", enrollmentId);
            Assert.Equal(hasRecord ? licenseNumber : "", Convert.ToString(await check.ExecuteScalarAsync()));
            check.CommandText = "SELECT CertificationIdentitySource FROM Enrollments WHERE Id=$id";
            Assert.Equal(hasRecord ? "StudentConfirmedRoster" : "PrelicensingStudent", await check.ExecuteScalarAsync());
            check.CommandText = "SELECT CertificationIdentitySource FROM CompletionCertificates WHERE EnrollmentId=$id";
            Assert.Equal(hasRecord ? "StudentConfirmedRoster" : "PrelicensingStudent", await check.ExecuteScalarAsync());
            check.CommandText = "SELECT CertificationPrintedName FROM Enrollments WHERE Id=$id";
            Assert.Equal(hasRecord ? "Licensee, Synthetic" : "Taylor Middle Student", Convert.ToString(await check.ExecuteScalarAsync()));
            check.CommandText = "SELECT CertificationPrintedName FROM CompletionCertificates WHERE EnrollmentId=$id";
            Assert.Equal(hasRecord ? "Licensee, Synthetic" : "Taylor Middle Student", Convert.ToString(await check.ExecuteScalarAsync()));
            check.CommandText = "SELECT CertificationFirstName || '|' || CertificationLastName FROM CompletionCertificates WHERE EnrollmentId=$id";
            Assert.Equal(hasRecord ? "Synthetic|Licensee" : "Taylor Middle|Student", Convert.ToString(await check.ExecuteScalarAsync()));
            await page.ReloadAsync();
            await Assertions.Expect(page.GetByText("Your certificate is available", new() { Exact = true })).ToBeVisibleAsync();
        }
    }
}
