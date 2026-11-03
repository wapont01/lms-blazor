using Lms.Application.Services;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Web.Services;

public interface IBulkRosterProvider
{
    string State { get; }
    string ArchiveFolder { get; }
    string ParserVersion { get; }
    string Layout { get; }
    IReadOnlyList<FloridaBulkFileDefinition> Files { get; }
    string? RowCountUrl { get; }
    Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct);
    Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null);
    Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress);
    Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged);
}

public sealed record BulkRosterDownloadResult(long Bytes, DateTimeOffset? SourceLastModifiedUtc, string? SourceETag);

public interface IBulkRosterFileDownloader
{
    Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress);
}

public sealed class FloridaBulkRosterProvider : IBulkRosterProvider
{
    public string State => "Florida";
    public string ArchiveFolder => "florida-bulk";
    public string ParserVersion => FloridaBulkImportService.ParserVersion;
    public string Layout => "Quoted DBPR physical rows; literal interior quotes preserved";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => FloridaBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public async Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null)
    {
        long rows = 0;
        await foreach (var fields in FloridaBulkImportService.ReadFieldsAsync(path, file.Columns, ct))
        {
            FloridaBulkImportService.ParseRow(file, fields);
            rows++;
            if (rows % 10_000 == 0) progress?.Invoke(rows);
        }
        return rows;
    }
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        FloridaBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<FloridaBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

public sealed class NevadaBulkRosterProvider : IBulkRosterProvider
{
    public string State => "Nevada";
    public string ArchiveFolder => "nevada-bulk";
    public string ParserVersion => NevadaNredBulkImportService.ParserVersion;
    public string Layout => "NRED Open XML active-license workbooks; all 12 supported source lists retained; HOA rows are source evidence only";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => NevadaNredBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(NevadaNredBulkImportService.CheckFile(path, file.Name, ct));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        NevadaNredBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<NevadaNredBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

public sealed class TexasBulkRosterProvider : IBulkRosterProvider
{
    public string State => "Texas";
    public string ArchiveFolder => "texas-bulk";
    public string ParserVersion => TexasBulkImportService.ParserVersion;
    public string Layout => "UTF-8 standard CSV with a named header; all published statuses; suffix-preserving license identities";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => TexasBulkImportService.Definitions;
    public string? RowCountUrl => TexasBulkImportService.RowCountUrl;
    public async Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct)
    {
        using var client = clients.CreateClient("TexasBulk");
        using var response = await client.GetAsync(TexasBulkImportService.RowCountUrl, ct);
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != TexasBulkImportService.RowCountUrl)
            throw new InvalidDataException("Texas source-count request redirected away from its allowlisted URL.");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != 1
            || !json.RootElement[0].TryGetProperty("count", out var count) || count.ValueKind != JsonValueKind.String
            || !long.TryParse(count.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var rows) || rows <= 0)
            throw new InvalidDataException("Texas official source count is invalid.");
        return rows;
    }

    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult((long)TexasBulkImportService.ReadRecords(path, ct, progress).Count);
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        TexasBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<TexasBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

