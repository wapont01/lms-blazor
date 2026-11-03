using Lms.Application.Services;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Web.Services;

public interface IBulkRosterProvider
{
    bool SupportsDownload => true;
    string State { get; }
    string ArchiveFolder { get; }
    string ParserVersion { get; }
    string Layout { get; }
    IReadOnlyList<FloridaBulkFileDefinition> Files { get; }
    string? RowCountUrl { get; }
    Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct);
    string? GetRowCountUrl(FloridaBulkFileDefinition file) => RowCountUrl;
    Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, FloridaBulkFileDefinition file, CancellationToken ct) =>
        GetPublishedRowsAsync(clients, ct);
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

/// <summary>One Socrata dataset exported page by page into one archive file.</summary>
public sealed record SocrataDataset(FloridaBulkFileDefinition File, string SourceUrl, string RowCountUrl, string OrderBy,
    IReadOnlyList<string> Headers, string? Where = null);

public abstract class SocrataBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    private const int PageSize = 50_000;
    public abstract string State { get; }
    public abstract string ArchiveFolder { get; }
    public abstract string ParserVersion { get; }
    public abstract string Layout { get; }
    public IReadOnlyList<FloridaBulkFileDefinition> Files => Datasets.Select(dataset => dataset.File).ToList();
    protected abstract IReadOnlyList<SocrataDataset> Datasets { get; }
    protected abstract string SourceLabel { get; }
    protected virtual long MaximumRows => 1_000_000;
    // Matches the named client the collection service creates for downloads.
    private string ClientName => State + "Bulk";

    public string? RowCountUrl => Datasets.Count == 1 ? Datasets[0].RowCountUrl : null;
    public string? GetRowCountUrl(FloridaBulkFileDefinition file) => Dataset(file).RowCountUrl;

    private SocrataDataset Dataset(FloridaBulkFileDefinition file) =>
        Datasets.SingleOrDefault(dataset => dataset.File == file)
            ?? throw new InvalidDataException($"Unknown {SourceLabel} source file.");

    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) =>
        Datasets.Count == 1 ? GetPublishedRowsAsync(clients, Datasets[0].File, ct) : Task.FromResult<long?>(null);

    public async Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, FloridaBulkFileDefinition file, CancellationToken ct)
    {
        using var client = clients.CreateClient(ClientName);
        return await GetPublishedRowsAsync(client, Dataset(file), ct);
    }

    private async Task<long?> GetPublishedRowsAsync(HttpClient client, SocrataDataset dataset, CancellationToken ct)
    {
        var rowCountUrl = dataset.RowCountUrl;
        using var response = await client.GetAsync(rowCountUrl, ct);
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != rowCountUrl)
            throw new InvalidDataException($"{SourceLabel} source-count request redirected away from its allowlisted URL.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != 1
            || !document.RootElement[0].TryGetProperty("count", out var count))
            throw new InvalidDataException($"{SourceLabel} source count response is invalid.");
        var value = count.ValueKind == JsonValueKind.String ? count.GetString()
            : count.ValueKind == JsonValueKind.Number ? count.GetRawText() : null;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rows) || rows <= 0
            || rows > MaximumRows)
            throw new InvalidDataException($"{SourceLabel} published source count is outside the supported range.");
        return rows;
    }

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        var dataset = Dataset(file);
        var headers = dataset.Headers;
        var rowCount = await GetPublishedRowsAsync(client, dataset, ct)
            ?? throw new InvalidDataException($"{SourceLabel} did not publish a source row count.");
        DateTimeOffset? lastModified = null;
        string? eTag = null;
        long bytes = 0;
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        for (long offset = 0; offset < rowCount; offset += PageSize)
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{dataset.SourceUrl}?$limit={PageSize}&$offset={offset}&$order={dataset.OrderBy}"
                + (dataset.Where is null ? "" : "&$where=" + dataset.Where);
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
                var pageHeaders = parser.ReadFields() ?? throw new InvalidDataException($"{SourceLabel} page has no CSV header.");
                if (!pageHeaders.Select(value => value.TrimStart('\uFEFF'))
                    .SequenceEqual(headers, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{SourceLabel} page schema changed during collection.");

                var pageRows = 0;
                while (!parser.EndOfData)
                {
                    ct.ThrowIfCancellationRequested();
                    var fields = parser.ReadFields() ?? throw new InvalidDataException($"{SourceLabel} page contains an empty row.");
                    if (fields.Length != headers.Count)
                        throw new InvalidDataException($"Invalid {SourceLabel} page row shape near line {parser.LineNumber}.");
                    if (offset == 0 && pageRows == 0)
                        await WriteCsvRowAsync(writer, headers, ct);
                    await WriteCsvRowAsync(writer, fields, ct);
                    pageRows++;
                    if (pageRows > PageSize)
                        throw new InvalidDataException($"{SourceLabel} page exceeded the requested row limit.");
                }

                var expectedPageRows = (int)Math.Min(PageSize, rowCount - offset);
                if (pageRows != expectedPageRows)
                    throw new InvalidDataException($"{SourceLabel} page at offset {offset} contained {pageRows} rows; expected {expectedPageRows}.");
                if (offset == 0 && pageRows == 0)
                    await WriteCsvRowAsync(writer, headers, ct);
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
    protected override IReadOnlyList<SocrataDataset> Datasets { get; } =
    [
        new(ColoradoDoraBulkImportService.Definitions[0], ColoradoDoraBulkImportService.SourceUrl,
            ColoradoDoraBulkImportService.RowCountUrl,
            "lastname,firstname,middlename,suffix,entityname,city,state,zipcode,licenseprefix,licensenumber,licensetype,licensefirstissuedate,licenselastreneweddate,licenseexpirationdate,licensestatus,casenumber,programaction,disciplineeffectivedate,disciplinecompletedate,verifylicense",
            ColoradoDoraBulkImportService.Headers)
    ];
    protected override string SourceLabel => "DORA";

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
    protected override IReadOnlyList<SocrataDataset> Datasets { get; } =
    [
        new(NewYorkDosBulkImportService.Definitions[0], NewYorkDosBulkImportService.SourceUrl,
            NewYorkDosBulkImportService.RowCountUrl,
            "license_type,license_number,license_holder_name,business_name,business_address_1,business_address_2,business_city,business_state,business_zip,county,license_expiration_date",
            NewYorkDosBulkImportService.Headers)
    ];
    protected override string SourceLabel => "NY DOS";

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

public sealed class ConnecticutDcpBulkRosterProvider : SocrataBulkRosterProvider
{
    public override string State => ConnecticutDcpBulkImportService.State;
    public override string ArchiveFolder => "connecticut-dcp-bulk";
    public override string ParserVersion => ConnecticutDcpBulkImportService.ParserVersion;
    public override string Layout => "UTF-8 CSV exports of CT DCP datasets 7y8e-cawe (salespersons, 20 columns), fwpc-pgqj (brokers, 20 columns) and 6tja-6vdt (salesperson-broker associations, 4 columns); complete source rows retained";
    protected override IReadOnlyList<SocrataDataset> Datasets { get; } =
    [
        new(ConnecticutDcpBulkImportService.Definitions[0], ConnecticutDcpBulkImportService.SalespersonsUrl,
            ConnecticutDcpBulkImportService.SalespersonsRowCountUrl, ConnecticutDcpBulkImportService.CredentialOrderBy,
            ConnecticutDcpBulkImportService.CredentialHeaders),
        new(ConnecticutDcpBulkImportService.Definitions[1], ConnecticutDcpBulkImportService.BrokersUrl,
            ConnecticutDcpBulkImportService.BrokersRowCountUrl, ConnecticutDcpBulkImportService.CredentialOrderBy,
            ConnecticutDcpBulkImportService.CredentialHeaders),
        new(ConnecticutDcpBulkImportService.Definitions[2], ConnecticutDcpBulkImportService.AssociationsUrl,
            ConnecticutDcpBulkImportService.AssociationsRowCountUrl, ConnecticutDcpBulkImportService.AssociationOrderBy,
            ConnecticutDcpBulkImportService.AssociationHeaders)
    ];
    protected override string SourceLabel => "CT DCP";

    public override Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct,
        Action<long>? progress = null) =>
        Task.FromResult(ConnecticutDcpBulkImportService.CheckFile(path, file.Name, ct, progress));

    public override Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct,
        Action<string, long, long>? progress) =>
        ConnecticutDcpBulkImportService.ValidateArchiveAsync(root, ct, progress);

    public override Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<ConnecticutDcpBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

public sealed class DelawareDprBulkRosterProvider : SocrataBulkRosterProvider
{
    public override string State => DelawareDprBulkImportService.State;
    public override string ArchiveFolder => "delaware-dpr-bulk";
    public override string ParserVersion => DelawareDprBulkImportService.ParserVersion;
    public override string Layout => "UTF-8 CSV export of DE DPR dataset pjnv-eaih (15 columns) filtered to the Real Estate and Real Estate Appraisers professions; complete source rows retained";
    protected override IReadOnlyList<SocrataDataset> Datasets { get; } =
    [
        new(DelawareDprBulkImportService.Definitions[0], DelawareDprBulkImportService.SourceUrl,
            DelawareDprBulkImportService.RowCountUrl, DelawareDprBulkImportService.OrderBy,
            DelawareDprBulkImportService.Headers, DelawareDprBulkImportService.Where)
    ];
    protected override string SourceLabel => "DE DPR";

    public override Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct,
        Action<long>? progress = null)
    {
        if (file != Files[0]) throw new InvalidDataException("Unknown Delaware DPR source file.");
        return Task.FromResult(DelawareDprBulkImportService.CheckFile(path, ct, progress));
    }

    public override Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct,
        Action<string, long, long>? progress) =>
        DelawareDprBulkImportService.ValidateArchiveAsync(root, ct, progress);

    public override Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<DelawareDprBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

