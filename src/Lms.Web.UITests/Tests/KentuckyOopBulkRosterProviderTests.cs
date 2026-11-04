using System.Net;
using System.Text;
using Lms.Application.Services;
using Lms.Web.Services;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class KentuckyOopBulkRosterProviderTests
{
    [Theory]
    [InlineData(KentuckyOopBulkImportService.BrokerFile)]
    [InlineData(KentuckyOopBulkImportService.SalesAssociateFile)]
    [InlineData(KentuckyOopBulkImportService.RegisteredFirmBranchFile)]
    public async Task Downloader_UsesFreshStateForEachPostbackAndRotatingIsolatedCookiesAndPreservesRawHtml(string fileName)
    {
        using var handler = new DirectoryHandler();
        using var client = new HttpClient(handler);
        var provider = new KentuckyOopBulkRosterProvider();
        var file = provider.Files.Single(file => file.Name == fileName);
        var path = TestPath();
        try
        {
            var result = await provider.DownloadFileAsync(client, path, file, CancellationToken.None, (_, _) => { });
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(Encoding.UTF8.GetBytes(handler.LastExport!), bytes);
            Assert.Equal(bytes.Length, result.Bytes);
            Assert.Equal(4, handler.Calls);
            Assert.Equal(1, handler.Sessions);
            Assert.Equal(1, await provider.CheckFileAsync(path, file, CancellationToken.None));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Downloader_StartsNewCookieSessionForEveryTypeAndSupportsOfficialCsvIfFormatChanges()
    {
        using var handler = new DirectoryHandler("csv");
        using var client = new HttpClient(handler);
        var provider = new KentuckyOopBulkRosterProvider();
        var path = TestPath();
        try
        {
            foreach (var file in provider.Files)
            {
                await provider.DownloadFileAsync(client, path, file, CancellationToken.None, (_, _) => { });
                Assert.Equal(1, await provider.CheckFileAsync(path, file, CancellationToken.None));
                File.Delete(path);
            }
            Assert.Equal(3, handler.Sessions);
            Assert.Equal(12, handler.Calls);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("missing-state")]
    [InlineData("missing-split-state")]
    [InlineData("wrong-board")]
    [InlineData("wrong-types")]
    [InlineData("wrong-region")]
    [InlineData("restricted-query")]
    [InlineData("restricted-county")]
    [InlineData("wrong-action")]
    [InlineData("missing-count")]
    [InlineData("zero-count")]
    [InlineData("count-mismatch")]
    [InlineData("missing-export")]
    [InlineData("html-login")]
    [InlineData("truncated-html")]
    [InlineData("truncated-length")]
    [InlineData("oversize")]
    [InlineData("cross-origin")]
    [InlineData("redirect-loop")]
    [InlineData("automatic-redirect")]
    [InlineData("wrong-schema")]
    [InlineData("wrong-status")]
    [InlineData("wrong-type")]
    [InlineData("conflicting-duplicate")]
    public async Task Downloader_RejectsUnverifiedWorkflowAndPartialOrUnexpectedExportsAndDeletesCreatedFiles(string failure)
    {
        using var handler = new DirectoryHandler(failure);
        using var client = new HttpClient(handler);
        var provider = new KentuckyOopBulkRosterProvider();
        var path = TestPath();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.DownloadFileAsync(client, path,
                provider.Files[0], CancellationToken.None, (_, _) => { }));
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Downloader_DoesNotBypassAccessDenial()
    {
        using var handler = new DirectoryHandler("forbidden");
        using var client = new HttpClient(handler);
        var provider = new KentuckyOopBulkRosterProvider();
        var path = TestPath();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => provider.DownloadFileAsync(client, path,
            provider.Files[0], CancellationToken.None, (_, _) => { }));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal(1, handler.Calls);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Downloader_FollowsOnlyBoundedExplicitOfficialCookieRedirects()
    {
        using var handler = new DirectoryHandler("official-redirect");
        using var client = new HttpClient(handler);
        var provider = new KentuckyOopBulkRosterProvider();
        var path = TestPath();
        try
        {
            await provider.DownloadFileAsync(client, path, provider.Files[0], CancellationToken.None, (_, _) => { });
            Assert.Equal(5, handler.Calls);
            Assert.True(handler.RedirectCookieSeen);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Downloader_NeverOverwritesOrDeletesAnExistingDestination()
    {
        using var handler = new DirectoryHandler();
        using var client = new HttpClient(handler);
        var provider = new KentuckyOopBulkRosterProvider();
        var path = TestPath();
        await File.WriteAllTextAsync(path, "existing evidence");
        try
        {
            await Assert.ThrowsAsync<IOException>(() => provider.DownloadFileAsync(client, path,
                provider.Files[0], CancellationToken.None, (_, _) => { }));
            Assert.Equal("existing evidence", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    private static string TestPath() =>
        Path.Combine(Directory.GetCurrentDirectory(), ".kentucky-download-" + Guid.NewGuid().ToString("N") + ".xls");

    private sealed class DirectoryHandler(string? failure = null) : HttpMessageHandler
    {
        private const string Prefix = "ctl00$ContentPlaceHolder2$";
        private int step;
        private bool redirected;
        private string typeValue = "101";
        public int Calls { get; private set; }
        public int Sessions { get; private set; }
        public string? LastExport { get; private set; }
        public bool RedirectCookieSeen { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("oop.ky.gov", request.RequestUri!.Host);
            Assert.Equal("/active_lic_dir.aspx", request.RequestUri.AbsolutePath);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            if (failure == "forbidden") { response.StatusCode = HttpStatusCode.Forbidden; return response; }
            if (failure == "automatic-redirect")
            {
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://oop.ky.gov/unexpected.aspx");
                response.Content = new StringContent("unexpected");
                return response;
            }
            if (failure is "cross-origin" or "redirect-loop" || failure == "official-redirect" && !redirected)
            {
                redirected = true;
                response.StatusCode = HttpStatusCode.Redirect;
                response.Headers.Location = new Uri(failure == "cross-origin" ? "https://example.invalid/active_lic_dir.aspx"
                    : KentuckyOopBulkImportService.SourcePage + "?AspxAutoDetectCookieSupport=1");
                response.Headers.Add("Set-Cookie", "redirect=verified; Path=/; Secure; HttpOnly");
                return response;
            }
            step++;
            if (step == 1)
            {
                Sessions++;
                Assert.Equal(HttpMethod.Get, request.Method);
                if (failure == "official-redirect")
                {
                    Assert.Equal("redirect=verified", Assert.Single(request.Headers.GetValues("Cookie")));
                    RedirectCookieSeen = true;
                }
                else Assert.False(request.Headers.Contains("Cookie"));
            }
            else
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                var cookie = Assert.Single(request.Headers.GetValues("Cookie"));
                Assert.Contains($"ASP.NET_SessionId=session{Sessions}-{step - 1}", cookie, StringComparison.Ordinal);
                var form = ParseForm(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal($"viewstate-{Sessions}-{step - 1}", form["__VIEWSTATE"]);
                Assert.Equal($"split-{Sessions}-{step - 1}", form["__VIEWSTATE1"]);
                Assert.Equal($"validation-{Sessions}-{step - 1}", form["__EVENTVALIDATION"]);
                Assert.Equal("31", form[Prefix + "ddlBoard"]);
                Assert.Equal("0", form[Prefix + "ddlRegion"]);
                Assert.Equal("0", form[Prefix + "ddlHours"]);
                Assert.Equal("", form[Prefix + "txtBusName"]);
                Assert.Equal("", form[Prefix + "txtBusLocation"]);
                Assert.False(form.ContainsKey(Prefix + "ddlCounty"));
                if (step < 4)
                {
                    Assert.Equal("Search", form[Prefix + "btnSearch"]);
                    Assert.False(form.ContainsKey(Prefix + "btnExport"));
                }
                else
                {
                    Assert.Equal("Export to Excel", form[Prefix + "btnExport"]);
                    Assert.False(form.ContainsKey(Prefix + "btnSearch"));
                }
                if (step >= 3) typeValue = form[Prefix + "ddlLicType"];
            }
            response.Headers.Add("Set-Cookie", $"ASP.NET_SessionId=session{Sessions}-{step}; Path=/; Secure; HttpOnly");
            if (step < 4)
            {
                response.Content = new StringContent(Page(step), Encoding.UTF8, "text/html");
                return response;
            }
            var type = typeValue switch { "101" => "Broker", "102" => "Sales Associate", _ => "Registered Firm Branch" };
            var fields = new[]
            {
                "Published, \"Name\" & Co", "Business evidence only", "Kentucky Real Estate Commission", type,
                "12345", "Active", "6/5/2008", "7/9/2026", "3/31/2027", "3/31/2028",
                "NOT PUBLIC INFORMATION", "Original business\nAddress", "Original mailing\nAddress"
            };
            if (failure == "wrong-status") fields[5] = "Inactive";
            if (failure == "wrong-type") fields[3] = "Registered Firm Branch";
            static string Row(string[] row) => "<tr>" + string.Join("", row.Select(value =>
                "<td>" + WebUtility.HtmlEncode(value).Replace("\n", "<br />") + "</td>")) + "</tr>";
            LastExport = "<table>" + Row(KentuckyOopBulkImportService.Headers) + Row(fields) + "</table>";
            if (failure == "wrong-schema") LastExport = LastExport.Replace("<td>Business Address</td>", "<td>Mailing Address</td>");
            if (failure == "conflicting-duplicate")
            {
                fields[0] = "Different Name";
                LastExport = LastExport.Replace("</table>", Row(fields) + "</table>");
            }
            if (failure == "truncated-html") LastExport = LastExport[..^8];
            if (failure == "html-login") LastExport = "<html>Sign in</html>";
            if (failure == "csv")
            {
                static string Csv(string[] row) => string.Join(",", row.Select(value => "\"" + value.Replace("\"", "\"\"") + "\""));
                LastExport = Csv(KentuckyOopBulkImportService.Headers) + "\r\n" + Csv(fields) + "\r\n";
            }
            response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(LastExport));
            response.Content.Headers.TryAddWithoutValidation("Content-Type", failure == "csv" ? "text/csv"
                : failure == "html-login" ? "text/html"
                : "application/application/vnd.openxmlformats-officedocument.spreadsheetml.sheet; charset=utf-8");
            if (failure == "truncated-length") response.Content.Headers.ContentLength = Encoding.UTF8.GetByteCount(LastExport) + 8;
            if (failure == "oversize") response.Content.Headers.ContentLength = KentuckyOopBulkImportService.MaxFileBytes + 1;
            step = 0;
            return response;
        }

        private string Page(int page)
        {
            var html = new StringBuilder("<html><form method=\"post\" action=\"./active_lic_dir.aspx\">");
            if (failure != "missing-state")
                html.Append($"<input type=\"hidden\" name=\"__VIEWSTATE\" value=\"viewstate-{Sessions}-{page}\" />");
            html.Append($"<input type=\"hidden\" name=\"__EVENTVALIDATION\" value=\"validation-{Sessions}-{page}\" />");
            html.Append("<input type=\"hidden\" name=\"__VIEWSTATEFIELDCOUNT\" value=\"2\" />");
            if (failure != "missing-split-state")
                html.Append($"<input type=\"hidden\" name=\"__VIEWSTATE1\" value=\"split-{Sessions}-{page}\" />");
            void Select(string control, params (string Value, string Text, bool Selected)[] options)
            {
                html.Append($"<select name=\"{Prefix}{control}\">");
                foreach (var (value, text, selected) in options)
                    html.Append($"<option value=\"{value}\"{(selected ? " selected=\"selected\"" : "")}>{text}</option>");
                html.Append("</select>");
            }
            Select("ddlBoard", ("0", "Select Board", page == 1), ("31",
                failure == "wrong-board" ? "Other Board" : "Kentucky Real Estate Commission", page > 1));
            Select("ddlRegion", ("7", "", page == 1), ("0", failure == "wrong-region" ? "Restricted Region" : "All Regions", page > 1));
            if (page == 1) Select("ddlLicType");
            else Select("ddlLicType", ("0", "Select License Type", page == 2),
                ("101", "Broker", page == 3 && typeValue == "101"),
                ("102", failure == "wrong-types" ? "Appraiser" : "Sales Associate", page == 3 && typeValue == "102"),
                ("103", "Registered Firm Branch", page == 3 && typeValue == "103"));
            if (failure == "restricted-county") Select("ddlCounty", ("1", "Restricted County", true));
            else Select("ddlCounty");
            Select("ddlHours", ("0", "Select Day", true));
            html.Append($"<input type=\"text\" name=\"{Prefix}txtBusName\" value=\"{(failure == "restricted-query" && page == 3 ? "restricted" : "")}\" />");
            html.Append($"<input type=\"text\" name=\"{Prefix}txtBusLocation\" value=\"\" />");
            html.Append($"<input type=\"submit\" name=\"{Prefix}btnSearch\" value=\"Search\" />");
            if (page == 3)
            {
                html.Append("Kentucky Real Estate Commission Active License Directory");
                if (failure != "missing-count")
                    html.Append($"<span id=\"ContentPlaceHolder2_lblDirCount\"><b>Count : {(failure == "zero-count" ? "0" : failure == "count-mismatch" ? "2" : "1")}</b></span>");
                if (failure != "missing-export")
                    html.Append($"<input type=\"submit\" name=\"{Prefix}btnExport\" value=\"Export to Excel\" />");
            }
            html.Append("</form></html>");
            return failure == "wrong-action" ? html.ToString().Replace("./active_lic_dir.aspx", "https://example.invalid/") : html.ToString();
        }

        private static Dictionary<string, string> ParseForm(string form) =>
            form.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair =>
                Uri.UnescapeDataString(pair[0].Replace("+", " ")), pair => Uri.UnescapeDataString(pair[1].Replace("+", " ")));
    }
}