public sealed class CaliforniaBulkRosterProvider : IBulkRosterProvider
{
    public string State => "California";
    public string ArchiveFolder => "california-bulk";
    public string ParserVersion => CaliforniaBulkImportService.ParserVersion;
    public string Layout => "DRE Windows-1252 current-list CSV in ZIP and BIFF XLS broker-associates; roles and source relationships preserved; examinee lists excluded";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => CaliforniaBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(CaliforniaBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        CaliforniaBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<CaliforniaBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

public sealed class ArizonaAdreBulkRosterProvider : IBulkRosterProvider
{
    public string State => ArizonaAdreBulkImportService.State;
    public string ArchiveFolder => "arizona-adre-bulk";
    public string ParserVersion => ArizonaAdreBulkImportService.ParserVersion;
    public string Layout => "ADRE on-demand UTF-8 CSV lists (22-column individuals, 18-column entities); Real Estate category imported with all statuses; cemetery and campground rows retained only in the source files";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => ArizonaAdreBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(ArizonaAdreBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        ArizonaAdreBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<ArizonaAdreBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

public abstract class SocrataBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    private const int PageSize = 50_000;
    public abstract string State { get; }
    public abstract string ArchiveFolder { get; }
    public abstract string ParserVersion { get; }
    public abstract string Layout { get; }
    public abstract IReadOnlyList<FloridaBulkFileDefinition> Files { get; }
    public abstract string? RowCountUrl { get; }
    protected abstract string SourceLabel { get; }
    // Matches the named client the collection service creates for downloads.
    private string ClientName => State + "Bulk";
    protected abstract string SourceUrl { get; }
    protected abstract string OrderBy { get; }
    protected abstract IReadOnlyList<string> Headers { get; }

    public async Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct)
    {
        using var client = clients.CreateClient(ClientName);
        return await GetPublishedRowsAsync(client, ct);
    }

    private async Task<long?> GetPublishedRowsAsync(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync(RowCountUrl, ct);
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != RowCountUrl)
            throw new InvalidDataException($"{SourceLabel} source-count request redirected away from its allowlisted URL.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != 1
            || !document.RootElement[0].TryGetProperty("count", out var count))
            throw new InvalidDataException($"{SourceLabel} source count response is invalid.");
        var value = count.ValueKind == JsonValueKind.String ? count.GetString()
            : count.ValueKind == JsonValueKind.Number ? count.GetRawText() : null;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rows) || rows <= 0
            || rows > 1_000_000)
            throw new InvalidDataException($"{SourceLabel} published source count is outside the supported range.");
        return rows;
    }

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        if (file != Files[0]) throw new InvalidDataException($"Unknown {SourceLabel} source file.");
        var rowCount = await GetPublishedRowsAsync(client, ct)
            ?? throw new InvalidDataException($"{SourceLabel} did not publish a source row count.");
        DateTimeOffset? lastModified = null;
        string? eTag = null;
        long bytes = 0;
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        for (long offset = 0; offset < rowCount; offset += PageSize)
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{SourceUrl}?$limit={PageSize}&$offset={offset}&$order={OrderBy}";
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.RequestMessage?.RequestUri?.AbsoluteUri != url)
                throw new InvalidDataException($"{SourceLabel} data request redirected away from its allowlisted URL.");
            response.EnsureSuccessStatusCode();
            if (!TryGetTruthLastModified(response, out var pageModified))
                throw new InvalidDataException($"{SourceLabel} response omitted its source modification timestamp.");
            if (lastModified is { } known && known != pageModified)
                throw new InvalidDataException($"{SourceLabel} source changed while its paged export was being collected.");
            lastModified = pageModified;
            eTag ??= response.Headers.ETag?.ToString();

            var pagePath = path + $".page-{offset}.tmp";
            try
            {
                await using (var page = new FileStream(pagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await using (var responseStream = await response.Content.ReadAsStreamAsync(ct))
                    await responseStream.CopyToAsync(page, ct);

                using var parser = new TextFieldParser(pagePath, new UTF8Encoding(false, true))
                {
                    TextFieldType = FieldType.Delimited,
                    HasFieldsEnclosedInQuotes = true,
                    TrimWhiteSpace = false
                };
                parser.SetDelimiters(",");
                var headers = parser.ReadFields() ?? throw new InvalidDataException($"{SourceLabel} page has no CSV header.");
                if (!headers.Select(value => value.TrimStart('\uFEFF'))
                    .SequenceEqual(Headers, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{SourceLabel} page schema changed during collection.");

                var pageRows = 0;
                while (!parser.EndOfData)
                {
                    ct.ThrowIfCancellationRequested();
                    var fields = parser.ReadFields() ?? throw new InvalidDataException($"{SourceLabel} page contains an empty row.");
                    if (fields.Length != Headers.Count)
                        throw new InvalidDataException($"Invalid {SourceLabel} page row shape near line {parser.LineNumber}.");
                    if (offset == 0 && pageRows == 0)
                        await WriteCsvRowAsync(writer, Headers, ct);
                    await WriteCsvRowAsync(writer, fields, ct);
                    pageRows++;
                    if (pageRows > PageSize)
                        throw new InvalidDataException($"{SourceLabel} page exceeded the requested row limit.");
                }

                var expectedPageRows = (int)Math.Min(PageSize, rowCount - offset);
                if (pageRows != expectedPageRows)
                    throw new InvalidDataException($"{SourceLabel} page at offset {offset} contained {pageRows} rows; expected {expectedPageRows}.");
                if (offset == 0 && pageRows == 0)
                    await WriteCsvRowAsync(writer, Headers, ct);
                await writer.FlushAsync(ct);
                bytes = destination.Length;
                progress(bytes, null);
            }
            finally
            {
                if (File.Exists(pagePath)) File.Delete(pagePath);
            }
        }

        await writer.FlushAsync(ct);
        bytes = destination.Length;
        return new(bytes, lastModified, eTag);
    }

    private static bool TryGetTruthLastModified(HttpResponseMessage response, out DateTimeOffset modified)
    {
        modified = default;
        return response.Headers.TryGetValues("X-SODA2-Truth-Last-Modified", out var values)
            && DateTimeOffset.TryParse(values.SingleOrDefault(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out modified);
    }

    private static async Task WriteCsvRowAsync(TextWriter writer, IEnumerable<string> values, CancellationToken ct)
    {
        var row = string.Join(',', values.Select(value =>
            "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
        await writer.WriteLineAsync(row.AsMemory(), ct);
    }

    public abstract Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct,
        Action<long>? progress = null);
    public abstract Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct,
        Action<string, long, long>? progress);
    public abstract Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged);
}

public sealed class ColoradoDoraBulkRosterProvider : SocrataBulkRosterProvider
{
    public override string State => "Colorado";
    public override string ArchiveFolder => "colorado-dora-bulk";
    public override string ParserVersion => ColoradoDoraBulkImportService.ParserVersion;
    public override string Layout => "UTF-8 CSV with the 20-column DORA 4zse-6bnw schema; complete source rows retained";
    public override IReadOnlyList<FloridaBulkFileDefinition> Files => ColoradoDoraBulkImportService.Definitions;
    public override string? RowCountUrl => ColoradoDoraBulkImportService.RowCountUrl;
    protected override string SourceLabel => "DORA";
    protected override string SourceUrl => ColoradoDoraBulkImportService.SourceUrl;
    protected override string OrderBy => "lastname,firstname,middlename,suffix,entityname,city,state,zipcode,licenseprefix,licensenumber,licensetype,licensefirstissuedate,licenselastreneweddate,licenseexpirationdate,licensestatus,casenumber,programaction,disciplineeffectivedate,disciplinecompletedate,verifylicense";
    protected override IReadOnlyList<string> Headers => ColoradoDoraBulkImportService.Headers;

    public override Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct,
        Action<long>? progress = null)
    {
        if (file != Files[0]) throw new InvalidDataException("Unknown Colorado DORA source file.");
        return Task.FromResult((long)ColoradoDoraBulkImportService.ReadRecords(path, ct, progress).Count);
    }

    public override Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct,
        Action<string, long, long>? progress) =>
        ColoradoDoraBulkImportService.ValidateArchiveAsync(root, ct, progress);

    public override Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<ColoradoDoraBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