public sealed class IllinoisIdfprBulkRosterProvider : SocrataBulkRosterProvider
{
    public override string State => IllinoisIdfprBulkImportService.State;
    public override string ArchiveFolder => "illinois-idfpr-bulk";
    public override string ParserVersion => IllinoisIdfprBulkImportService.ParserVersion;
    public override string Layout => "UTF-8 CSV export of IL IDFPR dataset pzzh-kp68 (30 columns) filtered to REAL ESTATE and APPRAISAL, excluding course approvals; all statuses and disciplinary source rows retained";
    protected override IReadOnlyList<SocrataDataset> Datasets { get; } =
    [
        new(IllinoisIdfprBulkImportService.Definitions[0], IllinoisIdfprBulkImportService.SourceUrl,
            IllinoisIdfprBulkImportService.RowCountUrl, IllinoisIdfprBulkImportService.OrderBy,
            IllinoisIdfprBulkImportService.Headers, IllinoisIdfprBulkImportService.Where)
    ];
    protected override string SourceLabel => "IL IDFPR";
    protected override long MaximumRows => 2_000_000;

    public override Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct,
        Action<long>? progress = null)
    {
        if (file != Files[0]) throw new InvalidDataException("Unknown Illinois IDFPR source file.");
        return Task.FromResult(IllinoisIdfprBulkImportService.CheckFile(path, ct, progress));
    }

    public override Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct,
        Action<string, long, long>? progress) =>
        IllinoisIdfprBulkImportService.ValidateArchiveAsync(root, ct, progress);

    public override Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<IllinoisIdfprBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

