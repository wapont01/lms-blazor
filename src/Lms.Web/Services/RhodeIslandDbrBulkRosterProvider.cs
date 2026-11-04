using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class RhodeIslandDbrBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    public string State => RhodeIslandDbrBulkImportService.State;
    public string ArchiveFolder => "rhode-island-bulk";
    public string ParserVersion => RhodeIslandDbrBulkImportService.ParserVersion;
    public string Layout => "DBR public guest-token search; exact case-sensitive credential types; complete unfiltered pages and corporation business details";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => RhodeIslandDbrBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(RhodeIslandDbrBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        RhodeIslandDbrBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct, Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<RhodeIslandDbrBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);

    public static object BuildSearchPayload(string licenseType, int rows = 1000, int start = 0)
    {
        RhodeIslandDbrBulkImportService.NormalizeLicenseType(licenseType);
        if (rows is < 1 or > 1000 || start is < 0 or >= RhodeIslandDbrBulkImportService.MaximumRows)
            throw new ArgumentOutOfRangeException(nameof(rows));
        object Field(string id, string label, string helper, string width, string kind = "text", object? value = null, bool multiple = false) => new
        {
            disabled = false, errorText = "", helperText = helper, hidden = false, required = false,
            inputPattern = (string?)null, id, label, type = kind, options = Array.Empty<object>(),
            mask = (string?)null, maxLength = 255, value, liveSearch = false, properties = (object?)null,
            readOnly = false, minDate = (string?)null, width, blockCopy = false, disableFutureDate = false,
            disablePastDate = false, multiple, autoComplete = (string?)null
        };
        return new
        {
            appliedFilters = (object?)null, highlight = "true",
            formRequest = new
            {
                metadata = new Dictionary<string, object>
                {
                    ["group-1"] = new
                    {
                        lastName = Field("lastName", "Last Name", "Last Name", "half"),
                        firstName = Field("firstName", "First Name", "First Name", "half")
                    },
                    ["group-2"] = new { organizationName = Field("organizationName", "Organization Name", "Organization Name", "full") },
                    ["group-3"] = new
                    {
                        board = Field("board", "Board", "Select the Board", "half", "select", "REAL ESTATE", true),
                        licenseType = Field("licenseType", "Credential Type", "Credential Type", "half", "select", licenseType, true)
                    },
                    ["group-4"] = new
                    {
                        licenseNumber = Field("licenseNumber", "Credential Number", "Credential Number", "half"),
                        licenseStatus = Field("licenseStatus", "Credential Status", "Credential Status", "half", "select", multiple: true)
                    },
                    ["group-5"] = new
                    {
                        city = Field("city", "City", "Credential City", "third"),
                        state = Field("state", "State", "State", "third", "select", multiple: true),
                        zipCode = Field("zipCode", "Zip Code", "Zip Code", "third")
                    }
                },
                values = new { board = "REAL ESTATE", licenseType }
            },
            rows, start, type = "credential"
        };
    }

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        if (!Files.Contains(file)) throw new InvalidDataException("Unknown Rhode Island roster file.");
        const int pageSize = 1000;
        var type = RhodeIslandDbrBulkImportService.LicenseTypeForFile(file.Name);
        // This token is local to one acquisition, never persisted, logged or shared with other collectors.
        string? token = null;
        var refreshed = false;
        var created = false;
        try
        {
            token = await GetTokenAsync();
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                created = true;
                using var writer = new Utf8JsonWriter(destination);
                writer.WriteStartObject();
                writer.WriteString("licenseType", type);
                writer.WriteNumber("pageSize", pageSize);
                writer.WriteStartArray("pages");
                int? total = null;
                var start = 0;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var corporations = new List<JsonElement>();
                string[]? firstIds = null;
                while (total is null || start < total)
                {
                    ct.ThrowIfCancellationRequested();
                    var raw = await SearchAsync(pageSize, start);
                    using var json = Parse(raw);
                    total ??= RhodeIslandDbrBulkImportService.ValidatePage(json.RootElement, type, start, pageSize, null);
                    RhodeIslandDbrBulkImportService.ValidatePage(json.RootElement, type, start, pageSize, total);
                    var rows = json.RootElement.GetProperty("results");
                    firstIds ??= rows.EnumerateArray().Take(5).Select(row => row.GetProperty("id").GetString()!).ToArray();
                    foreach (var row in rows.EnumerateArray())
                    {
                        var parsed = RhodeIslandDbrBulkImportService.ParseRecord(row, type);
                        if (!ids.Add(parsed.InternalId))
                            throw new InvalidDataException("Rhode Island paging repeated a source identity.");
                        if (type == RhodeIslandDbrBulkImportService.Corporation) corporations.Add(row.Clone());
                    }
                    writer.WriteStartObject();
                    writer.WriteNumber("start", start);
                    writer.WritePropertyName("response");
                    writer.WriteRawValue(raw, skipInputValidation: false);
                    writer.WriteEndObject();
                    start += rows.GetArrayLength();
                    await FlushAsync();
                }
                writer.WriteEndArray();
                if (type == RhodeIslandDbrBulkImportService.Corporation)
                {
                    writer.WriteStartArray("details");
                    foreach (var row in corporations)
                    {
                        var id = row.GetProperty("id").GetString()!;
                        var raw = await AuthorizedAsync(HttpMethod.Get, RhodeIslandDbrBulkImportService.SearchUrl + "/" + id + "/details", null);
                        using var json = Parse(raw);
                        RhodeIslandDbrBulkImportService.ParseRecord(row, type, json.RootElement);
                        writer.WriteRawValue(raw, skipInputValidation: false);
                        await FlushAsync();
                    }
                    writer.WriteEndArray();
                }
                // Count alone cannot detect offset drift; recheck the first published identities after acquisition.
                using (var final = Parse(await SearchAsync(5, 0)))
                {
                    RhodeIslandDbrBulkImportService.ValidatePage(final.RootElement, type, 0, 5, total);
                    if (ids.Count != total || !final.RootElement.GetProperty("results").EnumerateArray()
                        .Select(row => row.GetProperty("id").GetString()!).SequenceEqual(firstIds!))
                        throw new InvalidDataException("Rhode Island roster changed during acquisition.");
                }
                writer.WriteEndObject();
                await FlushAsync();

                async Task FlushAsync()
                {
                    if (destination.Length + writer.BytesPending > RhodeIslandDbrBulkImportService.MaximumArchiveBytes)
                        throw new InvalidDataException("Rhode Island archive exceeds the 200 MiB limit.");
                    await writer.FlushAsync(ct);
                    if (destination.Length > RhodeIslandDbrBulkImportService.MaximumArchiveBytes)
                        throw new InvalidDataException("Rhode Island archive exceeds the 200 MiB limit.");
                    progress(destination.Length, null);
                }
            }
            ct.ThrowIfCancellationRequested();
            RhodeIslandDbrBulkImportService.CheckFile(path, file.Name, ct);
            var bytes = new FileInfo(path).Length;
            progress(bytes, bytes);
            return new(bytes, null, null);
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }
        finally { token = null; }

        Task<byte[]> SearchAsync(int rows, int start) =>
            AuthorizedAsync(HttpMethod.Post, RhodeIslandDbrBulkImportService.SearchUrl,
                JsonSerializer.SerializeToUtf8Bytes(BuildSearchPayload(type, rows, start)));

        async Task<string> GetTokenAsync()
        {
            var raw = await SendAsync(HttpMethod.Post, RhodeIslandDbrBulkImportService.TokenUrl, [], null, 64 * 1024);
            using var json = Parse(raw);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("access_token", out var access) || access.ValueKind != JsonValueKind.String
                || access.GetString() is not { Length: > 0 and <= 16000 } value || value.Any(char.IsWhiteSpace)
                || value.Any(c => char.IsControl(c)))
                throw new InvalidDataException("Rhode Island public guest-token schema changed.");
            if (json.RootElement.TryGetProperty("expires_in", out var expiry)
                && (expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetInt64(out var seconds) || seconds <= 0))
                throw new InvalidDataException("Rhode Island public guest token is already expired.");
            if (json.RootElement.TryGetProperty("token_type", out var tokenType)
                && (tokenType.ValueKind != JsonValueKind.String || !string.Equals(tokenType.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Rhode Island public guest-token type changed.");
            return value;
        }

        async Task<byte[]> AuthorizedAsync(HttpMethod method, string url, byte[]? body)
        {
            try { return await SendAsync(method, url, body, token, 8 * 1024 * 1024); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                refreshed = true;
                token = await GetTokenAsync();
                return await SendAsync(method, url, body, token, 8 * 1024 * 1024);
            }
        }

        async Task<byte[]> SendAsync(HttpMethod method, string url, byte[]? body, string? bearer, int limit)
        {
            var original = new Uri(url);
            var current = original;
            for (var redirects = 0; redirects <= 3; redirects++)
            {
                ct.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(method, current);
                if (body is not null)
                {
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }
                if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.RequestMessage?.RequestUri != current)
                    throw new InvalidDataException("Rhode Island HTTP client followed an unvalidated redirect.");
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location;
                    if (redirects == 3 || location is null
                        || response.StatusCode is not (HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                        throw new InvalidDataException("Unsafe or excessive Rhode Island redirect.");
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (next.Scheme != Uri.UriSchemeHttps || !next.IsDefaultPort || next.UserInfo.Length != 0
                        || next.IdnHost != original.IdnHost || next.AbsolutePath != original.AbsolutePath
                        || next.Fragment.Length != 0)
                        throw new InvalidDataException("Rhode Island redirect left its allowlisted official endpoint.");
                    current = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("Rhode Island official public API request failed.", null, response.StatusCode);
                if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
                    throw new InvalidDataException("Rhode Island public API did not return JSON.");
                var length = response.Content.Headers.ContentLength;
                if (length is > 0 && length > limit) throw new InvalidDataException("Rhode Island API response exceeds its byte limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var bytes = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    if (bytes.Length + read > limit) throw new InvalidDataException("Rhode Island API response exceeds its byte limit.");
                    bytes.Write(buffer, 0, read);
                }
                if (bytes.Length == 0 || length is { } expected && bytes.Length != expected)
                    throw new InvalidDataException("Truncated Rhode Island API response.");
                return bytes.ToArray();
            }
            throw new InvalidDataException("Excessive Rhode Island redirects.");
        }
    }

    private static JsonDocument Parse(byte[] raw)
    {
        try { return JsonDocument.Parse(raw); }
        catch (JsonException ex) { throw new InvalidDataException("Malformed Rhode Island public API JSON.", ex); }
    }
}
