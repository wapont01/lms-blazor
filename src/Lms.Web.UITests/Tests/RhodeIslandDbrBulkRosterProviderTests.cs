using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lms.Application.Services;
using Lms.Web.Services;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class RhodeIslandDbrBulkRosterProviderTests
{
    [Fact]
    public async Task Downloader_UsesFreshPublicTokensExactTypesUnfilteredPagingAndUntouchedResponses()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        var result = await provider.DownloadFileAsync(client, destination.Path, provider.Files[0], CancellationToken.None, (_, _) => { });
        Assert.Equal(new FileInfo(destination.Path).Length, result.Bytes);
        Assert.Equal(1001, RhodeIslandDbrBulkImportService.CheckFile(destination.Path, provider.Files[0].Name));
        using var archive = JsonDocument.Parse(await File.ReadAllTextAsync(destination.Path));
        Assert.Equal(2, archive.RootElement.GetProperty("pages").GetArrayLength());
        Assert.Equal(handler.FirstResponse, archive.RootElement.GetProperty("pages")[0].GetProperty("response").GetRawText());
        Assert.Equal(new[] { 0, 1000, 0 }, handler.Offsets);
        Assert.Equal(1, handler.TokenCalls);
        using var second = new Destination();
        await provider.DownloadFileAsync(client, second.Path, provider.Files[0], CancellationToken.None, (_, _) => { });
        Assert.Equal(2, handler.TokenCalls);
    }

    [Fact]
    public async Task Downloader_RefreshesExpiredTokenOnceAndRetriesTheSamePage()
    {
        using var handler = new Handler("stale-once");
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        await provider.DownloadFileAsync(client, destination.Path, provider.Files[0], CancellationToken.None, (_, _) => { });
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal(new[] { 0, 1000, 1000, 0 }, handler.Offsets);
    }

    [Fact]
    public async Task Downloader_DoesNotRetryUnauthorizedResponsesWithoutBound()
    {
        using var handler = new Handler("stale-always");
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.DownloadFileAsync(client, destination.Path, provider.Files[0], CancellationToken.None, (_, _) => { }));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(2, handler.TokenCalls);
        Assert.Equal(2, handler.SearchCalls);
        Assert.DoesNotContain("synthetic", error.Message);
        Assert.False(File.Exists(destination.Path));
    }

    [Theory]
    [InlineData("malformed-token")]
    [InlineData("expired-token")]
    [InlineData("expiry-schema")]
    [InlineData("malformed-json")]
    [InlineData("wrong-schema")]
    [InlineData("count-schema")]
    [InlineData("wrong-type")]
    [InlineData("changed-count")]
    [InlineData("partial")]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    [InlineData("html")]
    [InlineData("changed-final")]
    [InlineData("external-redirect")]
    [InlineData("http-redirect")]
    [InlineData("redirect-loop")]
    public async Task Downloader_RejectsMalformedPartialChangingUnsafeOrOversizedEvidence(string mode)
    {
        using var handler = new Handler(mode);
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.DownloadFileAsync(client, destination.Path, provider.Files[0], CancellationToken.None, (_, _) => { }));
        Assert.False(File.Exists(destination.Path));
    }

    [Fact]
    public async Task Downloader_AcceptsOnlyBoundedSameOfficialEndpoint307Redirects()
    {
        using var handler = new Handler("safe-redirect");
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        await provider.DownloadFileAsync(client, destination.Path, provider.Files[0], CancellationToken.None, (_, _) => { });
        Assert.True(handler.Redirected);
        Assert.Equal(1001, RhodeIslandDbrBulkImportService.CheckFile(destination.Path, provider.Files[0].Name));
    }

    [Fact]
    public async Task Downloader_CancellationRemovesPartialArchiveWithoutTouchingExistingFiles()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        using var cancellation = new CancellationTokenSource();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.DownloadFileAsync(client, destination.Path, provider.Files[0], cancellation.Token, (_, _) => cancellation.Cancel()));
        Assert.False(File.Exists(destination.Path));
        await File.WriteAllTextAsync(destination.Path, "Existing evidence");
        await Assert.ThrowsAsync<IOException>(() =>
            provider.DownloadFileAsync(client, destination.Path, provider.Files[0], CancellationToken.None, (_, _) => { }));
        Assert.Equal("Existing evidence", await File.ReadAllTextAsync(destination.Path));
    }

    [Fact]
    public async Task Downloader_CorporationsRetainPublishedBusinessDetailsWithoutInventingQualifierRelationships()
    {
        using var handler = new Handler(total: 1);
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        await provider.DownloadFileAsync(client, destination.Path, provider.Files[2], CancellationToken.None, (_, _) => { });
        var row = Assert.Single(RhodeIslandDbrBulkImportService.ReadRecords(destination.Path, provider.Files[2].Name));
        Assert.Equal("Published Realty LLC", row.Name);
        Assert.Null(row.QualifierName);
        Assert.Contains("Business Address", row.SourceRecordJson);
        Assert.Contains("\"search\"", row.SourceRecordJson);
        Assert.Equal(1, handler.DetailCalls);
        using var archive = JsonDocument.Parse(await File.ReadAllTextAsync(destination.Path));
        Assert.Single(archive.RootElement.GetProperty("details").EnumerateArray());
    }

    [Theory]
    [InlineData("detail-identity")]
    [InlineData("detail-status")]
    public async Task Downloader_RejectsChangedOrIncompleteCorporationDetails(string mode)
    {
        using var handler = new Handler(mode, total: 1);
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.DownloadFileAsync(client, destination.Path, provider.Files[2], CancellationToken.None, (_, _) => { }));
        Assert.False(File.Exists(destination.Path));
    }

    [Fact]
    public async Task Downloader_RetainsUnnamedCorporationsWithoutSubstitutingIndividualSearchTitles()
    {
        using var handler = new Handler("detail-name", total: 1);
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        await provider.DownloadFileAsync(client, destination.Path, provider.Files[2], CancellationToken.None, (_, _) => { });
        var row = Assert.Single(RhodeIslandDbrBulkImportService.ReadRecords(destination.Path, provider.Files[2].Name));
        Assert.Equal("", row.Name);
        Assert.Contains("\"title\":\"Jane Q Smith\"", row.SourceRecordJson);
    }

    [Fact]
    public async Task Downloader_RetainsCorporationsWithSeveralHistoricalBusinessAddressesAsSourceOnly()
    {
        using var handler = new Handler("detail-historical", total: 1);
        using var client = new HttpClient(handler);
        using var destination = new Destination();
        var provider = new RhodeIslandDbrBulkRosterProvider();
        await provider.DownloadFileAsync(client, destination.Path, provider.Files[2], CancellationToken.None, (_, _) => { });
        var row = Assert.Single(RhodeIslandDbrBulkImportService.ReadRecords(destination.Path, provider.Files[2].Name));
        Assert.Equal("", row.Name);
        Assert.Null(row.Address);
        Assert.Contains("Older Realty Inc.", row.SourceRecordJson);
        Assert.Contains("Published Realty LLC", row.SourceRecordJson);
    }
    [Fact]
    public void Payload_RejectsUppercaseCredentialNamesRatherThanSilentlyCollectingZeroRows()
    {
        Assert.Throws<InvalidDataException>(() => RhodeIslandDbrBulkRosterProvider.BuildSearchPayload("REAL ESTATE BROKER"));
        Assert.Throws<ArgumentOutOfRangeException>(() => RhodeIslandDbrBulkRosterProvider.BuildSearchPayload("Real Estate Broker", 1001));
        Assert.Equal(3, new RhodeIslandDbrBulkRosterProvider().Files.Count);
    }

    private sealed class Handler(string mode = "", int total = 1001) : HttpMessageHandler
    {
        public int TokenCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public int DetailCalls { get; private set; }
        public List<int> Offsets { get; } = [];
        public bool Redirected { get; private set; }
        public string? FirstResponse { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Contains(request.RequestUri.IdnHost, new[]
            {
                "ridbrprod-search.state-reg-eastern.tylerapp.com", "ridbrprod.state-reg-eastern.tylerapp.com"
            });
            if (request.RequestUri.AbsolutePath == "/api/auth/token")
            {
                TokenCalls++;
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Null(request.Headers.Authorization);
                Assert.Equal("", await request.Content!.ReadAsStringAsync(ct));
                return Response(request, mode == "malformed-token" ? "{\"token\":\"not-an-access-token\"}"
                    : mode == "expiry-schema" ? "{\"access_token\":\"synthetic-guest-1\",\"expires_in\":\"300\"}"
                    : JsonSerializer.Serialize(new { access_token = "synthetic-guest-" + TokenCalls, token_type = "Bearer", expires_in = mode == "expired-token" ? 0 : 300 }));
            }
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("synthetic-guest-" + TokenCalls, request.Headers.Authorization?.Parameter);
            if (request.RequestUri.AbsolutePath.EndsWith("/details", StringComparison.Ordinal))
            {
                DetailCalls++;
                Assert.Equal(HttpMethod.Get, request.Method);
                return Response(request, JsonSerializer.Serialize(new
                {
                    id = mode == "detail-identity" ? "C2" : "C1",
                    tiles = new[]
                    {
                        new
                        {
                            steps = new[]
                            {
                                new
                                {
                                    contents = new[]
                                    {
                                        new
                                        {
                                            data = new[]
                                            {
                                                new { label = "Credential", value = new[] { "REC.0000001", RhodeIslandDbrBulkImportService.Corporation, "REAL ESTATE" } },
                                                new { label = "Status", value = new[] { mode == "detail-status" ? "INACTIVE" : "ACTIVE" } },
                                                new { label = "Initial Credential Date", value = new[] { "" } },
                                                new { label = "Expiration Date", value = new[] { "04/30/2028" } },
                                                new { label = "Business Address", value = new[] { mode == "detail-name" ? "" : "Published Realty LLC", "1 Main St", "", "Providence, Rhode Island 02903", "United States" } },
                                                new { label = "Business Address", value = new[] { "Older Realty Inc.", "2 Elm St", null!, "Warwick, Rhode Island 02888", "United States" } }
                                            }.Take(mode == "detail-historical" ? 6 : 5).ToArray()
                                        }
                                    }
                                }
                            }
                        }
                    }
                }));
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/licensing/api/endpoints/v1/portal/search", request.RequestUri.AbsolutePath);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = payload.RootElement;
            Assert.Equal(JsonValueKind.Null, root.GetProperty("appliedFilters").ValueKind);
            Assert.Equal("true", root.GetProperty("highlight").GetString());
            Assert.Equal("credential", root.GetProperty("type").GetString());
            var values = root.GetProperty("formRequest").GetProperty("values");
            Assert.Equal(2, values.EnumerateObject().Count());
            Assert.Equal("REAL ESTATE", values.GetProperty("board").GetString());
            var type = values.GetProperty("licenseType").GetString()!;
            Assert.Contains(type, new[] { RhodeIslandDbrBulkImportService.Broker, RhodeIslandDbrBulkImportService.Salesperson, RhodeIslandDbrBulkImportService.Corporation });
            var metadata = root.GetProperty("formRequest").GetProperty("metadata");
            Assert.Equal(type, metadata.GetProperty("group-3").GetProperty("licenseType").GetProperty("value").GetString());
            foreach (var group in metadata.EnumerateObject())
                foreach (var field in group.Value.EnumerateObject().Where(field => field.Name is not ("board" or "licenseType")))
                    Assert.Equal(JsonValueKind.Null, field.Value.GetProperty("value").ValueKind);
            var start = root.GetProperty("start").GetInt32();
            var rows = root.GetProperty("rows").GetInt32();
            Offsets.Add(start);
            SearchCalls++;
            if (mode == "stale-always" || mode == "stale-once" && SearchCalls == 2)
                return new(HttpStatusCode.Unauthorized) { RequestMessage = request, Content = new StringContent("Unauthorized") };
            if (mode is "external-redirect" or "http-redirect" or "redirect-loop" || mode == "safe-redirect" && !Redirected)
            {
                Redirected = true;
                var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { RequestMessage = request };
                response.Headers.Location = mode switch
                {
                    "external-redirect" => new Uri("https://example.invalid/collect-token"),
                    "http-redirect" => new Uri(RhodeIslandDbrBulkImportService.SearchUrl.Replace("https:", "http:")),
                    _ => new Uri(RhodeIslandDbrBulkImportService.SearchUrl + "?guest=1")
                };
                return response;
            }
            if (mode == "malformed-json") return Response(request, "{");
            if (mode == "wrong-schema") return Response(request, "{\"resultCount\":1001,\"results\":[]}");
            if (mode == "count-schema") return Response(request, "{\"resultCount\":\"1001\",\"results\":[],\"filters\":[]}");
            var count = mode == "changed-count" && start > 0 ? total + 1 : total;
            var length = Math.Min(rows, total - start);
            if (mode == "partial" && start > 0) length = 0;
            var sourceType = mode == "wrong-type" ? type.ToUpperInvariant() : type;
            var prefix = type == RhodeIslandDbrBulkImportService.Corporation ? "REC." : type == RhodeIslandDbrBulkImportService.Salesperson ? "RES." : "REB.";
            var cards = Enumerable.Range(start + 1, length).Select(i =>
            {
                var id = mode == "duplicate" && start > 0 ? 1 : mode == "changed-final" && rows == 5 ? i + 1 : i;
                return new
                {
                    id = "C" + id, title = "Jane Q Smith", subtitle = "INDIVIDUAL",
                    details = new[]
                    {
                        new { label = "Credential Details", values = new[] { sourceType, prefix + id.ToString("D7"), "ACTIVE", "04/30/2028", id.ToString("D8") } }
                    },
                    untouched = "Keep this official source property"
                };
            }).ToArray();
            var raw = JsonSerializer.Serialize(new
            {
                resultCount = count, results = cards,
                filters = new[]
                {
                    new { key = "board", items = new[] { new { label = "REAL ESTATE", count } } },
                    new { key = "licenseType", items = new[] { new { label = sourceType, count } } }
                }
            });
            FirstResponse ??= raw;
            var result = Response(request, raw);
            if (mode == "html") result.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            if (mode == "truncated") result.Content.Headers.ContentLength = Encoding.UTF8.GetByteCount(raw) + 1;
            if (mode == "oversized") result.Content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
            return result;
        }

        private static HttpResponseMessage Response(HttpRequestMessage request, string body) =>
            new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class Destination : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(Environment.CurrentDirectory, "ri-download-test-" + Guid.NewGuid().ToString("N") + ".json");
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