/// <summary>DPOR publishes static tab-delimited regulant lists; each is fetched directly from its allowlisted URL.</summary>
public sealed class VirginiaDporBulkRosterProvider : IBulkRosterProvider
{
    public string State => VirginiaDporBulkImportService.State;
    public string ArchiveFolder => "virginia-dpor-bulk";
    public string ParserVersion => VirginiaDporBulkImportService.ParserVersion;
    public string Layout => "DPOR tab-delimited regulant lists (20 columns) for the Real Estate Board: active and inactive salespersons, associate brokers, principal brokers and sole proprietors; current firms, business entities, sole proprietorships, branch offices and settlement agents; complete source rows retained";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => VirginiaDporBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(VirginiaDporBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        VirginiaDporBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<VirginiaDporBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}

/// <summary>
/// OREA generates rosters through an ASP.NET form: load the generator, post the free roster selection, read its
/// download id from the download list, then fetch the CSV. Each file uses its own session; cookies are kept per call.
/// </summary>
public sealed class OregonOreaBulkRosterProvider : IBulkRosterProvider, IBulkRosterFileDownloader
{
    private const string Origin = "https://orea.elicense.micropact.com";
    private const long MaxBytes = 200L * 1024 * 1024;
    public string State => OregonOreaBulkImportService.State;
    public string ArchiveFolder => "oregon-orea-bulk";
    public string ParserVersion => OregonOreaBulkImportService.ParserVersion;
    public string Layout => "OREA free roster CSV downloads: active businesses (19 columns), active individuals (32 columns, one row per business association) and inactive individuals (24 columns); spacer columns validated empty; complete source rows retained";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => OregonOreaBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(OregonOreaBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        OregonOreaBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<OregonOreaBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);

