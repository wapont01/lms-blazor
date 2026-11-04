using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Lms.Application.Services;
using Lms.Web.Services;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class IdahoDoplBulkRosterProviderTests
{
    [Fact]
    public async Task Downloader_UsesIsolatedCookiesFreshFastStateAndAnonymousUnrestrictedAllTypesQuery()
    {
        using var handler = new FastHandler();
        using var client = new HttpClient(handler);
        var provider = new IdahoDoplBulkRosterProvider();
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var path = NewPath();
            try
            {
                var result = await provider.DownloadFileAsync(client, path, provider.Files[0], CancellationToken.None, (_, _) => { });
                Assert.Equal(new FileInfo(path).Length, result.Bytes);
                Assert.Equal(3, IdahoDoplBulkImportService.CheckFile(path));
                Assert.Equal(1, IdahoDoplBulkImportService.ReadRecords(path).Count(IdahoDoplBulkImportService.IsEvidence));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        Assert.Equal(18, handler.Calls);
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("redirect-loop")]
    [InlineData("automatic-redirect")]
    [InlineData("missing-version")]
    [InlineData("missing-hidden")]
    [InlineData("duplicate-hidden")]
    [InlineData("missing-field")]
    [InlineData("stale-state")]
    [InlineData("wrong-board")]
    [InlineData("changed-types")]
    [InlineData("count")]
    [InlineData("wrong-grid")]
    [InlineData("wrong-caption")]
    [InlineData("zero-count")]
    [InlineData("rows")]
    [InlineData("export-action")]
    [InlineData("export-url")]
    [InlineData("export-token")]
    [InlineData("html")]
    [InlineData("signature")]
    [InlineData("length")]
    [InlineData("too-large")]
    [InlineData("schema")]
    [InlineData("type")]
    [InlineData("status")]
    [InlineData("truncated")]
    public async Task Downloader_RejectsChangedWorkflowOrIncompleteEvidenceAndDeletesOnlyItsOwnFile(string failure)
    {
        using var handler = new FastHandler(failure);
        using var client = new HttpClient(handler);
        var provider = new IdahoDoplBulkRosterProvider();
        var path = NewPath();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.DownloadFileAsync(client, path, provider.Files[0],
                CancellationToken.None, (_, _) => { }));
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Downloader_DoesNotOverwriteExistingArchiveFiles()
    {
        using var handler = new FastHandler();
        using var client = new HttpClient(handler);
        var provider = new IdahoDoplBulkRosterProvider();
        var path = NewPath();
        await File.WriteAllTextAsync(path, "existing evidence");
        try
        {
            await Assert.ThrowsAsync<IOException>(() => provider.DownloadFileAsync(client, path, provider.Files[0],
                CancellationToken.None, (_, _) => { }));
            Assert.Equal("existing evidence", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    private static string NewPath() => Path.Combine(Directory.GetCurrentDirectory(), ".idaho-download-" + Guid.NewGuid().ToString("N"));

    private sealed class FastHandler(string? failure = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = Calls++ % 9;
            var session = (Calls - 1) / 9 + 1;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            var landing = "https://edopl.idaho.gov/OnlineServices/";
            var workflow = landing + "_/";
            if (failure == "redirect-loop")
            {
                response.StatusCode = HttpStatusCode.Redirect;
                response.Headers.Location = request.RequestUri;
                return response;
            }
            if (step == 0)
            {
                Assert.Equal(landing, request.RequestUri!.AbsoluteUri);
                Assert.False(request.Headers.Contains("Cookie"));
                response.StatusCode = HttpStatusCode.Redirect;
                response.Headers.Location = new Uri(failure == "redirect" ? "https://example.invalid/steal" : landing + "?Check=1");
                response.Headers.Add("Set-Cookie", $"FASTSESSION=session{session}; Path=/OnlineServices/; Secure; HttpOnly");
                if (failure == "automatic-redirect") response.RequestMessage = new(HttpMethod.Get, "https://example.invalid/");
                return response;
            }
            Assert.Equal($"FASTSESSION=session{session}", Assert.Single(request.Headers.GetValues("Cookie")));
            if (step == 1)
            {
                Assert.Equal(landing + "?Check=1", request.RequestUri!.AbsoluteUri);
                response.StatusCode = HttpStatusCode.Redirect;
                response.Headers.Location = new Uri(workflow);
                return response;
            }
            if (step == 2)
            {
                Assert.Equal(workflow, request.RequestUri!.AbsoluteUri);
                response.Content = new StringContent(failure == "missing-version" ? "Changed landing" :
                    $"<script>FWDC.setVerLast('v0&amp;{session}', 'landing&amp;{session}');</script>");
                return response;
            }
            Dictionary<string, string> fields;
            if (step == 3)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                fields = Decode(request.RequestUri!.Query.TrimStart('?'));
                Assert.Equal("1", fields["Load"]);
            }
            else if (step < 8)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                fields = Decode(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("0", fields["DOC_MODAL_ID__"]);
                Assert.Equal($"hidden&{session}", fields["SESSION_STATE__"]);
                if (step == 4)
                {
                    Assert.Equal(workflow + "EventOccurred", request.RequestUri!.AbsoluteUri);
                    Assert.Equal("Df-3-2", fields["EVENT__"]);
                    Assert.Equal("", fields["Dd-a"]);
                    Assert.Equal("", fields["Dd-b"]);
                    Assert.False(fields.ContainsKey("ACTION_ID__"));
                }
                else
                {
                    Assert.Equal("SingleSearch", fields["Dd-b"]);
                    Assert.Equal("BORECM", fields["Dd-f"]);
                    Assert.Equal("LICVLD", fields["Dd-v"]);
                    foreach (var name in new[] { "Dd-g", "Dd-w", "Dd-x", "Dd-y", "Dd-z", "Dd-01", "Dd-11", "Dd-21", "Dd-31", "Dd-41" })
                        Assert.Equal("", fields[name]);
                    if (step == 5)
                    {
                        Assert.Equal(workflow + "Recalc", request.RequestUri!.AbsoluteUri);
                        Assert.Equal("Dd-f", fields["LASTFOCUSFIELD__"]);
                    }
                    else if (step == 6)
                    {
                        Assert.Equal(workflow + "ExecuteAction", request.RequestUri!.AbsoluteUri);
                        Assert.Equal("1", fields["ACTION_ID__"]);
                        Assert.Equal("CTLSTD", fields["TYPE__"]);
                    }
                    else
                    {
                        Assert.Equal(workflow + "EventOccurred", request.RequestUri!.AbsoluteUri);
                        Assert.Equal("Df-t", fields["EVENT__"]);
                        Assert.False(fields.ContainsKey("ACTION_ID__"));
                    }
                }
            }
            else
            {
                Assert.Equal(workflow + $"ExportView?Doc=Df&UID=uidfresh{session}&Format=Token&Token=tokenfresh{session}", request.RequestUri!.AbsoluteUri);
                var bytes = Ods(failure);
                response.Content = new ByteArrayContent(failure == "truncated" ? bytes[..(bytes.Length / 2)] : bytes);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(failure == "html" ? "text/html" : "application/vnd.oasis.opendocument.spreadsheet");
                if (failure == "length") response.Content.Headers.ContentLength = bytes.Length + 1;
                if (failure == "too-large") response.Content.Headers.ContentLength = 101L * 1024 * 1024;
                return response;
            }
            Assert.Equal("1", fields["FAST_SCRIPT_VER__"]);
            Assert.Equal($"v{step - 3}&{session}", fields["FAST_VERLAST__"]);
            Assert.Equal(step == 3 ? $"landing&{session}" : $"source{step - 3}&{session}", fields["FAST_VERLAST_SOURCE__"]);
            response.Headers.Add("Fast-Ver-Last", $"v{(failure == "stale-state" ? step - 3 : step - 2)}&{session}");
            response.Headers.Add("Fast-Ver-Source", $"source{step - 2}&{session}");
            var hidden = $"<input type='hidden' name='LASTFOCUSFIELD__' value=''><input type='hidden' name='DOC_MODAL_ID__' value='0'>"
                + $"<input type='hidden' name='SESSION_STATE__' value='hidden&amp;{session}'>";
            if (failure == "missing-hidden") hidden = "";
            if (failure == "duplicate-hidden") hidden += "<input type='hidden' name='DOC_MODAL_ID__' value='0'>";
            object json;
            if (step == 3)
                json = new { contents = new[] { new { manager = hidden
                    + "<input name='Dd-a'><input name='Dd-b'><input name='Df-f'>"
                    + "<a title='Search for a Bulk List of Licenses' data-event='LinkClick' data-linkid='Df-3-2'></a>" } } };
            else if (step == 4)
            {
                var names = new[] { "Dd-b", "Dd-f", "Dd-g", "Dd-v", "Dd-w", "Dd-x", "Dd-y", "Dd-z", "Dd-01", "Dd-11", "Dd-21", "Dd-31", "Dd-41" };
                json = new { html = "Public License List Search BORECM LICVLD " + hidden
                    + string.Concat(names.Where(name => failure != "missing-field" || name != "Dd-y").Select(name => $"<input name='{name}'>")) };
            }
            else if (step == 5)
            {
                var types = new[] { "", "Real Estate Broker License", "Real Estate Salesperson License", "Real Estate Cooperative Broker License",
                    IdahoDoplBulkImportService.CompanyType, IdahoDoplBulkImportService.BranchType,
                    "Real Estate Instructor Certification License", "Real Estate Provider Certification License", "Timeshare Registration" };
                if (failure == "changed-types") types[1] = "Real Estate Appraiser License";
                json = new { Updates = new { FieldUpdates = new object[]
                {
                    new { field = "Dd-f", value = failure == "wrong-board" ? "BOREAP" : "BORECM", text = "Real Estate Commission" },
                    new { field = "Dd-g", value = "", comboitems = types.Select(label => new { label }) }
                } } };
            }
            else if (step == 6)
                json = new { html = "List Search Results <span id='caption2_Df-s'>List Search Results</span>"
                    + "<table id='Dd-o' data-fast-rowcount='0'></table>" + (failure == "count" ? "" :
                    $"<table id='{(failure == "wrong-grid" ? "Df-s" : "Df-r")}' aria-labelledby='{(failure == "wrong-caption" ? "caption2_other" : "caption2_Df-s")}' data-fast-rowcount='{(failure == "rows" ? "4" : failure == "zero-count" ? "0" : "3")}'></table>")
                    + (failure == "export-action" ? "" : "<a id='cl_Df-t'>Export</a>") };
            else
                json = new { postFunctions = new[]
                {
                    new { name = "OpenWindow", parameters = new[] { failure == "export-url" ? "https://example.invalid/steal" :
                        $"ExportView?Doc=Df&UID=uidfresh{session}&Format=Token&Token={(failure == "export-token" ? "" : "tokenfresh" + session)}" } }
                } };
            response.Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
            return response;
        }

        private static Dictionary<string, string> Decode(string body) => body.Split('&').Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));

        private static byte[] Ods(string? failure)
        {
            if (failure == "signature") return Encoding.UTF8.GetBytes("not an ODS package");
            var headers = (string[])IdahoDoplBulkImportService.Headers.Clone();
            if (failure == "schema") headers[0] = "Profession";
            var rows = Enumerable.Range(0, 3).Select(index =>
            {
                var row = Enumerable.Repeat("", 23).ToArray();
                row[0] = index == 2 ? "Real Estate Instructor Certification License" : "Real Estate Salesperson License";
                row[1] = index == 2 ? "I-4449" : "SP" + index;
                row[2] = "Smith"; row[4] = "Jane"; row[6] = "School";
                row[8] = failure == "status" ? "Inactive" : "Active";
                row[11] = "No";
                if (failure == "type") row[0] = "Real Estate Appraiser License";
                return row;
            });
            XNamespace table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
            XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
            XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
            var document = new XDocument(new XElement(office + "document-content", new XElement(office + "body",
                new XElement(office + "spreadsheet", new XElement(table + "table", new[] { headers }.Concat(rows)
                    .Select(row => new XElement(table + "table-row", row.Select(value =>
                        new XElement(table + "table-cell", new XAttribute(office + "value-type", "string"), new XElement(text + "p", value))))))))));
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(archive.CreateEntry("content.xml").Open(), new UTF8Encoding(false)))
                writer.Write(document.ToString());
            return buffer.ToArray();
        }
    }
}
