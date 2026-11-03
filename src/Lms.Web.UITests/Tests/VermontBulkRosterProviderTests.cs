using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lms.Application.Services;
using Lms.Web.Services;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class VermontBulkRosterProviderTests
{
    [Fact]
    public async Task Downloader_BootstrapsFreshCookiesAndPublishedActionsForEveryAcquisition()
    {
        using var handler = new RosterHandler();
        using var client = new HttpClient(handler);
        var provider = new VermontOprBulkRosterProvider();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var path = TestPath();
            try
            {
                var result = await provider.DownloadFileAsync(client, path, provider.Files[1], CancellationToken.None, (_, _) => { });
                Assert.Equal(4, result.Bytes);
                Assert.Equal(new byte[] { 80, 75, 3, 4 }, await File.ReadAllBytesAsync(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        Assert.Equal(16, handler.Calls);
    }

    [Theory]
    [InlineData("NGLP083-MAIN.xlsx")]
    [InlineData("NGLP081.xlsx")]
    [InlineData("NGLP081-EMGY.xlsx")]
    [InlineData("NGLP081-TEMP.xlsx")]
    [InlineData("NGLP083-BRCH.xlsx")]
    [InlineData("NGLP083-SOLE.xlsx")]
    [InlineData("NGLP082.xlsx")]
    [InlineData("NGLP082-EMGY.xlsx")]
    [InlineData("NGLP082-TEMP.xlsx")]
    public async Task Downloader_AcceptsPartialPostValueResponsesForEveryPublishedCategory(string name)
    {
        var code = VermontOprBulkImportService.ProfessionCode(name);
        using var handler = new RosterHandler(expectedCode: code);
        using var client = new HttpClient(handler);
        var provider = new VermontOprBulkRosterProvider();
        var file = provider.Files.Single(file => file.Name == name);
        var path = TestPath();
        try
        {
            var result = await provider.DownloadFileAsync(client, path, file, CancellationToken.None, (_, _) => { });
            Assert.Equal(4, result.Bytes);
            Assert.Equal(8, handler.Calls);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Downloader_DecodesNearLimitUnicodeHeavyHtmlAcrossRepeatedFreshSessions()
    {
        using var handler = new RosterHandler(largeHtml: true);
        using var client = new HttpClient(handler);
        var provider = new VermontOprBulkRosterProvider();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var path = TestPath();
            try
            {
                var result = await provider.DownloadFileAsync(client, path, provider.Files[1], CancellationToken.None, (_, _) => { });
                Assert.Equal(4, result.Bytes);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        Assert.Equal(16, handler.Calls);
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("csrf")]
    [InlineData("statuses")]
    [InlineData("confirmation")]
    [InlineData("type")]
    [InlineData("html")]
    [InlineData("redirect")]
    [InlineData("download-url")]
    [InlineData("access")]
    [InlineData("empty")]
    [InlineData("incomplete")]
    [InlineData("oversized-html")]
    public async Task Downloader_FailsClosedOnChangedWorkflowAccessOrIncompleteDownload(string failure)
    {
        using var handler = new RosterHandler(failure);
        using var client = new HttpClient(handler);
        var provider = new VermontOprBulkRosterProvider();
        var path = TestPath();
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                provider.DownloadFileAsync(client, path, provider.Files[1], CancellationToken.None, (_, _) => { }));
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Downloader_DoesNotOverwriteAnExistingDestination()
    {
        using var handler = new RosterHandler();
        using var client = new HttpClient(handler);
        var provider = new VermontOprBulkRosterProvider();
        var path = TestPath();
        await File.WriteAllTextAsync(path, "keep");
        try
        {
            await Assert.ThrowsAsync<IOException>(() => provider.DownloadFileAsync(client, path, provider.Files[1], CancellationToken.None, (_, _) => { }));
            Assert.Equal("keep", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    private static string TestPath() => Path.Combine(Directory.GetCurrentDirectory(), "vermont-download-" + Guid.NewGuid().ToString("N"));

    private sealed class RosterHandler(string? failure = null, string expectedCode = "NGLP081", bool largeHtml = false) : HttpMessageHandler
    {
        private const string Portal = "https://secure.professionals.vermont.gov/prweb/PRServletCustom/app/NGLPGuestUser/V9csDxL3sXkkjMC_FR2HrA*/!STANDARD";
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = Calls++ % 8;
            var acquisition = (Calls - 1) / 8;
            var cookie = "vt-session=s" + acquisition;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            Assert.Equal("secure.professionals.vermont.gov", request.RequestUri!.Host);
            if (step == 0)
            {
                Assert.Equal(VermontOprBulkImportService.GuestUrl, request.RequestUri.AbsoluteUri);
                Assert.False(request.Headers.Contains("Cookie"));
                response.StatusCode = HttpStatusCode.Found;
                response.Headers.Location = new Uri(failure == "redirect" ? "https://example.invalid/session" : Portal);
                response.Headers.Add("Set-Cookie", cookie + "; Path=/prweb; Secure; HttpOnly");
                response.Content = new StringContent("");
                return response;
            }
            Assert.Equal(cookie, Assert.Single(request.Headers.GetValues("Cookie")));
            if (step == 1)
            {
                response.Content = new StringContent(failure == "workflow" ? "<html>changed</html>"
                    : failure == "access" ? "<html>CAPTCHA required</html>" : Bootstrap(acquisition));
            }
            else if (step is >= 2 and <= 6)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("pzTransactionId=t" + acquisition, request.RequestUri.Query);
                Assert.Equal("csrf" + acquisition, Assert.Single(request.Headers.GetValues("X-CSRF-Token")));
                var fields = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                    .Select(part => part.Split('=', 2)).ToDictionary(part => WebUtility.UrlDecode(part[0]), part => WebUtility.UrlDecode(part[1]));
                Assert.Equal("HID" + acquisition, fields["pzHarnessID"]);
                if (step == 2)
                {
                    Assert.Equal("Roster Download", fields["LookupType"]);
                    Assert.Equal("mode" + acquisition, fields["pzuiactionzzz"]);
                    response.Content = new StringContent(Roster(acquisition, selected: false));
                }
                else if (step is 3 or 4)
                {
                    Assert.Equal("post" + acquisition, fields["pzuiactionzzz"]);
                    var target = step == 3 ? "$PpyDisplayHarness$pProductIds" : "$PpyDisplayHarness$pSetProfessionType";
                    Assert.Equal(target, fields["pyPropertyTarget"]);
                    Assert.Equal(step == 3 ? "PC027" : expectedCode, fields[target]);
                    response.Content = new StringContent(failure == "type" ? "<html>no real estate</html>"
                        : step == 3 ? """<html>{"ProductIds":"PC027"}</html>"""
                        : "<html>{\"SetProfessionType\":\"" + expectedCode + "\"}</html>");
                }
                else if (step == 5)
                {
                    Assert.Equal("statuses" + acquisition, fields["pzuiactionzzz"]);
                    Assert.Equal("PC027", fields["$PpyDisplayHarness$pProductIds"]);
                    Assert.Equal(expectedCode, fields["$PpyDisplayHarness$pSetProfessionType"]);
                    for (var i = 1; i <= 4; i++)
                        Assert.Equal("true", fields["$PpyDisplayHarness$pEligibleStatusforRoster$l" + i + "$pSelectStatus"]);
                    response.Content = new StringContent(Roster(acquisition, selected: true));
                }
                else
                {
                    Assert.Equal("download" + acquisition, fields["pzuiactionzzz"]);
                    response.Content = new StringContent("<html>Roster prepared</html>");
                }
            }
            else
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("pyPreActivity=NGLPGenerateRosterDownload", request.RequestUri.Query);
                response.Content = new ByteArrayContent(failure == "empty" ? [] : [80, 75, 3, 4]);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(failure == "html" ? "text/html"
                    : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                if (failure == "incomplete") response.Content.Headers.ContentLength = 99;
            }
            if (response.Content is StringContent)
            {
                if (largeHtml || failure == "oversized-html")
                {
                    var original = await response.Content.ReadAsStringAsync(cancellationToken);
                    response.Content.Dispose();
                    var padding = failure == "oversized-html" ? new string('x', 2_000_001)
                        : string.Concat(Enumerable.Repeat("\\u0061\\u0022\\uZZZZ\\u0", 85_000));
                    response.Content = new StringContent(padding + original);
                }
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            }
            return response;
        }

        private string Bootstrap(int acquisition) =>
            $$"""
            <html><input name='XCSRFToken' value='{{(failure == "csrf" ? "" : "csrf" + acquisition)}}'>
            {"url":"{{Portal}}?pzTransactionId=t{{acquisition}}&pzFromFrame=pyDisplayHarness&pzPrimaryPageName=pyDisplayHarness","pzHarnessID":"HID{{acquisition}}"}
            {"section":"FindAProfessionalWrapper","UID":"SID1"}
            {{Action("FindAProfessionalWrapper", "UpdateLookupType,", "mode" + acquisition, mode: true)}}
            </html>
            """;
        private string Roster(int acquisition, bool selected)
        {
            var statuses = new[] { "Active", "Inactive per Request", "Expired", "Disciplined" };
            var statusData = string.Join("", statuses.Select((status, index) =>
                "{\"$InternalRoasterStatuses$pyCaption\":\"" + (failure == "statuses" && index == 0 ? "Changed" : status)
                + "\",\"SelectStatus\":\"" + (selected && !(failure == "confirmation" && index == 0) ? "true" : "false") + "\"}"));
            var url = failure == "download-url" ? "https://example.invalid/roster" : Portal;
            return $$"""
                <html>{"section":"NGLPRosterDownload","UID":"SID2","secPostValueURL":"pzuiactionzzz=post{{acquisition}}"}
                {"section":"FindAProfessionalInput","UID":"SID3"}{"section":"NGLPRosterDownloadResults","UID":"SID4"}
                {"ProductIds":"PC027","SetProfessionType":"{{expectedCode}}"}
                {{statusData}}
                {{Action("FindAProfessionalInput", "SetRosterDownloadStatuses,", "statuses" + acquisition)}}
                {{Action("NGLPRosterDownloadResults", "SetRosterDownloadApplicant", "download" + acquisition, downloadUrl: url)}}
                </html>
                """;
        }
        private static string Action(string section, string activity, string token, bool mode = false, string? downloadUrl = null)
        {
            var args = new object?[] { "otherSection", section, activity == "SetRosterDownloadApplicant" ? activity : "", "=", "",
                activity == "SetRosterDownloadApplicant" ? "," : activity, ":event", "",
                new object[] { "pzuiactionzzz=" + token, new { }, mode ? new Dictionary<string, string> { ["LookupType"] = "Roster+Download" } : new Dictionary<string, string>() },
                "pyDisplayHarness" };
            var actions = new List<object[]> { new object[] { "refresh", args } };
            if (downloadUrl is not null)
                actions.Add(["openUrlInWindow", new[] { downloadUrl + "?pyActivity=@baseclass.pzProcessURLInWindow&pyPreActivity=NGLPGenerateRosterDownload&pzPrimaryPageName=pyDisplayHarness" }]);
            return "data-click='" + JsonSerializer.Serialize(actions) + "'";
        }
    }
}