    public async Task<BulkRosterDownloadResult> DownloadFileAsync(HttpClient client, string path,
        FloridaBulkFileDefinition file, CancellationToken ct, Action<long, long?> progress)
    {
        var roster = OregonOreaBulkImportService.RosterName(file.Name);
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);

        var generator = await SendAsync(client, HttpMethod.Get, OregonOreaBulkImportService.SourcePage, null, null, cookies, ct);
        var form = await ReadPageAsync(generator, OregonOreaBulkImportService.SourcePage, ct);
        var checkbox = FindRosterCheckbox(form, roster);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__EVENTTARGET", ""), new("__EVENTARGUMENT", ""),
            new("__VIEWSTATE", HiddenValue(form, "__VIEWSTATE") ?? throw new InvalidDataException("OREA roster form omitted its view state.")),
            new("__VIEWSTATEGENERATOR", HiddenValue(form, "__VIEWSTATEGENERATOR") ?? ""),
            new("__VIEWSTATEENCRYPTED", HiddenValue(form, "__VIEWSTATEENCRYPTED") ?? ""),
            new(checkbox, "on"),
            new("ctl00$MainContentPlaceHolder$btnRosterContinue", "Continue"),
            new("ctl00$MainContentPlaceHolder$hfValidPage", "")
        };
        if (HiddenValue(form, "__EVENTVALIDATION") is { } validation) fields.Add(new("__EVENTVALIDATION", validation));

        var posted = await SendAsync(client, HttpMethod.Post, OregonOreaBulkImportService.SourcePage,
            new FormUrlEncodedContent(fields), OregonOreaBulkImportService.SourcePage, cookies, ct);
        string downloads;
        if (posted.StatusCode is System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.Found
            or System.Net.HttpStatusCode.SeeOther or System.Net.HttpStatusCode.MovedPermanently)
        {
            var target = posted.Headers.Location is { } location ? new Uri(new Uri(OregonOreaBulkImportService.SourcePage), location) : null;
            posted.Dispose();
            if (target?.AbsoluteUri != OregonOreaBulkImportService.DownloadPage)
                throw new InvalidDataException("OREA roster selection redirected away from its download list.");
            downloads = await ReadPageAsync(await SendAsync(client, HttpMethod.Get, OregonOreaBulkImportService.DownloadPage,
                null, OregonOreaBulkImportService.SourcePage, cookies, ct), OregonOreaBulkImportService.DownloadPage, ct);
        }
        else
            downloads = await ReadPageAsync(posted, OregonOreaBulkImportService.SourcePage, ct);

