using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lms.Application.Services;
using Lms.Web.Services;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class WyomingWrecBulkRosterProviderTests
{
    [Theory]
    [InlineData("individuals.json", "")]
    [InlineData("firms.json", "2")]
    [InlineData("amc.json", "1")]
    public async Task Downloader_UsesAllPublishedAnonymousQueriesAndPreservesTwoCompleteOriginalPagingPasses(string name, string providerId)
    {
        using var handler = new WrecHandler(name);
        using var client = new HttpClient(handler);
        var provider = new WyomingWrecBulkRosterProvider();
        var definition = provider.Files.Single(file => file.Name == name);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var path = NewPath();
            try
            {
                var result = await provider.DownloadFileAsync(client, path, definition, CancellationToken.None, (_, _) => { });
                Assert.Equal(new FileInfo(path).Length, result.Bytes);
                Assert.Null(result.SourceETag);
                Assert.Null(result.SourceLastModifiedUtc);
                Assert.Equal(251, await provider.CheckFileAsync(path, definition, CancellationToken.None));
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                Assert.Equal(WyomingWrecBulkImportService.ArchiveFormat, root.GetProperty("format").GetString());
                Assert.Equal(providerId, root.GetProperty("query").GetProperty("ProviderTypeId").GetString());
                Assert.Equal(2, root.GetProperty("pages").GetArrayLength());
                Assert.Equal(2, root.GetProperty("verification_pages").GetArrayLength());
                foreach (var page in root.GetProperty("pages").EnumerateArray().Concat(root.GetProperty("verification_pages").EnumerateArray()))
                {
                    Assert.Equal(TimeSpan.Zero, page.GetProperty("captured_at_utc").GetDateTimeOffset().Offset);
                    var original = page.GetProperty("response_json").GetString()!;
                    Assert.StartsWith("{ \"TotalRecord\"", original);
                    using var response = JsonDocument.Parse(original);
                    Assert.True(response.RootElement.GetProperty("Status").GetBoolean());
                    Assert.Equal("00", response.RootElement.GetProperty("StatusCode").GetString());
                    Assert.Equal("Published envelope evidence", response.RootElement.GetProperty("AdditionalInformation").GetString());
                }
                var records = WyomingWrecBulkImportService.ReadRecords(path, name);
                Assert.Equal(251, records.Count);
                Assert.All(records, row => Assert.NotNull(row.CapturedAtUtc));
                Assert.Equal(1, records[0].Page);
                Assert.Equal(2, records[^1].Page);
                Assert.Equal(251, records[^1].Raw.GetProperty("TotalRecord").GetInt32());
                Assert.Equal(name == WyomingWrecBulkImportService.IndividualFile ? "Responsible Broker" :
                    name == WyomingWrecBulkImportService.FirmFile ? "Real Estate License" : "Appraisal Management Company",
                    records[0].LicenseType);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        Assert.Equal(8, handler.Calls);
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("automatic-redirect")]
    [InlineData("http")]
    [InlineData("media")]
    [InlineData("length")]
    [InlineData("too-large")]
    [InlineData("json")]
    [InlineData("status")]
    [InlineData("status-code")]
    [InlineData("total")]
    [InlineData("missing-list")]
    [InlineData("short-page")]
    [InlineData("empty-page")]
    [InlineData("row-total")]
    [InlineData("schema")]
    [InlineData("duplicate")]
    [InlineData("overlap")]
    [InlineData("page-count")]
    [InlineData("verification-count")]
    [InlineData("verification-content")]
    [InlineData("verification-identity")]
    public async Task Downloader_RejectsPartialDuplicateConflictingUnstableOrNonOfficialResponsesWithoutLeavingFiles(string failure)
    {
        using var handler = new WrecHandler(WyomingWrecBulkImportService.IndividualFile, failure);
        using var client = new HttpClient(handler);
        var provider = new WyomingWrecBulkRosterProvider();
        var path = NewPath();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.DownloadFileAsync(client, path, provider.Files[0],
                CancellationToken.None, (_, _) => { }));
            Assert.False(File.Exists(path));
            Assert.Equal(failure switch
            {
                "empty-page" or "overlap" or "page-count" => 2,
                "verification-count" or "verification-content" or "verification-identity" => 4,
                _ => 1
            }, handler.Calls);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("query")]
    [InlineData("url")]
    [InlineData("capture")]
    [InlineData("old-verification")]
    [InlineData("missing-page")]
    [InlineData("missing-verification")]
    [InlineData("extra-page")]
    [InlineData("reordered-pages")]
    [InlineData("response-content")]
    public async Task PagedReader_RejectsArchiveTamperingAndRequiresExactPublishedQueryCoverage(string change)
    {
        using var handler = new WrecHandler(WyomingWrecBulkImportService.IndividualFile);
        using var client = new HttpClient(handler);
        var provider = new WyomingWrecBulkRosterProvider();
        var path = NewPath();
        try
        {
            await provider.DownloadFileAsync(client, path, provider.Files[0], CancellationToken.None, (_, _) => { });
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var pages = root["pages"]!.AsArray();
            var verification = root["verification_pages"]!.AsArray();
            switch (change)
            {
                case "query": root["query"]!["IsIndividualSearch"] = false; break;
                case "url": pages[0]!["url"] = "https://example.invalid/"; break;
                case "capture": pages[0]!["captured_at_utc"] = "not a timestamp"; break;
                case "old-verification": verification[0]!["captured_at_utc"] = "2000-01-01T00:00:00Z"; break;
                case "missing-page": pages.RemoveAt(1); break;
                case "missing-verification": root.Remove("verification_pages"); break;
                case "extra-page": pages.Add(pages[1]!.DeepClone()); break;
                case "reordered-pages":
                    var first = pages[0]!.DeepClone(); pages[0] = pages[1]!.DeepClone(); pages[1] = first; break;
                case "response-content":
                    var response = JsonNode.Parse(verification[0]!["response_json"]!.GetValue<string>())!;
                    response["IndividualProviderVerifySearchList"]![0]!["LastName"] = "Changed";
                    verification[0]!["response_json"] = response.ToJsonString(); break;
            }
            File.WriteAllText(path, root.ToJsonString());
            Assert.Throws<InvalidDataException>(() => WyomingWrecBulkImportService.CheckFile(path, provider.Files[0].Name));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Downloader_DoesNotOverwriteOrDeleteAnExistingArchive()
    {
        using var handler = new WrecHandler(WyomingWrecBulkImportService.IndividualFile);
        using var client = new HttpClient(handler);
        var provider = new WyomingWrecBulkRosterProvider();
        var path = NewPath();
        File.WriteAllText(path, "Existing archive");
        try
        {
            await Assert.ThrowsAsync<IOException>(() => provider.DownloadFileAsync(client, path, provider.Files[0],
                CancellationToken.None, (_, _) => { }));
            Assert.Equal("Existing archive", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Downloader_CancellationAndAnonymousClientGuardsLeaveNoArtifacts()
    {
        using var handler = new WrecHandler(WyomingWrecBulkImportService.IndividualFile);
        using var client = new HttpClient(handler);
        var provider = new WyomingWrecBulkRosterProvider();
        var path = NewPath();
        using var cancellation = new CancellationTokenSource();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.DownloadFileAsync(client, path,
                provider.Files[0], cancellation.Token, (_, _) => cancellation.Cancel()));
            Assert.False(File.Exists(path));
            Assert.Equal(1, handler.Calls);
            client.DefaultRequestHeaders.Add("Cookie", "Not anonymous");
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.DownloadFileAsync(client, path,
                provider.Files[0], CancellationToken.None, (_, _) => { }));
            Assert.False(File.Exists(path));
            Assert.Equal(1, handler.Calls);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string NewPath() => Path.Combine(Directory.GetCurrentDirectory(), ".wyoming-download-" + Guid.NewGuid().ToString("N"));

    private sealed class WrecHandler(string file, string? failure = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Calls++ % 4;
            var verification = call >= 2;
            var page = call % 2 + 1;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(WyomingWrecBulkImportService.PageUrl(page, 250), request.RequestUri!.AbsoluteUri);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            var expected = new Dictionary<string, object>
            {
                ["LastName"] = "", ["FirstName"] = "", ["LicenseNumber"] = "",
                ["IsIndividualSearch"] = file == "individuals.json", ["IsFirmSearch"] = file != "individuals.json",
                ["State"] = "", ["Zip"] = "", ["LicenseTypeId"] = "", ["CorporationName"] = "",
                ["City"] = "", ["StreetAddress"] = "", ["ProviderTypeId"] = file == "firms.json" ? "2" : file == "amc.json" ? "1" : "",
                ["SortOrder"] = "asc", ["SortType"] = "LicenseNumber", ["RemoveRelationshipsForLicenseTypes"] = Array.Empty<string>()
            };
            Assert.Equal(15, payload.RootElement.EnumerateObject().Count());
            Assert.Equal(WyomingWrecBulkImportService.CanonicalJson(JsonSerializer.SerializeToElement(expected)),
                WyomingWrecBulkImportService.CanonicalJson(payload.RootElement));
            Assert.Equal("asc", payload.RootElement.GetProperty("SortOrder").GetString());
            Assert.Equal("LicenseNumber", payload.RootElement.GetProperty("SortType").GetString());
            Assert.Empty(payload.RootElement.GetProperty("RemoveRelationshipsForLicenseTypes").EnumerateArray());
            var total = failure == "verification-count" && verification || failure == "page-count" && page == 2 ? 252 : 251;
            var offset = (page - 1) * 250;
            var rows = Enumerable.Range(offset, Math.Min(250, total - offset)).Select(index => Record(index, total)).ToList();
            if (failure == "duplicate" && page == 1) rows[1] = rows[0];
            if (failure == "overlap" && page == 2) rows[0] = Record(0, total);
            if (failure == "short-page" && page == 1) rows.RemoveAt(0);
            if (failure == "empty-page" && page == 2) rows.Clear();
            if (failure == "row-total") rows[0]["TotalRecord"] = 0;
            if (failure == "schema") rows[0].Remove("Zip");
            if (failure == "verification-content" && verification) rows[0]["LastName"] = "Changed";
            if (failure == "verification-identity" && verification) rows[0]["IndividualId"] = 999999L;
            var envelope = new Dictionary<string, object?>
            {
                ["TotalRecord"] = failure == "total" ? 10001 : total,
                ["IndividualProviderVerifySearchList"] = rows, ["Status"] = failure != "status",
                ["StatusCode"] = failure == "status-code" ? "99" : "00", ["Message"] = "",
                ["AdditionalInformation"] = "Published envelope evidence"
            };
            if (failure == "missing-list") envelope.Remove("IndividualProviderVerifySearchList");
            var json = failure == "json" ? "{not json" : JsonSerializer.Serialize(envelope).Replace("{\"TotalRecord\"", "{ \"TotalRecord\"", StringComparison.Ordinal);
            var response = new HttpResponseMessage(failure == "redirect" ? HttpStatusCode.Redirect :
                failure == "http" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            {
                RequestMessage = failure == "automatic-redirect" ? new(HttpMethod.Get, "https://example.invalid/") : request,
                Content = new StringContent(json, new UTF8Encoding(false), failure == "media" ? "text/html" : "application/json")
            };
            if (failure == "redirect") response.Headers.Location = new Uri("https://example.invalid/");
            if (failure == "length") response.Content.Headers.ContentLength = 1;
            if (failure == "too-large") response.Content.Headers.ContentLength = WyomingWrecBulkRosterProvider.MaxResponseBytes + 1;
            return response;
        }
        private Dictionary<string, object?> Record(int index, int total)
        {
            var individual = file == WyomingWrecBulkImportService.IndividualFile;
            var type = individual ? 3 : file == WyomingWrecBulkImportService.FirmFile ? 2 : 1;
            var row = WyomingWrecBulkImportService.Headers.ToDictionary(key => key, _ => (object?)null);
            row["LicenseNumber"] = index.ToString("D5"); row["IndividualId"] = 100000L + index;
            row["ProviderId"] = individual ? 0L : 100000L + index;
            row["LicenseTypeId"] = type;
            row["LicenseTypeName"] = individual ? "Responsible Broker License" :
                file == WyomingWrecBulkImportService.FirmFile ? "Real Estate License" : "Appraisal Management Company";
            row["FirstName"] = "Jane"; row["LastName"] = "Smith"; row["CorporationName"] = "Published Realty";
            row["LicenseStatusTypeName"] = "Active"; row["IsActive"] = false;
            row["TotalRecord"] = total; row["EntityRelationList"] = Array.Empty<object>();
            return row;
        }
    }
}
