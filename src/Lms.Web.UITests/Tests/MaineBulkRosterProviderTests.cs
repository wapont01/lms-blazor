using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Lms.Application.Services;
using Lms.Web.Services;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class MaineBulkRosterProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Downloader_UsesFreshSplitViewstateIsolatedCookiesAndUnrestrictedActiveQueries(bool company)
    {
        using var handler = new AlmsHandler(company);
        using var client = new HttpClient(handler);
        var provider = new MaineAlmsBulkRosterProvider();
        var file = provider.Files[company ? 1 : 0];
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var path = NewPath();
            try
            {
                var result = await provider.DownloadFileAsync(client, path, file, CancellationToken.None, (_, _) => { });
                Assert.Equal(new FileInfo(path).Length, result.Bytes);
                Assert.Equal(2, MaineAlmsBulkImportService.CheckFile(path, file.Name));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        Assert.Equal(10, handler.Calls);
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("missing-viewstate")]
    [InlineData("split-viewstate")]
    [InlineData("missing-active")]
    [InlineData("count")]
    [InlineData("export-link")]
    [InlineData("html")]
    [InlineData("length")]
    [InlineData("too-large")]
    [InlineData("rows")]
    [InlineData("status")]
    [InlineData("automatic-redirect")]
    public async Task Downloader_RejectsWorkflowAndEvidenceFailuresWithoutLeavingFiles(string failure)
    {
        using var handler = new AlmsHandler(false, failure);
        using var client = new HttpClient(handler);
        var provider = new MaineAlmsBulkRosterProvider();
        var path = NewPath();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.DownloadFileAsync(client, path, provider.Files[0],
                CancellationToken.None, (_, _) => { }));
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string NewPath() => Path.Combine(Directory.GetCurrentDirectory(), ".maine-download-" + Guid.NewGuid().ToString("N"));

    private sealed class AlmsHandler(bool company, string? failure = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = Calls++ % 5;
            var session = (Calls - 1) / 5 + 1;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            var search = MaineAlmsBulkImportService.SearchUrl(company);
            if (step == 0)
            {
                Assert.Equal(search, request.RequestUri!.AbsoluteUri);
                Assert.False(request.Headers.Contains("Cookie"));
                response.StatusCode = HttpStatusCode.Redirect;
                response.Headers.Location = new Uri(failure == "redirect" ? "https://example.invalid/steal" : search + "?AspxAutoDetectCookieSupport=1");
                response.Headers.Add("Set-Cookie", $"ASP.NET_SessionId=session{session}; Path=/; Secure; HttpOnly");
                if (failure == "automatic-redirect") response.RequestMessage = new(HttpMethod.Get, "https://example.invalid/");
            }
            else
            {
                Assert.Equal($"ASP.NET_SessionId=session{session}", Assert.Single(request.Headers.GetValues("Cookie")));
                if (step == 1)
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    var prefix = "ctl00$ctl00$mainContent$mainContent$";
                    var active = company ? "scOnlyActive" : "scActiveOnly";
                    response.Content = new StringContent(
                        (failure == "missing-viewstate" ? "" : $"<input value='fresh&amp;{session}' name='__VIEWSTATE' type='hidden'>")
                        + "<input type='hidden' name='__VIEWSTATEFIELDCOUNT' value='2'>"
                        + (failure == "split-viewstate" ? "" : "<input type='hidden' name='__VIEWSTATE1' value='split&amp;state'>")
                        + "<input type='hidden' name='__EVENTVALIDATION' value='event&amp;validation'>"
                        + "<input type='hidden' name='__EVENTTARGET' value=''>"
                        + $"<select name='{prefix}scRegulator'></select>"
                        + (failure == "missing-active" ? "" : $"<input name='{prefix}{active}' type='checkbox'>")
                        + $"<input name='{prefix}btnSearch' type='submit' value='Search'>");
                }
                else if (step == 2)
                {
                    Assert.Equal(HttpMethod.Post, request.Method);
                    var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    var fields = body.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair =>
                        WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
                    Assert.Equal($"fresh&{session}", fields["__VIEWSTATE"]);
                    Assert.Equal("split&state", fields["__VIEWSTATE1"]);
                    Assert.Equal("event&validation", fields["__EVENTVALIDATION"]);
                    Assert.Equal("4060", fields["ctl00$ctl00$mainContent$mainContent$scRegulator"]);
                    Assert.Equal("on", fields["ctl00$ctl00$mainContent$mainContent$" + (company ? "scOnlyActive" : "scActiveOnly")]);
                    Assert.Equal(8, fields.Count);
                    response.StatusCode = HttpStatusCode.Redirect;
                    response.Headers.Location = new Uri("/ALMSOnline/ALMSQuery/SearchResults.aspx", UriKind.Relative);
                }
                else if (step == 3)
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.EndsWith("SearchResults.aspx", request.RequestUri!.AbsoluteUri);
                    response.Content = new StringContent((failure == "count" ? "No count" : failure == "rows" ? "3 records found." : "2 records found.")
                        + (failure == "export-link" ? "" : "<a href='ExportToCSV.aspx'>Export to CSV</a>"));
                }
                else
                {
                    Assert.Equal(MaineAlmsBulkImportService.ExportUrl, request.RequestUri!.AbsoluteUri);
                    var rows = Enumerable.Range(0, 2).Select(index =>
                    {
                        var row = Enumerable.Repeat("", 40).ToArray();
                        row[0] = row[1] = index == 0 ? "Published Name" : "Published Alias";
                        row[2] = company ? "AL123" : "SA" + index;
                        row[3] = company ? "LIMITED LIABILITY" : "SALES AGENT";
                        row[4] = failure == "status" ? "INACTIVE" : "ACTIVE";
                        row[37] = "REAL ESTATE COMMISSION";
                        return row;
                    });
                    var csv = string.Join("\r\n", new[] { MaineAlmsBulkImportService.Headers }.Concat(rows)
                        .Select(row => string.Join(",", row.Select(value => "\"" + value + "\"")))) + "\r\n";
                    response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue(failure == "html" ? "text/html" : "application/octet-stream");
                    if (failure == "length") response.Content.Headers.ContentLength = 1;
                    if (failure == "too-large") response.Content.Headers.ContentLength = 101L * 1024 * 1024;
                }
            }
            return response;
        }
    }
}
