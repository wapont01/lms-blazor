using System.Net;
using System.Text;
using System.Text.Json;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class WyomingWrecBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    public const long MaxResponseBytes = WyomingWrecBulkImportService.MaxResponseBytes;
    public string State => WyomingWrecBulkImportService.State;
    public string ArchiveFolder => "wyoming-bulk";
    public string ParserVersion => WyomingWrecBulkImportService.ParserVersion;
    public string Layout => "Official WREC anonymous individual (including appraisal), firm (2) and AMC (1) POST queries; "
        + "all statuses, exact 67-property records and original envelopes retained; two complete stable paging passes";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => WyomingWrecBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(WyomingWrecBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct,
        Action<string, long, long>? progress) => WyomingWrecBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<WyomingWrecBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        if (!Files.Contains(file)) throw new InvalidDataException("Unknown Wyoming official query.");
        if (client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Contains("Cookie"))
            throw new InvalidDataException("Wyoming acquisition must use a fresh anonymous HTTP client.");
        // The collection client is configured with UseCookies=false and AllowAutoRedirect=false.
        // The public endpoint needs neither browser state nor authentication.
        var query = WyomingWrecBulkImportService.SearchPayload(file.Name);
        var pageSize = WyomingWrecBulkImportService.DefaultPageSize;
        var payload = query.GetRawText();
        long received = 0;
        var owned = false;
        try
        {
            var first = await CollectAsync();
            var verified = await CollectAsync();
            if (first.Total != verified.Total || first.Hashes.Count != verified.Hashes.Count
                || first.Hashes.Any(pair => !verified.Hashes.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
                throw new InvalidDataException("Wyoming repeated collection count, identity or content changed.");
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
            {
                owned = true;
                using var bounded = new BoundedArchiveStream(output);
                await JsonSerializer.SerializeAsync(bounded, new
                {
                    format = WyomingWrecBulkImportService.ArchiveFormat, file = file.Name, page_size = pageSize,
                    query, pages = first.Pages, verification_pages = verified.Pages
                }, cancellationToken: ct);
                if (output.Length > WyomingWrecBulkImportService.MaxFileBytes)
                    throw new InvalidDataException("Wyoming acquisition archive exceeds its byte limit.");
            }
            if (WyomingWrecBulkImportService.CheckFile(path, file.Name, ct) != first.Total)
                throw new InvalidDataException("Wyoming written archive is incomplete.");
            var bytes = new FileInfo(path).Length;
            progress(bytes, bytes);
            return new(bytes, null, null);
        }
        catch
        {
            if (owned && File.Exists(path)) File.Delete(path);
            throw;
        }

        async Task<(int Total, List<object> Pages, Dictionary<string, string> Hashes)> CollectAsync()
        {
            int? total = null;
            var captures = new List<object>();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var page = 1; page <= WyomingWrecBulkImportService.MaxPages; page++)
            {
                ct.ThrowIfCancellationRequested();
                var url = WyomingWrecBulkImportService.PageUrl(page, pageSize);
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload, new UTF8Encoding(false, true), "application/json")
                };
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode != HttpStatusCode.OK
                    || response.RequestMessage?.RequestUri is { } finalUri && finalUri.AbsoluteUri != url
                    || response.Content.Headers.ContentType?.MediaType is not { } mediaType
                    || !(mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                    || response.Content.Headers.ContentLength is <= 0 or > MaxResponseBytes)
                    throw new InvalidDataException("Wyoming API HTTP status, redirect, media type or byte limit changed.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var body = new MemoryStream();
                var buffer = new byte[64 * 1024];
                int count;
                while ((count = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    if (body.Length + count > MaxResponseBytes
                        || received + count > WyomingWrecBulkImportService.MaxFileBytes / 2)
                        throw new InvalidDataException("Wyoming response or total acquisition exceeds its bounded byte limit.");
                    body.Write(buffer, 0, count);
                    received += count;
                    progress(received, null);
                }
                if (body.Length == 0 || response.Content.Headers.ContentLength is { } declared && body.Length != declared)
                    throw new InvalidDataException("Wyoming response is empty or truncated.");
                string json;
                try { json = new UTF8Encoding(false, true).GetString(body.GetBuffer(), 0, checked((int)body.Length)).TrimStart('\uFEFF'); }
                catch (DecoderFallbackException ex) { throw new InvalidDataException("Wyoming response is not valid UTF-8.", ex); }
                var at = DateTimeOffset.UtcNow;
                var result = WyomingWrecBulkImportService.ReadPage(json, file.Name, page, pageSize, at, query);
                total ??= result.Total;
                if (result.Total != total) throw new InvalidDataException("Wyoming published count changed during paging.");
                foreach (var pair in WyomingWrecBulkImportService.Fingerprints(result.Rows))
                    if (!hashes.TryAdd(pair.Key, pair.Value))
                        throw new InvalidDataException("Wyoming pages overlap or conflict on a published identity.");
                captures.Add(new { page_number = page, url, captured_at_utc = at, response_json = json });
                if (hashes.Count == total) return (total.Value, captures, hashes);
                if (hashes.Count > total) throw new InvalidDataException("Wyoming paging returned too many records.");
            }
            throw new InvalidDataException("Wyoming acquisition exceeded its maximum page count.");
        }
    }

    private sealed class BoundedArchiveStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count); inner.Write(buffer, offset, count);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Check(count); return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Check(buffer.Length); return inner.WriteAsync(buffer, cancellationToken);
        }
        private void Check(int count)
        {
            if (inner.Position + count > WyomingWrecBulkImportService.MaxFileBytes)
                throw new InvalidDataException("Wyoming acquisition archive exceeds its byte limit.");
        }
    }
}
