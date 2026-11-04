using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class KentuckyOopBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    private const string Prefix = "ctl00$ContentPlaceHolder2$";
    private static readonly Regex Inputs = new(@"<input\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex Attributes = new("""(?<key>[\w:$-]+)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)')""",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex Selects = new(@"<select\b(?<attrs>[^>]*)>(?<options>.*?)</select>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex Options = new(@"<option\b(?<attrs>[^>]*)>(?<text>.*?)</option>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
    private static readonly Regex Forms = new(@"<form\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex Count = new(
        """<span\b[^>]*\bid\s*=\s*["']ContentPlaceHolder2_lblDirCount["'][^>]*>\s*<b>\s*Count\s*:\s*(?<count>[\d,]+)\s*</b>""",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly IReadOnlyDictionary<string, string> OfferedTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["101"] = "Broker", ["102"] = "Sales Associate", ["103"] = "Registered Firm Branch"
    };

    public string State => KentuckyOopBulkImportService.State;
    public string ArchiveFolder => "kentucky-bulk";
    public string ParserVersion => KentuckyOopBulkImportService.ParserVersion;
    public string Layout => "OOP All Regions active-directory Broker, Sales Associate and Registered Firm Branch complete HTML Excel exports; published Probation/requested-cancellation labels retained; original address labels retained; no inferred affiliations or parent/branch distinction";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => KentuckyOopBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(KentuckyOopBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        KentuckyOopBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct, Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<KentuckyOopBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        if (!Files.Contains(file)) throw new InvalidDataException("Unknown Kentucky export.");
        // The supplied client must have UseCookies=false and AllowAutoRedirect=false.
        // Each export uses a separate session and the fresh hidden state from each preceding response.
        var cookies = new CookieContainer();
        var sourceUri = new Uri(KentuckyOopBulkImportService.SourcePage);
        var type = KentuckyOopBulkImportService.LicenseTypeForFile(file.Name);
        var typeValue = OfferedTypes.Single(pair => pair.Value == type).Key;
        Dictionary<string, string> form;
        using (var page = await SendAsync(sourceUri, HttpMethod.Get, null))
        {
            var html = await ReadHtmlAsync(page, ct);
            form = ReadForm(html, page.RequestMessage!.RequestUri!, out var choices);
            RequireChoice(choices, "ddlBoard", "31", "Kentucky Real Estate Commission");
            RequireChoice(choices, "ddlRegion", "0", "All Regions");
            SetUnrestricted(form);
            form[Prefix + "btnSearch"] = "Search";
        }
        using (var board = await SendAsync(sourceUri, HttpMethod.Post, form))
        {
            var html = await ReadHtmlAsync(board, ct);
            form = ReadForm(html, board.RequestMessage!.RequestUri!, out var choices);
            RequireChoice(choices, "ddlBoard", "31", "Kentucky Real Estate Commission");
            RequireChoice(choices, "ddlRegion", "0", "All Regions");
            if (!choices.TryGetValue(Prefix + "ddlLicType", out var offered)
                || offered.Where(pair => pair.Key != "0").Count() != OfferedTypes.Count
                || OfferedTypes.Any(pair => offered.GetValueOrDefault(pair.Key) != pair.Value))
                throw new InvalidDataException("Kentucky offered real estate license types changed.");
            SetUnrestricted(form);
            form[Prefix + "ddlLicType"] = typeValue;
            form[Prefix + "btnSearch"] = "Search";
        }
        long published;
        using (var results = await SendAsync(sourceUri, HttpMethod.Post, form))
        {
            var html = await ReadHtmlAsync(results, ct);
            form = ReadForm(html, results.RequestMessage!.RequestUri!, out _);
            if (form.GetValueOrDefault(Prefix + "ddlBoard") != "31"
                || form.GetValueOrDefault(Prefix + "ddlLicType") != typeValue
                || form.GetValueOrDefault(Prefix + "ddlRegion") != "0"
                || form.GetValueOrDefault(Prefix + "ddlCounty", "") is not ("" or "0")
                || form.GetValueOrDefault(Prefix + "ddlHours") != "0"
                || form.GetValueOrDefault(Prefix + "txtBusName") != ""
                || form.GetValueOrDefault(Prefix + "txtBusLocation") != ""
                || !html.Contains("Kentucky Real Estate Commission Active License Directory", StringComparison.Ordinal))
                throw new InvalidDataException("Kentucky query did not retain unrestricted active real estate filters.");
            var match = Count.Match(html);
            if (!match.Success || !long.TryParse(match.Groups["count"].Value, NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out published) || published <= 0)
                throw new InvalidDataException("Kentucky active query did not publish a positive result count.");
            var exportControl = Inputs.Matches(html).Cast<Match>().Select(input => ReadAttributes(input.Value))
                .SingleOrDefault(attrs => attrs.GetValueOrDefault("name") == Prefix + "btnExport");
            if (exportControl is null || exportControl.GetValueOrDefault("value") != "Export to Excel"
                || exportControl.GetValueOrDefault("style", "").Replace(" ", "").Contains("display:none", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Kentucky query did not publish its Excel export.");
            form[Prefix + "btnExport"] = "Export to Excel";
        }
        using var export = await SendAsync(sourceUri, HttpMethod.Post, form);
        var contentTypes = export.Content.Headers.TryGetValues("Content-Type", out var rawContentTypes)
            ? rawContentTypes.ToArray() : Array.Empty<string>();
        if (contentTypes.Length != 1) throw new InvalidDataException("Kentucky export omitted its content type.");
        var media = contentTypes[0].Split(';')[0].Trim().ToLowerInvariant();
        // This unusual media type is actually returned by OOP. Preserve the bare HTML bytes.
        if (media is not ("application/application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or "application/vnd.ms-excel"
            or "application/octet-stream" or "text/csv"))
            throw new InvalidDataException("Kentucky export did not return an official Excel or CSV download.");
        var expectedBytes = export.Content.Headers.ContentLength;
        if (expectedBytes > KentuckyOopBulkImportService.MaxFileBytes)
            throw new InvalidDataException("Kentucky export exceeds the 100 MiB limit.");
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
                    if (bytes > KentuckyOopBulkImportService.MaxFileBytes)
                        throw new InvalidDataException("Kentucky export exceeds the 100 MiB limit.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                    if (DateTime.UtcNow - lastProgress >= TimeSpan.FromSeconds(1))
                    {
                        progress(bytes, expectedBytes);
                        lastProgress = DateTime.UtcNow;
                    }
                }
            }
            if (bytes == 0 || expectedBytes is { } length && length != bytes)
                throw new InvalidDataException("Incomplete Kentucky export download.");
            if (KentuckyOopBulkImportService.CheckFile(path, file.Name, ct) != published)
                throw new InvalidDataException("Kentucky export row count differs from its unrestricted active query count.");
            progress(bytes, expectedBytes);
            return new(bytes, export.Content.Headers.LastModified, export.Headers.ETag?.ToString());
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }

        async Task<HttpResponseMessage> SendAsync(Uri uri, HttpMethod method, Dictionary<string, string>? body)
        {
            for (var redirects = 0; redirects <= 6; redirects++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(sourceUri.Host, StringComparison.OrdinalIgnoreCase)
                    || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
                    || !uri.AbsolutePath.Equals(sourceUri.AbsolutePath, StringComparison.OrdinalIgnoreCase)
                    || uri.Query is not ("" or "?AspxAutoDetectCookieSupport=1"))
                    throw new InvalidDataException("Kentucky workflow redirected outside its official URL.");
                using var request = new HttpRequestMessage(method, uri);
                if (body is not null) request.Content = new FormUrlEncodedContent(body);
                var cookie = cookies.GetCookieHeader(uri);
                if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.RequestMessage?.RequestUri != uri)
                {
                    response.Dispose();
                    throw new InvalidDataException("Kentucky HTTP client automatically redirected; disable automatic redirects and cookies.");
                }
                if (response.Headers.TryGetValues("Set-Cookie", out var values))
                    foreach (var value in values) cookies.SetCookies(uri, value);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    var status = response.StatusCode;
                    response.Dispose();
                    if (location is null) throw new InvalidDataException("Kentucky redirect omitted its location.");
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
                    throw new HttpRequestException("Kentucky OOP request failed.", null, status);
                }
                return response;
            }
            throw new InvalidDataException("Kentucky workflow exceeded its redirect limit.");
        }
    }

    private static void SetUnrestricted(Dictionary<string, string> form)
    {
        form[Prefix + "ddlBoard"] = "31";
        form[Prefix + "ddlRegion"] = "0";
        form[Prefix + "ddlHours"] = "0";
        form[Prefix + "txtBusName"] = "";
        form[Prefix + "txtBusLocation"] = "";
    }

    private static void RequireChoice(Dictionary<string, Dictionary<string, string>> choices, string control, string value, string label)
    {
        if (!choices.TryGetValue(Prefix + control, out var offered) || offered.GetValueOrDefault(value) != label)
            throw new InvalidDataException("Kentucky official query controls changed.");
    }

    private static Dictionary<string, string> ReadForm(string html, Uri uri,
        out Dictionary<string, Dictionary<string, string>> choices)
    {
        var forms = Forms.Matches(html);
        if (forms.Count != 1) throw new InvalidDataException("Kentucky workflow omitted its official form.");
        var formAttrs = ReadAttributes(forms[0].Value);
        if (formAttrs.GetValueOrDefault("method")?.Equals("post", StringComparison.OrdinalIgnoreCase) != true
            || string.IsNullOrEmpty(formAttrs.GetValueOrDefault("action"))
            || !Uri.TryCreate(uri, formAttrs.GetValueOrDefault("action"), out var action)
            || action.Host != uri.Host || action.Scheme != uri.Scheme || !action.IsDefaultPort
            || action.UserInfo.Length != 0 || action.Fragment.Length != 0 || action.Query.Length != 0
            || !action.AbsolutePath.Equals(uri.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Kentucky workflow form action changed.");
        var form = new Dictionary<string, string>(StringComparer.Ordinal);
        var controls = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match input in Inputs.Matches(html))
        {
            var attrs = ReadAttributes(input.Value);
            if (!attrs.TryGetValue("name", out var name)) continue;
            if (!controls.Add(name)) throw new InvalidDataException("Kentucky form contains duplicate controls.");
            if (attrs.GetValueOrDefault("type")?.ToLowerInvariant() is "hidden" or "text")
                form.Add(name, attrs.GetValueOrDefault("value") ?? "");
        }
        if (string.IsNullOrEmpty(form.GetValueOrDefault("__VIEWSTATE"))
            || string.IsNullOrEmpty(form.GetValueOrDefault("__EVENTVALIDATION"))
            || !controls.Contains(Prefix + "btnSearch") || !controls.Contains(Prefix + "txtBusName")
            || !controls.Contains(Prefix + "txtBusLocation"))
            throw new InvalidDataException("Kentucky search omitted fresh ASP.NET state or query controls.");
        if (form.TryGetValue("__VIEWSTATEFIELDCOUNT", out var split)
            && (!int.TryParse(split, out var count) || count is < 1 or > 100
                || Enumerable.Range(1, count - 1).Any(index => !form.ContainsKey("__VIEWSTATE" + index))))
            throw new InvalidDataException("Kentucky search omitted split viewstate fields.");
        choices = new(StringComparer.Ordinal);
        foreach (Match select in Selects.Matches(html))
        {
            var name = ReadAttributes(select.Groups["attrs"].Value).GetValueOrDefault("name");
            if (name is null) continue;
            if (!controls.Add(name)) throw new InvalidDataException("Kentucky form contains duplicate controls.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            string? selected = null;
            foreach (Match option in Options.Matches(select.Groups["options"].Value))
            {
                var attrs = ReadAttributes(option.Groups["attrs"].Value);
                var value = attrs.GetValueOrDefault("value") ?? throw new InvalidDataException("Kentucky option omitted its value.");
                if (!options.TryAdd(value, WebUtility.HtmlDecode(option.Groups["text"].Value).Trim()))
                    throw new InvalidDataException("Kentucky form contains duplicate options.");
                if (attrs.ContainsKey("selected")) selected = value;
            }
            choices.Add(name, options);
            if (options.Count > 0) form.Add(name, selected ?? options.First().Key);
        }
        RequireChoice(choices, "ddlHours", "0", "Select Day");
        if (!choices.TryGetValue(Prefix + "ddlCounty", out var counties) || counties.Count != 0)
            throw new InvalidDataException("Kentucky unrestricted county control changed.");
        if (form.ContainsKey("__EVENTTARGET")) form["__EVENTTARGET"] = "";
        if (form.ContainsKey("__EVENTARGUMENT")) form["__EVENTARGUMENT"] = "";
        return form;
    }

    private static Dictionary<string, string> ReadAttributes(string tag) =>
        Attributes.Matches(tag).Cast<Match>().ToDictionary(match => match.Groups["key"].Value,
            match => WebUtility.HtmlDecode(match.Groups["value"].Value), StringComparer.OrdinalIgnoreCase);

    private static async Task<string> ReadHtmlAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const int limit = 32 * 1024 * 1024;
        if (response.Content.Headers.ContentType?.MediaType != "text/html" || response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException("Kentucky workflow did not return a bounded HTML page.");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (destination.Length + read > limit) throw new InvalidDataException("Kentucky workflow page exceeds its size limit.");
            destination.Write(buffer, 0, read);
        }
        if (response.Content.Headers.ContentLength is { } length && length != destination.Length)
            throw new InvalidDataException("Truncated Kentucky workflow page.");
        return new UTF8Encoding(false, true).GetString(destination.ToArray());
    }
}