public sealed class NewYorkDosBulkRosterProvider : SocrataBulkRosterProvider
{
    public override string State => NewYorkDosBulkImportService.State;
    public override string ArchiveFolder => "new-york-dos-bulk";
    public override string ParserVersion => NewYorkDosBulkImportService.ParserVersion;
    public override string Layout => "UTF-8 CSV with the 11-column NY DOS yg7h-zjbf schema; complete source rows retained";
    public override IReadOnlyList<FloridaBulkFileDefinition> Files => NewYorkDosBulkImportService.Definitions;
    public override string? RowCountUrl => NewYorkDosBulkImportService.RowCountUrl;
    protected override string SourceLabel => "NY DOS";
    protected override string SourceUrl => NewYorkDosBulkImportService.SourceUrl;
    protected override string OrderBy => "license_type,license_number,license_holder_name,business_name,business_address_1,business_address_2,business_city,business_state,business_zip,county,license_expiration_date";
    protected override IReadOnlyList<string> Headers => NewYorkDosBulkImportService.Headers;

    public override Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct,
        Action<long>? progress = null)
    {
        if (file != Files[0]) throw new InvalidDataException("Unknown New York DOS source file.");
        return Task.FromResult((long)NewYorkDosBulkImportService.ReadRecords(path, ct, progress).Count);
    }

    public override Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct,
        Action<string, long, long>? progress) =>
        NewYorkDosBulkImportService.ValidateArchiveAsync(root, ct, progress);

    public override Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<NewYorkDosBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}
