using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class IdahoDoplBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    private const string Landing = "https://edopl.idaho.gov/OnlineServices/";
    private const string Workflow = Landing + "_/";
    private const long MaxBytes = 100L * 1024 * 1024;
    private static readonly Regex Version = new(@"setVerLast\('(?<version>[^']+)',\s*'(?<source>[^']+)'",
        RegexOptions.None, TimeSpan.FromSeconds(2));
    private static readonly Regex Tags = new(@"<(?:input|select|a|table)\b[^>]*>",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex Attributes = new("""(?<key>[\w:$-]+)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)')""",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly string[] SearchFields =
    [
        "Dd-b", "Dd-f", "Dd-g", "Dd-v", "Dd-w", "Dd-x", "Dd-y", "Dd-z",
        "Dd-01", "Dd-11", "Dd-21", "Dd-31", "Dd-41"
    ];
    private static readonly string[] LicenseTypes =
    [
        "Real Estate Broker License", "Real Estate Salesperson License", "Real Estate Cooperative Broker License",
        IdahoDoplBulkImportService.CompanyType, IdahoDoplBulkImportService.BranchType,
        "Real Estate Instructor Certification License", "Real Estate Provider Certification License", "Timeshare Registration"
    ];

    public string State => IdahoDoplBulkImportService.State;
    public string ArchiveFolder => "idaho-bulk";
    public string ParserVersion => IdahoDoplBulkImportService.ParserVersion;
    public string Layout => "Official FAST unrestricted active REC all-types ODS; historical 23-column CSV supported; raw certifications/timeshares retained";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => IdahoDoplBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(IdahoDoplBulkImportService.CheckFile(path, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        IdahoDoplBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct, Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<IdahoDoplBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        if (!Files.Contains(file)) throw new InvalidDataException("Unknown Idaho export.");
        // UseCookies=false and AllowAutoRedirect=false: each anonymous acquisition owns its fresh FAST state.
        // HTTP deliberately does not execute the home page's optional WebAuthN/passkey client scripts.
        var cookies = new CookieContainer();
        string version;
        string source;
        using (var page = await SendAsync(new Uri(Landing), HttpMethod.Get, null, landing: true))
        {
            var html = await ReadTextAsync(page, ct);
            var match = Version.Match(html);
            if (!match.Success) throw new InvalidDataException("Idaho landing omitted fresh FAST state.");
            version = WebUtility.HtmlDecode(match.Groups["version"].Value);
            source = WebUtility.HtmlDecode(match.Groups["source"].Value);
        }
        using var home = await JsonAsync("", new() { ["Load"] = "1" }, get: true);
        var homeHtml = Markup(home.RootElement);
        var homeTags = ParseTags(homeHtml);
        var bulkLinks = homeTags.Where(attrs => attrs.GetValueOrDefault("title") == "Search for a Bulk List of Licenses"
            && attrs.GetValueOrDefault("data-event") == "LinkClick").ToList();
        if (bulkLinks.Count != 1 || bulkLinks[0].GetValueOrDefault("data-linkid") != "Df-3-2")
            throw new InvalidDataException("Idaho home omitted its public bulk-list action.");
        var form = HiddenFields(homeTags);
        foreach (var name in new[] { "Dd-a", "Dd-b", "Df-f" })
        {
            if (!homeTags.Any(attrs => attrs.GetValueOrDefault("name") == name))
                throw new InvalidDataException("Idaho home form changed.");
            form[name] = "";
        }
        form["LASTFOCUSFIELD__"] = "l_Df-3-2";
        form["EVENT__"] = "Df-3-2";
        form["TYPE__"] = "0";
        form["CLOSECONFIRMED__"] = "false";
        form["SCREENWIDTH__"] = "1";
        form["SCROLLPOSITIONS__"] = "";
        using var search = await JsonAsync("EventOccurred", form);
        var searchHtml = Markup(search.RootElement);
        var searchTags = ParseTags(searchHtml);
        if (!searchHtml.Contains("Public License List Search", StringComparison.Ordinal)
            || !SearchFields.All(name => searchTags.Any(attrs => attrs.GetValueOrDefault("name") == name))
            || !searchHtml.Contains("BORECM", StringComparison.Ordinal)
            || !searchHtml.Contains("LICVLD", StringComparison.Ordinal))
            throw new InvalidDataException("Idaho public bulk-list form or active REC controls changed.");
        form = HiddenFields(searchTags);
        foreach (var name in SearchFields) form[name] = "";
        form["Dd-b"] = "SingleSearch";
        form["Dd-f"] = "BORECM";
        form["Dd-v"] = "LICVLD";
        form["LASTFOCUSFIELD__"] = "Dd-f";
        using (var recalculated = await JsonAsync("Recalc", form))
        {
            if (!recalculated.RootElement.TryGetProperty("Updates", out var updates)
                || !updates.TryGetProperty("FieldUpdates", out var fields) || fields.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Idaho did not recalculate the REC query.");
            var boards = fields.EnumerateArray().Where(field => String(field, "field") == "Dd-f").ToList();
            var types = fields.EnumerateArray().Where(field => String(field, "field") == "Dd-g").ToList();
            if (boards.Count != 1 || String(boards[0], "value") != "BORECM"
                || String(boards[0], "text") != "Real Estate Commission"
                || types.Count != 1 || String(types[0], "value") != ""
                || !types[0].TryGetProperty("comboitems", out var options)
                || options.ValueKind != JsonValueKind.Array
                || options.EnumerateArray().Any(option => String(option, "label") is null)
                || !options.EnumerateArray().Select(option => String(option, "label"))
                    .Where(label => !string.IsNullOrEmpty(label)).Order(StringComparer.Ordinal)
                    .SequenceEqual(LicenseTypes.Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Idaho REC all-types query coverage changed.");
        }
        form["LASTFOCUSFIELD__"] = "action_1";
        form["ACTION_ID__"] = "1";
        form["TYPE__"] = "CTLSTD";
        form["CLOSECONFIRMED__"] = "false";
        form["EVENT_TYPE__"] = "0";
        long published;
        using (var results = await JsonAsync("ExecuteAction", form))
        {
            var html = Markup(results.RootElement);
            var tables = ParseTags(html).Where(attrs => attrs.GetValueOrDefault("id") == "Df-r"
                && attrs.GetValueOrDefault("aria-labelledby") == "caption2_Df-s").ToList();
            if (!html.Contains("List Search Results", StringComparison.Ordinal) || tables.Count != 1
                || !long.TryParse(tables[0].GetValueOrDefault("data-fast-rowcount"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out published) || published is <= 0 or > 500_000
                || !ParseTags(html).Any(attrs => attrs.GetValueOrDefault("id") == "cl_Df-t"))
                throw new InvalidDataException("Idaho results omitted the complete count or public export action.");
        }
        form.Remove("ACTION_ID__");
        form.Remove("EVENT_TYPE__");
        form["LASTFOCUSFIELD__"] = "cl_Df-t";
        form["EVENT__"] = "Df-t";
        form["TYPE__"] = "0";
        form["SCREENWIDTH__"] = "1";
        form["SCROLLPOSITIONS__"] = "";
        Uri exportUri;
        using (var exportAction = await JsonAsync("EventOccurred", form))
        {
            if (!exportAction.RootElement.TryGetProperty("postFunctions", out var functions) || functions.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Idaho omitted its fresh export token.");
            var windows = functions.EnumerateArray().Where(function => String(function, "name") == "OpenWindow").ToList();
            if (windows.Count != 1 || !windows[0].TryGetProperty("parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Array || parameters.GetArrayLength() != 1
                || parameters[0].ValueKind != JsonValueKind.String
                || !Uri.TryCreate(new Uri(Workflow), parameters[0].GetString(), out exportUri!)
                || !ValidExport(exportUri))
                throw new InvalidDataException("Idaho export token URL changed or left the official workflow.");
        }
        using var export = await SendAsync(exportUri, HttpMethod.Get, null);
        if (export.Content.Headers.ContentType?.MediaType != "application/vnd.oasis.opendocument.spreadsheet")
            throw new InvalidDataException("Idaho export did not return the official ODS download.");
        var expectedBytes = export.Content.Headers.ContentLength;
        if (expectedBytes > MaxBytes) throw new InvalidDataException("Idaho export exceeds the 100 MiB limit.");
        var created = false;
        try
        {
            long bytes = 0;
            await using (var input = await export.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                created = true;
                var buffer = new byte[81920];
                var lastProgress = DateTime.UtcNow;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    bytes += read;
                    if (bytes > MaxBytes) throw new InvalidDataException("Idaho export exceeds the 100 MiB limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    if (DateTime.UtcNow - lastProgress >= TimeSpan.FromSeconds(1))
                    {
                        progress(bytes, expectedBytes);
                        lastProgress = DateTime.UtcNow;
                    }
                }
            }
            if (bytes == 0 || expectedBytes is { } length && length != bytes)
                throw new InvalidDataException("Incomplete Idaho export download.");
            using (var signature = File.OpenRead(path))
                if (signature.ReadByte() != 'P' || signature.ReadByte() != 'K')
                    throw new InvalidDataException("Idaho official ODS download omitted its ZIP package signature.");
            if (IdahoDoplBulkImportService.CheckFile(path, ct) != published)
                throw new InvalidDataException("Idaho export row count differs from the unrestricted active query count.");
            progress(bytes, expectedBytes);
            return new(bytes, export.Content.Headers.LastModified, export.Headers.ETag?.ToString());
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }

        async Task<JsonDocument> JsonAsync(string endpoint, Dictionary<string, string> fields, bool get = false)
        {
            var body = new Dictionary<string, string>(fields, StringComparer.Ordinal)
            {
                ["FAST_SCRIPT_VER__"] = "1", ["FAST_VERLAST__"] = version, ["FAST_VERLAST_SOURCE__"] = source
            };
            var uri = new Uri(Workflow + endpoint);
            if (get)
            {
                using var encoded = new FormUrlEncodedContent(body);
                uri = new Uri(uri.AbsoluteUri + "?" + await encoded.ReadAsStringAsync(ct));
            }
            using var response = await SendAsync(uri, get ? HttpMethod.Get : HttpMethod.Post, get ? null : body);
            if (response.Content.Headers.ContentType?.MediaType != "application/json"
                || !response.Headers.TryGetValues("Fast-Ver-Last", out var versions)
                || !response.Headers.TryGetValues("Fast-Ver-Source", out var sources))
                throw new InvalidDataException("Idaho workflow omitted fresh FAST response state.");
            var versionValues = versions.ToList();
            var sourceValues = sources.ToList();
            if (versionValues.Count != 1 || sourceValues.Count != 1)
                throw new InvalidDataException("Idaho workflow returned ambiguous FAST state.");
            var nextVersion = versionValues[0];
            var nextSource = sourceValues[0];
            if (string.IsNullOrWhiteSpace(nextVersion) || nextVersion == version || string.IsNullOrWhiteSpace(nextSource))
                throw new InvalidDataException("Idaho workflow reused stale FAST state.");
            version = nextVersion;
            source = nextSource;
            try
            {
                var json = JsonDocument.Parse(await ReadTextAsync(response, ct));
                if (json.RootElement.ValueKind != JsonValueKind.Object)
                {
                    json.Dispose();
                    throw new InvalidDataException("Idaho workflow JSON schema changed.");
                }
                return json;
            }
            catch (JsonException ex) { throw new InvalidDataException("Malformed Idaho workflow JSON.", ex); }
        }

        async Task<HttpResponseMessage> SendAsync(Uri uri, HttpMethod method, Dictionary<string, string>? body, bool landing = false)
        {
            var expected = uri;
            for (var redirects = 0; redirects <= 6; redirects++)
            {
                if (!Official(uri) || (landing
                        ? !(uri.AbsoluteUri is Landing or Workflow || uri.AbsoluteUri == Landing + "?Check=1")
                        : uri != expected))
                    throw new InvalidDataException("Idaho workflow redirected outside its allowlisted official URLs.");
                using var request = new HttpRequestMessage(method, uri);
                if (body is not null) request.Content = new FormUrlEncodedContent(body);
                var cookie = cookies.GetCookieHeader(uri);
                if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.RequestMessage?.RequestUri != uri)
                {
                    response.Dispose();
                    throw new InvalidDataException("Idaho HTTP client automatically redirected; disable redirects and cookies.");
                }
                if (response.Headers.TryGetValues("Set-Cookie", out var values))
                    foreach (var value in values) cookies.SetCookies(uri, value);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    var status = response.StatusCode;
                    response.Dispose();
                    if (location is null) throw new InvalidDataException("Idaho redirect omitted its location.");
                    uri = new Uri(uri, location);
                    if (status is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther)
                    {
                        method = HttpMethod.Get;
                        body = null;
                    }
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    var status = response.StatusCode;
                    response.Dispose();
                    throw new HttpRequestException("Idaho public workflow request failed.", null, status);
                }
                return response;
            }
            throw new InvalidDataException("Idaho workflow exceeded its redirect limit.");
        }
    }

    private static bool Official(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("edopl.idaho.gov", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    private static bool ValidExport(Uri uri)
    {
        if (!Official(uri) || uri.AbsolutePath != "/OnlineServices/_/ExportView") return false;
        var parts = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToList();
        if (parts.Count != 4 || parts.Any(part => part.Length != 2) || parts.Select(part => part[0]).Distinct().Count() != 4)
            return false;
        var fields = parts.ToDictionary(part => part[0], part => WebUtility.UrlDecode(part[1]), StringComparer.Ordinal);
        return fields.GetValueOrDefault("Doc") == "Df" && fields.GetValueOrDefault("Format") == "Token"
            && new[] { "UID", "Token" }.All(key => fields.TryGetValue(key, out var value) && value.Length is >= 8 and <= 200
                && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
    }

    private static List<Dictionary<string, string>> ParseTags(string html) =>
        Tags.Matches(html).Cast<Match>().Select(tag => Attributes.Matches(tag.Value).Cast<Match>()
            .ToDictionary(attribute => attribute.Groups["key"].Value,
                attribute => WebUtility.HtmlDecode(attribute.Groups["value"].Value), StringComparer.OrdinalIgnoreCase)).ToList();

    private static Dictionary<string, string> HiddenFields(List<Dictionary<string, string>> tags)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags.Where(tag => tag.GetValueOrDefault("type")?.Equals("hidden", StringComparison.OrdinalIgnoreCase) == true))
        {
            if (tag.TryGetValue("name", out var name) && !fields.TryAdd(name, tag.GetValueOrDefault("value") ?? ""))
                throw new InvalidDataException("Idaho form contains duplicate hidden fields.");
        }
        if (!fields.ContainsKey("LASTFOCUSFIELD__") || fields.GetValueOrDefault("DOC_MODAL_ID__") != "0")
            throw new InvalidDataException("Idaho public form omitted fresh hidden state.");
        return fields;
    }

    private static string Markup(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Idaho workflow JSON schema changed.");
        if (root.TryGetProperty("html", out var html) && html.ValueKind == JsonValueKind.String)
            return html.GetString()!;
        if (root.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
        {
            var managers = contents.EnumerateArray().Where(content => String(content, "manager") is not null).ToList();
            if (managers.Count == 1) return String(managers[0], "manager")!;
        }
        throw new InvalidDataException("Idaho workflow omitted the public manager form.");
    }

    private static string? String(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const int limit = 2_000_000;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Idaho workflow page exceeds its size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Idaho workflow page exceeds its size limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