        // OREA usually generates a free roster immediately; allow a short wait if it is still being prepared.
        string? rosterId = null;
        for (var check = 0; ; check++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(downloads,
                "RosterIdnt=\"(\\d+)\"\\s*/>\\s*</td>\\s*<td>\\s*" + System.Text.RegularExpressions.Regex.Escape(roster)
                + "\\s*</td>\\s*<td[^>]*>\\s*([^<]*?)\\s*</td>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
                throw new InvalidDataException($"OREA download list did not include the {roster} roster.");
            if (string.Equals(match.Groups[2].Value, "Ready for Download", StringComparison.OrdinalIgnoreCase))
            {
                rosterId = match.Groups[1].Value;
                break;
            }
            if (check >= 11)
                throw new InvalidDataException($"OREA did not finish preparing the {roster} roster ({match.Groups[2].Value}).");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            downloads = await ReadPageAsync(await SendAsync(client, HttpMethod.Get, OregonOreaBulkImportService.DownloadPage,
                null, OregonOreaBulkImportService.SourcePage, cookies, ct), OregonOreaBulkImportService.DownloadPage, ct);
        }

        var url = $"{OregonOreaBulkImportService.FileDownloadUrl}?Idnt={rosterId}&Type=Comma";
        using var response = await SendAsync(client, HttpMethod.Get, url, null, OregonOreaBulkImportService.DownloadPage, cookies, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            throw new HttpRequestException($"OREA roster download returned {(int)response.StatusCode}.", null, response.StatusCode);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
        if (!string.Equals(fileName?.Trim('"'), file.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"OREA returned an unexpected file for the {roster} roster.");
        var expected = response.Content.Headers.ContentLength;
        if (expected > MaxBytes) throw new InvalidDataException("OREA roster exceeds the 200 MiB safety limit.");
        long bytes = 0;
        await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                bytes += read;
                if (bytes > MaxBytes) throw new InvalidDataException("OREA roster exceeds the 200 MiB safety limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                progress(bytes, expected);
            }
        }
        if (expected is { } length && length != bytes)
            throw new InvalidDataException("Incomplete OREA roster download.");
        return new(bytes, response.Content.Headers.LastModified, response.Headers.ETag?.ToString());
    }

    public static string FindRosterCheckbox(string html, string roster)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html,
            "name=\"(ctl00\\$MainContentPlaceHolder\\$ckbRoster\\d+)\"[^>]*/>\\s*</span>\\s*" + System.Text.RegularExpressions.Regex.Escape(roster)
            + "\\s*\\(No Fee Required\\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidDataException($"OREA no longer offers the free {roster} roster.");
    }

    private static string? HiddenValue(string html, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html,
            "<input[^>]*name=\"" + System.Text.RegularExpressions.Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"");
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static async Task<string> ReadPageAsync(HttpResponseMessage response, string url, CancellationToken ct)
    {
        using (response)
        {
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                throw new HttpRequestException($"OREA page {url} returned {(int)response.StatusCode}.", null, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync(ct);
            return html.Length > 2_000_000 ? throw new InvalidDataException("OREA page exceeded the expected size.") : html;
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, HttpContent? content,
        string? referrer, Dictionary<string, string> cookies, CancellationToken ct)
    {
        if (!url.StartsWith(Origin + "/", StringComparison.Ordinal))
            throw new InvalidDataException("OREA request left the allowlisted host.");
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (referrer is not null) request.Headers.Referrer = new Uri(referrer);
        if (cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => cookie.Key + "=" + cookie.Value)));
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                var pair = value.Split(';', 2)[0];
                var separator = pair.IndexOf('=');
                if (separator > 0) cookies[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
            }
        }
        return response;
    }
}
