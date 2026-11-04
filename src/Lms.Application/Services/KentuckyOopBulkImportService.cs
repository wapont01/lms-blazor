using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Services;

public sealed record KentuckyOopRecord(string LicenseNumber, string Name, string LicenseType, string Status,
    DateOnly? IssueDate, DateOnly? StatusDate, DateOnly? RenewalDate, DateOnly? ExpirationDate, string[] Fields);

public sealed class KentuckyOopBulkImportService(ApplicationDbContext db)
{
    public const string State = "Kentucky";
    public const string ParserVersion = "kentucky-oop-active-html-csv-v1";
    public const string SourcePage = "https://oop.ky.gov/active_lic_dir.aspx";
    public const string BrokerFile = "active-brokers.xls";
    public const string SalesAssociateFile = "active-sales-associates.xls";
    public const string RegisteredFirmBranchFile = "active-registered-firm-branches.xls";
    public const long MaxFileBytes = 100L * 1024 * 1024;
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(BrokerFile, BrokerFile, 13) { SourceUrl = SourcePage },
        new(SalesAssociateFile, SalesAssociateFile, 13) { SourceUrl = SourcePage },
        new(RegisteredFirmBranchFile, RegisteredFirmBranchFile, 13) { SourceUrl = SourcePage }
    ];
    public static readonly string[] Headers =
    [
        "Name", "Business Name", "Board Name", "License Type", "License ID", "Status", "Issue Date",
        "Status Date", "Renewal Date", "Expiration Date", "Business Email", "Business Address", "Mailing Address"
    ];
    private static readonly Regex HtmlTokens = new(@"<[^>]*>|[^<]+", RegexOptions.None, TimeSpan.FromSeconds(10));
    private static readonly Regex HtmlTag = new(@"^<\s*(?<end>/)?(?<name>[a-z]+)\b(?<attrs>[^<>]*)>$",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
    private static readonly Regex Spans = new(@"\b(?:rowspan|colspan)\s*=", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));

    public static string LicenseTypeForFile(string name) => name switch
    {
        BrokerFile => "Broker",
        SalesAssociateFile => "Sales Associate",
        RegisteredFirmBranchFile => "Registered Firm Branch",
        _ => throw new InvalidDataException("Unknown Kentucky export.")
    };

    public static List<KentuckyOopRecord> ReadRecords(string path, string name, CancellationToken ct = default,
        Action<long>? progress = null)
    {
        var type = LicenseTypeForFile(name);
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaxFileBytes) throw new InvalidDataException("Invalid Kentucky export size.");
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var first = reader.Read();
        while (first >= 0 && char.IsWhiteSpace((char)first)) first = reader.Read();
        var source = first == '<' ? ReadHtml(path, ct) : ReadCsv(path, ct);
        var records = new List<KentuckyOopRecord>();
        var identities = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var fields in source)
        {
            ct.ThrowIfCancellationRequested();
            if (fields.Length != Headers.Length) throw new InvalidDataException("Kentucky export column count changed.");
            string Text(int index) => fields[index].Trim();
            // Both the baseline and the live active directory include these two Sales Associate
            // statuses. Retain their exact labels rather than silently dropping rows or calling them Active.
            var supportedStatus = Text(5) == "Active" || type == "Sales Associate"
                && Text(5) is "Probation" or "Active - Requested Cancellation";
            if (Text(2) != "Kentucky Real Estate Commission" || Text(3) != type || !supportedStatus)
                throw new InvalidDataException("Kentucky export contains an unexpected board, type or status.");
            var number = Required(Text(4), 40, "license ID");
            if (!number.All(char.IsAsciiDigit)) throw new InvalidDataException("Kentucky license ID is not numeric.");
            if (identities.TryGetValue(number, out var previous) && !previous.SequenceEqual(fields))
                throw new InvalidDataException("Conflicting Kentucky records share a license ID.");
            identities.TryAdd(number, fields);
            records.Add(new(number, Required(Text(0), name == RegisteredFirmBranchFile ? 200 : 160, "published name"),
                type, Text(5), Date(Text(6)), Date(Text(7)), Date(Text(8)), Date(Text(9)), fields));
            if (records.Count % 10_000 == 0) progress?.Invoke(records.Count);
        }
        if (records.Count == 0) throw new InvalidDataException("Kentucky export contains no active license records.");
        return records;
    }

    private static List<string[]> ReadCsv(string path, CancellationToken ct)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true), true)
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        try
        {
            if (parser.EndOfData || !parser.ReadFields()!.SequenceEqual(Headers))
                throw new InvalidDataException("Kentucky CSV header changed.");
            while (!parser.EndOfData)
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(parser.ReadFields()!);
            }
        }
        catch (MalformedLineException ex) { throw new InvalidDataException("Malformed Kentucky CSV.", ex); }
        return rows;
    }

    private static List<string[]> ReadHtml(string path, CancellationToken ct)
    {
        // The official "Excel" response is a bare HTML table, not an XLSX ZIP or an XML document.
        // Parse only that grammar: no scripts, external resources, DTDs, merged cells or repaired truncation.
        var html = File.ReadAllText(path, new UTF8Encoding(false, true));
        var rows = new List<string[]>();
        List<string>? row = null;
        StringBuilder? cell = null;
        var inTable = false;
        var closed = false;
        var position = 0;
        foreach (Match token in HtmlTokens.Matches(html))
        {
            ct.ThrowIfCancellationRequested();
            if (token.Index != position) throw new InvalidDataException("Malformed Kentucky HTML export.");
            position += token.Length;
            if (token.Value[0] != '<')
            {
                if (cell is not null) cell.Append(token.Value);
                else if (!string.IsNullOrWhiteSpace(token.Value.Trim('\uFEFF')))
                    throw new InvalidDataException("Unexpected text outside Kentucky export cells.");
                continue;
            }
            var tag = HtmlTag.Match(token.Value);
            if (!tag.Success) throw new InvalidDataException("Unsupported Kentucky HTML markup.");
            var closing = tag.Groups["end"].Success;
            var tagName = tag.Groups["name"].Value.ToLowerInvariant();
            var attrs = tag.Groups["attrs"].Value;
            if (Spans.IsMatch(attrs)) throw new InvalidDataException("Kentucky export contains merged cells.");
            switch (tagName, closing)
            {
                case ("table", false) when !inTable && !closed && row is null:
                    inTable = true;
                    break;
                case ("table", true) when inTable && row is null && cell is null:
                    inTable = false;
                    closed = true;
                    break;
                case ("tr", false) when inTable && row is null:
                    row = [];
                    break;
                case ("tr", true) when row is not null && cell is null:
                    if (row.Count != Headers.Length) throw new InvalidDataException("Kentucky HTML column count changed.");
                    rows.Add(row.ToArray());
                    row = null;
                    break;
                case ("td", false) when row is not null && cell is null:
                    cell = new();
                    break;
                case ("td", true) when row is not null && cell is not null:
                    row.Add(WebUtility.HtmlDecode(cell.ToString()).Trim());
                    cell = null;
                    break;
                case ("br", false) when cell is not null:
                    cell.Append('\n');
                    break;
                default:
                    throw new InvalidDataException("Unsupported or malformed Kentucky HTML export.");
            }
        }
        if (position != html.Length || !closed || inTable || row is not null || cell is not null)
            throw new InvalidDataException("Truncated Kentucky HTML export.");
        if (rows.Count == 0 || !rows[0].SequenceEqual(Headers)) throw new InvalidDataException("Kentucky HTML header changed.");
        return rows.Skip(1).ToList();
    }

    public static long CheckFile(string path, string name, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, name, ct, progress).Count;

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Kentucky acquisition manifest.");
        if (entries.Count != Definitions.Count || entries.Select(entry => entry.File).Distinct().Count() != entries.Count)
            throw new InvalidDataException("Kentucky archive must contain all three official active exports.");
        foreach (var definition in Definitions)
        {
            var entry = entries.SingleOrDefault(entry => entry.File == definition.RelativePath)
                ?? throw new InvalidDataException("Kentucky archive omitted an official export.");
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes is <= 0 or > MaxFileBytes
                || entry.VerifiedAtUtc == default || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue("13", out var count) || count != entry.Rows
                || entry.SourceRowCount is { } published && published != entry.Rows)
                throw new InvalidDataException("Invalid Kentucky acquisition manifest.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using var file = File.OpenRead(path);
            if (file.Length != entry.Bytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
                entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Kentucky export integrity verification failed.");
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, file.Length)) != entry.Rows)
                throw new InvalidDataException("Kentucky export row count differs from its manifest.");
        }
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var source = Definitions.SelectMany(file => ReadRecords(FloridaBulkImportService.ArchiveFile(root, file.RelativePath),
            file.Name, ct).Select((record, index) => (File: file.Name, Row: index + 1, Record: record))).ToList();
        var records = source.GroupBy(row => row.Record.LicenseNumber, StringComparer.Ordinal).Select(group =>
        {
            var first = group.First();
            if (group.Any(row => row.File != first.File || !row.Record.Fields.SequenceEqual(first.Record.Fields)))
                throw new InvalidDataException("Conflicting Kentucky records share a license ID.");
            return first.Record;
        }).ToList();
        var people = records.Where(row => row.LicenseType != "Registered Firm Branch").ToList();
        var firms = records.Where(row => row.LicenseType == "Registered Firm Branch").ToList();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + string.Join("",
            entries.OrderBy(entry => entry.File, StringComparer.Ordinal).Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatorySourceRecords.CountAsync(row => row.SnapshotId == id, ct) != source.Count
                || await db.RegulatoryLicensees.CountAsync(row => row.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(row => row.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.AnyAsync(row => row.SnapshotId == id, ct)
                || await db.RegulatoryFirmLicenseeAffiliations.AnyAsync(row => row.SnapshotId == id, ct))
                throw new InvalidDataException("Existing Kentucky snapshot conflicts with the validated archive.");
            var current = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == State))
                .Select(row => row.Id).FirstAsync(ct);
            if (current != id) { existing.ActivatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
            return id;
        }
        var connection = db.Database.GetDbConnection();
        var sqlTransaction = transaction.GetDbTransaction();
        var now = DateTime.UtcNow;
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [[id, State, "Kentucky OOP All Regions active real estate directory exports", SourcePage, ParserVersion,
                entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, now, RegulatorySnapshotStatuses.Approved, now, now]], ct);
        long total = 0;
        foreach (var group in source.Select((row, index) => (row.File, row.Row, row.Record, Number: index + 1)).Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatorySourceRecords",
                ["Id", "SnapshotId", "SourceRowNumber", "LicenseNumber", "LicenseType", "LicenseStatus", "SourceRecordJson"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, row.Number, row.Record.LicenseNumber,
                    row.Record.LicenseType, row.Record.Status, JsonSerializer.Serialize(new
                    {
                        source_file = row.File, source_row = row.Row,
                        fields = Headers.Zip(row.Record.Fields).ToDictionary(pair => pair.First, pair => pair.Second)
                    }) }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in people.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "SourceLicenseType", "LicenseStatus",
                    "LicenseIssuedDate", "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, State, row.LicenseNumber, row.Name,
                    row.LicenseType, row.LicenseType, row.Status, row.IssueDate, row.RenewalDate, false, false, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        // Registered Firm Branch is a licensed entity credential. The source does not distinguish
        // parent firms from branches or publish credential-based affiliation; names are not relationship evidence.
        // Export address labels disagree with the directory UI, so neither address is projected as a corrected Address.
        foreach (var group in firms.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "LicenseStatus", "LicenseIssuedDate", "LicenseRenewalDate", "Industry"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, State, row.LicenseNumber, row.Name,
                    row.Status, row.IssueDate, row.RenewalDate, RegulatoryIndustries.RealEstate }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;
    }

    private static string Required(string value, int maximum, string field) =>
        value.Length is > 0 && value.Length <= maximum ? value : throw new InvalidDataException("Invalid Kentucky " + field + ".");
    private static DateOnly? Date(string value) => value.Length == 0 ? null
        : DateOnly.TryParseExact(value, ["M/d/yyyy", "MM/dd/yyyy", "M/dd/yyyy", "MM/d/yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new InvalidDataException("Invalid Kentucky published date.");
}
