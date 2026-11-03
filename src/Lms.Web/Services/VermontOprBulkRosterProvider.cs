using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class VermontOprBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    public string State => VermontOprBulkImportService.State;
    public string ArchiveFolder => "vermont-bulk";
    public string ParserVersion => VermontOprBulkImportService.ParserVersion;
    public string Layout => "OPR Open XML rosters; all nine real-estate types and all four status filters; latest published effective-dated period per license; no inferred affiliations";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => VermontOprBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(VermontOprBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        VermontOprBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct, Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<VermontOprBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        if (!Files.Contains(file)) throw new InvalidDataException("Unknown Vermont roster category.");
        var cookies = new CookieContainer();
        var code = VermontOprBulkImportService.ProfessionCode(file.Name);
        var csrf = "";
        string html;
        using (var bootstrap = await SendAsync(HttpMethod.Get, new Uri(file.Url)))
            html = await ReadHtmlAsync(bootstrap);
        var postUri = PublishedUri(Match(html, "\"url\":\\s*\"([^\"]*pzTransactionId[^\"]*)\""));
        csrf = Match(html, "name='XCSRFToken' value='([^']+)'");
        var harness = Match(html, "\"pzHarnessID\":\\s*\"([^\"]+)\"");
        var rosterMode = RefreshFields(html, "FindAProfessionalWrapper", "UpdateLookupType,", harness, rosterMode: true);
        rosterMode["LookupType"] = "Roster Download";
        html = await PostAsync(rosterMode);
        foreach (var status in new[] { "Active", "Inactive per Request", "Expired", "Disciplined" })
            if (!html.Contains("\"$InternalRoasterStatuses$pyCaption\":\"" + status + "\"", StringComparison.Ordinal))
                throw new InvalidDataException("Vermont roster status filters changed.");
        var postValue = Match(html, "\"section\":\"NGLPRosterDownload\".*?\"secPostValueURL\":\"pzuiactionzzz=([^\"]+)\"", singleline: true);
        await SetValueAsync("$PpyDisplayHarness$pProductIds", "PC027");
        await SetValueAsync("$PpyDisplayHarness$pSetProfessionType", code);
        var selections = RefreshFields(html, "FindAProfessionalInput", "SetRosterDownloadStatuses,", harness);
        selections["$PpyDisplayHarness$pProductIds"] = "PC027";
        selections["$PpyDisplayHarness$pSetProfessionType"] = code;
        for (var i = 1; i <= 4; i++)
            selections["$PpyDisplayHarness$pEligibleStatusforRoster$l" + i + "$pSelectStatus"] = "true";
        html = await PostAsync(selections);
        if (Regex.Matches(html, "\"SelectStatus\":\"true\"", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)).Count != 4)
            throw new InvalidDataException("Vermont did not confirm all four requested status filters.");
        if (!html.Contains("\"ProductIds\":\"PC027\"", StringComparison.Ordinal)
            || !html.Contains("\"SetProfessionType\":\"" + code + "\"", StringComparison.Ordinal))
            throw new InvalidDataException("Vermont did not confirm the requested real-estate profession type.");
        var downloadUri = PublishedUri(Match(html, "openUrlInWindow\",\\s*\\[\"([^\"]*NGLPGenerateRosterDownload[^\"]*)\""));
        if (downloadUri.Query != "?pyActivity=@baseclass.pzProcessURLInWindow&pyPreActivity=NGLPGenerateRosterDownload&pzPrimaryPageName=pyDisplayHarness")
            throw new InvalidDataException("Vermont published download action changed.");
        await PostAsync(RefreshFields(html, "NGLPRosterDownloadResults", "SetRosterDownloadApplicant", harness));
        using var report = await SendAsync(HttpMethod.Get, downloadUri);
        if (report.Content.Headers.ContentType?.MediaType != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            throw new InvalidDataException("Vermont roster did not return an Excel workbook.");
        const long limit = 200L * 1024 * 1024;
        var length = report.Content.Headers.ContentLength;
        if (length > limit) throw new InvalidDataException("Vermont roster exceeds the 200 MiB limit.");
        var created = false;
        try
        {
            await using var source = await report.Content.ReadAsStreamAsync(ct);
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            created = true;
            var buffer = new byte[81920];
            long bytes = 0;
            var lastProgress = DateTime.UtcNow;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                bytes += read;
                if (bytes > limit) throw new InvalidDataException("Vermont roster exceeds the 200 MiB limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                if (DateTime.UtcNow - lastProgress >= TimeSpan.FromSeconds(1))
                {
                    progress(bytes, length);
                    lastProgress = DateTime.UtcNow;
                }
            }
            if (bytes == 0 || length is { } expected && expected != bytes)
                throw new InvalidDataException("Incomplete Vermont roster download.");
            progress(bytes, length);
            return new(bytes, report.Content.Headers.LastModified, report.Headers.ETag?.ToString());
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }

        async Task SetValueAsync(string target, string value)
        {
            var responseHtml = await PostAsync(new()
            {
                ["pzuiactionzzz"] = postValue, [target] = value, ["pyPropertyTarget"] = target,
                ["updateDOM"] = "true", ["BaseReference"] = "", ["ContextPage"] = "",
                ["pzKeepPageMessages"] = "true", ["UITemplatingStatus"] = "Y", ["PVClientVal"] = "true",
                ["inStandardsMode"] = "true", ["pzHarnessID"] = harness
            });
            var property = target.EndsWith("pProductIds", StringComparison.Ordinal) ? "ProductIds" : "SetProfessionType";
            if (!responseHtml.Contains("\"" + property + "\":\"" + value + "\"", StringComparison.Ordinal))
                throw new InvalidDataException("Vermont did not confirm the posted profession selection.");
        }
        async Task<string> PostAsync(Dictionary<string, string> fields)
        {
            using var response = await SendAsync(HttpMethod.Post, postUri, fields);
            return await ReadHtmlAsync(response);
        }
        async Task<string> ReadHtmlAsync(HttpResponseMessage response)
        {
            if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
                throw new InvalidDataException("Vermont guest workflow did not return HTML.");
            const int htmlLimit = 2_000_000;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var block = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(block, ct)) > 0)
            {
                if (buffer.Length + read > htmlLimit) throw new InvalidDataException("Vermont guest workflow HTML exceeds its size limit.");
                buffer.Write(block, 0, read);
            }
            var text = Encoding.UTF8.GetString(buffer.ToArray());
            if (text.Contains("<title>Status Page</title>", StringComparison.Ordinal)
                || text.Contains("captcha", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Vermont guest workflow requires an unavailable session or access verification.");
            return DecodeHtml(text);
        }
        async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, Dictionary<string, string>? fields = null)
        {
            for (var redirects = 0; redirects <= 5; redirects++)
            {
                EnsureAllowed(uri);
                using var request = new HttpRequestMessage(method, uri);
                var cookie = cookies.GetCookieHeader(uri);
                if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
                if (fields is not null)
                {
                    request.Content = new FormUrlEncodedContent(fields);
                    request.Headers.Add("X-CSRF-Token", csrf);
                    request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                }
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.RequestMessage?.RequestUri != uri)
                {
                    response.Dispose();
                    throw new HttpRequestException("Vermont client must disable automatic redirects and shared cookies.");
                }
                if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                    foreach (var value in setCookies) cookies.SetCookies(uri, value);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var next = response.Headers.Location is { } location ? new Uri(uri, location) : null;
                    response.Dispose();
                    if (next is null || method != HttpMethod.Get) throw new HttpRequestException("Unexpected Vermont workflow redirect.");
                    uri = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    var status = response.StatusCode;
                    response.Dispose();
                    throw new HttpRequestException("Vermont roster request failed.", null, status);
                }
                return response;
            }
            throw new HttpRequestException("Too many Vermont guest workflow redirects.");
        }
    }

    private static string DecodeHtml(string text)
    {
        var start = text.IndexOf("\\u", StringComparison.Ordinal);
        if (start >= 0)
        {
            var decoded = new StringBuilder(text.Length);
            decoded.Append(text.AsSpan(0, start));
            for (var i = start; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 5 < text.Length && text[i + 1] == 'u'
                    && ushort.TryParse(text.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var character))
                {
                    decoded.Append((char)character);
                    i += 5;
                }
                else decoded.Append(text[i]);
            }
            text = decoded.ToString();
        }
        return WebUtility.HtmlDecode(text.Replace("\\\"", "\"", StringComparison.Ordinal));
    }

    private static Uri PublishedUri(string value)
    {
        var uri = new Uri(new Uri(VermontOprBulkImportService.GuestUrl), value);
        EnsureAllowed(uri);
        return uri;
    }
    private static void EnsureAllowed(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "secure.professionals.vermont.gov" || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0
            || !uri.AbsolutePath.StartsWith("/prweb/PRServletCustom/", StringComparison.Ordinal))
            throw new InvalidDataException("Vermont workflow attempted a request outside its official guest portal.");
    }
    private static string Match(string html, string pattern, bool singleline = false)
    {
        var match = Regex.Match(html, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
            | (singleline ? RegexOptions.Singleline : RegexOptions.None), TimeSpan.FromSeconds(1));
        return match.Success && match.Groups[1].Value.Length > 0 ? match.Groups[1].Value
            : throw new InvalidDataException("Vermont published guest workflow metadata changed.");
    }
    private static Dictionary<string, string> RefreshFields(string html, string section, string activity, string harness, bool rosterMode = false)
    {
        string? token = null;
        foreach (Match match in Regex.Matches(html, "data-(?:click|change)='(\\[\\[[^']+)'",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)))
        {
            if (!match.Groups[1].Value.Contains("\"" + section + "\"", StringComparison.Ordinal)
                || !match.Groups[1].Value.Contains(activity, StringComparison.Ordinal)) continue;
            using var document = JsonDocument.Parse(match.Groups[1].Value);
            foreach (var action in document.RootElement.EnumerateArray())
            {
                if (action[0].GetString() != "refresh") continue;
                var args = action[1];
                if (args[1].GetString() != section || args[2].GetString() != activity && args[5].GetString() != activity) continue;
                if (rosterMode && (!args[8][2].TryGetProperty("LookupType", out var lookup) || lookup.GetString() != "Roster+Download")) continue;
                token = args[8][0].GetString();
                break;
            }
            if (token is not null) break;
        }
        if (token is null || !token.StartsWith("pzuiactionzzz=", StringComparison.Ordinal))
            throw new InvalidDataException("Vermont published roster action changed.");
        var sid = Match(html, "\"section\":\"" + Regex.Escape(section) + "\".{0,200}\"UID\":\"([^\"]+)\"");
        return new()
        {
            ["pzuiactionzzz"] = token["pzuiactionzzz=".Length..], ["PreActivitiesList"] = "", ["ActivityParams"] = "=",
            ["PreActivityContextPageList"] = "$NULL$", ["pzKeepPageMessages"] = "false",
            ["strPHarnessClass"] = "SoV-License-NGLP-Work-License", ["strPHarnessPurpose"] = "NGLPFindAProfessionalExternal",
            ["BaseReference"] = "", ["StreamList"] = section + "|Rule-HTML-Section||||Y|" + sid + "|:",
            ["bClientValidation"] = "true", ["HeaderButtonSectionName"] = "-1", ["ReadOnly"] = "-1",
            ["PagesToRemove"] = "", ["pzHarnessID"] = harness, ["inStandardsMode"] = "true"
        };
    }
}
