using System.Net;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class MichiganLaraBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    public string State => MichiganLaraBulkImportService.State;
    public string ArchiveFolder => "michigan-bulk";
    public string ParserVersion => MichiganLaraBulkImportService.ParserVersion;
    public string Layout => "MiPLUS current real-estate license and active-employer Open XML reports; published license identities";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => MichiganLaraBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(MichiganLaraBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        MichiganLaraBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct, Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<MichiganLaraBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        if (!Files.Contains(file)) throw new InvalidDataException("Unknown Michigan report.");
        var cookies = new CookieContainer();
        using (var parameter = await SendAsync(file.Url))
        {
            var html = await parameter.Content.ReadAsStringAsync(ct);
            var expected = file.Url.Replace("ReportParameter.aspx", "ShowReport.aspx", StringComparison.Ordinal);
            if (html.Length > 2_000_000 || !html.Contains(expected[(expected.LastIndexOf('/') + 1)..], StringComparison.Ordinal))
                throw new InvalidDataException("Michigan report workflow changed; the expected report redirect was not published.");
        }
        var reportUrl = file.Url.Replace("ReportParameter.aspx", "ShowReport.aspx", StringComparison.Ordinal);
        using var report = await SendAsync(reportUrl);
        if (report.Content.Headers.ContentType?.MediaType != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            throw new InvalidDataException("Michigan report did not return an Excel workbook.");
        const long maxBytes = 200L * 1024 * 1024;
        var expectedBytes = report.Content.Headers.ContentLength;
        if (expectedBytes > maxBytes) throw new InvalidDataException("Michigan report exceeds the 200 MiB limit.");
        await using var source = await report.Content.ReadAsStreamAsync(ct);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long bytes = 0;
        var lastProgress = DateTime.UtcNow;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            bytes += read;
            if (bytes > maxBytes) throw new InvalidDataException("Michigan report exceeds the 200 MiB limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            if (DateTime.UtcNow - lastProgress >= TimeSpan.FromSeconds(1))
            {
                progress(bytes, expectedBytes);
                lastProgress = DateTime.UtcNow;
            }
        }
        if (bytes == 0 || expectedBytes is { } expectedLength && expectedLength != bytes)
            throw new InvalidDataException("Incomplete Michigan report download.");
        progress(bytes, expectedBytes);
        return new(bytes, report.Content.Headers.LastModified, report.Headers.ETag?.ToString());

        async Task<HttpResponseMessage> SendAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var uri = new Uri(url);
            var cookie = cookies.GetCookieHeader(uri);
            if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.RequestMessage?.RequestUri?.AbsoluteUri != url || !response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException("Michigan report request failed or redirected away from its allowlisted URL.", null, status);
            }
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values) cookies.SetCookies(uri, value);
            return response;
        }
    }
}
