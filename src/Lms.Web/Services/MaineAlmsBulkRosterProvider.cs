using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class MaineAlmsBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    private const string Prefix = "ctl00$ctl00$mainContent$mainContent$";
    private const long MaxBytes = 100L * 1024 * 1024;
    private const string ResultsPath = "/ALMSOnline/ALMSQuery/SearchResults.aspx";
    private static readonly Regex Inputs = new(@"<input\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex Attributes = new("""(?<key>[\w:$-]+)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)')""",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex Count = new(@"(?<count>[\d,]+)\s+records?\s+found\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex Links = new(@"<a\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));

    public string State => MaineAlmsBulkImportService.State;
    public string ArchiveFolder => "maine-bulk";
    public string ParserVersion => MaineAlmsBulkImportService.ParserVersion;
    public string Layout => "ALMS unrestricted active real estate CSV exports; all locations, raw alias rows retained; distinct credentials";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => MaineAlmsBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(MaineAlmsBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        MaineAlmsBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct, Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<MaineAlmsBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        if (!Files.Contains(file)) throw new InvalidDataException("Unknown Maine export.");
        // The supplied client must have UseCookies=false and AllowAutoRedirect=false.
        // Each export needs its own ALMS query session; the export URL selects the session's last search.
        var cookies = new CookieContainer();
        var searchUri = new Uri(file.Url);
        var activeField = file.Name == MaineAlmsBulkImportService.CompanyFile ? "scOnlyActive" : "scActiveOnly";
        Dictionary<string, string> form;
        Uri formUri;
        using (var page = await SendAsync(searchUri, HttpMethod.Get, null, searchUri.AbsolutePath))
        {
            formUri = page.RequestMessage!.RequestUri!;
            var html = await ReadHtmlAsync(page, ct);
            form = new(StringComparer.Ordinal);
            var controls = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match input in Inputs.Matches(html))
            {
                var attrs = ReadAttributes(input.Value);
                if (!attrs.TryGetValue("name", out var name)) continue;
                controls.Add(name);
                if (attrs.GetValueOrDefault("type")?.Equals("hidden", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (!form.TryAdd(name, attrs.GetValueOrDefault("value") ?? ""))
                        throw new InvalidDataException("Maine search contains duplicate hidden fields.");
                }
            }
            if (!form.ContainsKey("__VIEWSTATE") || !form.ContainsKey("__EVENTVALIDATION")
                || !controls.Contains(Prefix + activeField) || !controls.Contains(Prefix + "btnSearch")
                || !html.Contains(Prefix + "scRegulator", StringComparison.Ordinal))
                throw new InvalidDataException("Maine search form changed or omitted fresh ASP.NET state.");
            if (form.TryGetValue("__VIEWSTATEFIELDCOUNT", out var split))
            {
                if (!int.TryParse(split, out var fields) || fields is < 1 or > 100
                    || Enumerable.Range(1, fields - 1).Any(index => !form.ContainsKey("__VIEWSTATE" + index)))
                    throw new InvalidDataException("Maine search omitted split viewstate fields.");
            }
            form[Prefix + "scRegulator"] = "4060";
            form[Prefix + activeField] = "on";
            form[Prefix + "btnSearch"] = "Search";
        }
        long published;
        using (var results = await SendAsync(formUri, HttpMethod.Post, form, searchUri.AbsolutePath, ResultsPath))
        {
            if (!results.RequestMessage!.RequestUri!.AbsolutePath.Equals(ResultsPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Maine query did not reach its official results page.");
            var html = WebUtility.HtmlDecode(await ReadHtmlAsync(results, ct));
            var match = Count.Match(html);
            if (!match.Success || !long.TryParse(match.Groups["count"].Value, NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out published) || published <= 0)
                throw new InvalidDataException("Maine active query did not publish a positive result count.");
            if (!Links.Matches(html).Cast<Match>().Select(link => ReadAttributes(link.Value).GetValueOrDefault("href"))
                .Any(href => href is not null && Uri.TryCreate(results.RequestMessage.RequestUri, href, out var uri)
                    && uri.AbsoluteUri.Equals(MaineAlmsBulkImportService.ExportUrl, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Maine results did not publish the expected CSV export.");
        }
        var exportUri = new Uri(MaineAlmsBulkImportService.ExportUrl);
        using var export = await SendAsync(exportUri, HttpMethod.Get, null, exportUri.AbsolutePath);
        if (export.Content.Headers.ContentType?.MediaType is not ("application/octet-stream" or "text/csv"))
            throw new InvalidDataException("Maine export did not return a CSV download.");
        var expectedBytes = export.Content.Headers.ContentLength;
        if (expectedBytes > MaxBytes) throw new InvalidDataException("Maine export exceeds the 100 MiB limit.");
        var created = false;
        try
        {
            long bytes = 0;
            await using (var source = await export.Content.ReadAsStreamAsync(ct))
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                created = true;
                var buffer = new byte[81920];
                var lastProgress = DateTime.UtcNow;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    bytes += read;
                    if (bytes > MaxBytes) throw new InvalidDataException("Maine export exceeds the 100 MiB limit.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                    if (DateTime.UtcNow - lastProgress >= TimeSpan.FromSeconds(1))
                    {
                        progress(bytes, expectedBytes);
                        lastProgress = DateTime.UtcNow;
                    }
                }
            }
            if (bytes == 0 || expectedBytes is { } length && length != bytes)
                throw new InvalidDataException("Incomplete Maine export download.");
            // ALMS counts search/alias rows, not distinct credentials.
            if (MaineAlmsBulkImportService.CheckFile(path, file.Name, ct) != published)
                throw new InvalidDataException("Maine CSV row count differs from the unrestricted active query count.");
            progress(bytes, expectedBytes);
            return new(bytes, export.Content.Headers.LastModified, export.Headers.ETag?.ToString());
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }

        async Task<HttpResponseMessage> SendAsync(Uri uri, HttpMethod method, Dictionary<string, string>? body, params string[] allowedPaths)
        {
            for (var redirects = 0; redirects <= 6; redirects++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(searchUri.Host, StringComparison.OrdinalIgnoreCase)
                    || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
                    || !allowedPaths.Contains(uri.AbsolutePath, StringComparer.OrdinalIgnoreCase)
                    || uri.Query is not ("" or "?AspxAutoDetectCookieSupport=1"))
                    throw new InvalidDataException("Maine workflow redirected outside its allowlisted official URLs.");
                using var request = new HttpRequestMessage(method, uri);
                if (body is not null) request.Content = new FormUrlEncodedContent(body);
                var cookie = cookies.GetCookieHeader(uri);
                if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.RequestMessage?.RequestUri != uri)
                {
                    response.Dispose();
                    throw new InvalidDataException("Maine HTTP client automatically redirected; disable automatic redirects and cookies.");
                }
                if (response.Headers.TryGetValues("Set-Cookie", out var values))
                    foreach (var value in values) cookies.SetCookies(uri, value);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    var status = response.StatusCode;
                    response.Dispose();
                    if (location is null) throw new InvalidDataException("Maine redirect omitted its location.");
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
                    throw new HttpRequestException("Maine ALMS request failed.", null, status);
                }
                return response;
            }
            throw new InvalidDataException("Maine workflow exceeded its redirect limit.");
        }
    }

    private static Dictionary<string, string> ReadAttributes(string tag) =>
        Attributes.Matches(tag).Cast<Match>().ToDictionary(match => match.Groups["key"].Value,
            match => WebUtility.HtmlDecode(match.Groups["value"].Value), StringComparer.OrdinalIgnoreCase);

    private static async Task<string> ReadHtmlAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const int limit = 2_000_000;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Maine workflow page exceeds its size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (destination.Length + read > limit) throw new InvalidDataException("Maine workflow page exceeds its size limit.");
            destination.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(destination.ToArray());
    }
}
