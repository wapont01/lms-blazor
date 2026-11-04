using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Services;

public sealed record IdahoDoplRecord(string LicenseNumber, string Name, string SourceLicenseType,
    string? LicenseType, string Status, DateOnly? OriginalIssueDate, DateOnly? ExpirationDate,
    string? TradeName, string? Address, string[] Fields);

public sealed class IdahoDoplBulkImportService(ApplicationDbContext db)
{
    public const string State = "Idaho";
    public const string ParserVersion = "idaho-dopl-active-ods-csv-v1";
    public const string SourcePage = "https://edopl.idaho.gov/OnlineServices/?link=PubSearch";
    // FAST currently publishes ODS; the historical CSV conversion has the same 23 columns.
    public const string FileName = "real-estate-active-all-types.ods";
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(FileName, FileName, 23) { SourceUrl = SourcePage }
    ];
    public static readonly string[] Headers =
    [
        "Type", "License Number", "Last Name", "Middle Name", "First Name", "Suffix", "Business Name",
        "DBA", "Status", "Original Issue Date", "Expiration", "Discipline", "Street", "Street 2",
        "Unit Type", "Unit", "City", "County", "State", "Country", "ZIP Code", "Email", "Phone Number"
    ];
    public const string CompanyType = "Real Estate Company License";
    public const string BranchType = "Real Estate Branch Office License";
    private static readonly IReadOnlyDictionary<string, string> PersonTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Real Estate Broker License"] = "Broker",
            ["Real Estate Salesperson License"] = "Salesperson",
            ["Real Estate Cooperative Broker License"] = "Cooperative Broker"
        };
    private static readonly IReadOnlySet<string> EvidenceTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Real Estate Instructor Certification License", "Real Estate Provider Certification License", "Timeshare Registration"
    };
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    public static bool IsPerson(IdahoDoplRecord row) => row.LicenseType is not null;
    public static bool IsEvidence(IdahoDoplRecord row) => EvidenceTypes.Contains(row.SourceLicenseType);

    public static List<IdahoDoplRecord> ReadRecords(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        using var input = File.OpenRead(path);
        if (input.Length is <= 0 or > 100L * 1024 * 1024)
            throw new InvalidDataException("Idaho export is empty or exceeds the 100 MiB limit.");
        var zipped = input.ReadByte() == 'P' && input.ReadByte() == 'K';
        input.Position = 0;
        IEnumerable<string[]> source = zipped ? ReadOds(input, ct) : ReadCsv(path, ct);
        var rows = new List<IdahoDoplRecord>();
        var identities = new Dictionary<string, string[]>(StringComparer.Ordinal);
        using var iterator = source.GetEnumerator();
        try
        {
            if (!iterator.MoveNext() || !iterator.Current.SequenceEqual(Headers))
                throw new InvalidDataException("Idaho DOPL export header changed.");
            while (iterator.MoveNext())
            {
                ct.ThrowIfCancellationRequested();
                var fields = iterator.Current;
                if (fields.Length != Headers.Length)
                    throw new InvalidDataException("Idaho DOPL export column count changed.");
                string Value(int index) => fields[index].Trim();
                var type = Value(0);
                var person = PersonTypes.TryGetValue(type, out var licenseType);
                if ((!person && type is not (CompanyType or BranchType) && !EvidenceTypes.Contains(type))
                    || Value(8) != "Active" || Value(11) is not ("Yes" or "No"))
                    throw new InvalidDataException("Idaho export contains an unexpected board/type, status or discipline value.");
                var number = Required(Value(1), 40, "license number");
                if (!number.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
                    throw new InvalidDataException("Invalid Idaho published license number.");
                var name = person
                    ? string.Join(' ', new[] { Value(4), Value(3), Value(2), Value(5) }.Where(value => value.Length > 0))
                    : Value(6);
                if (name.Length == 0)
                    name = string.Join(' ', new[] { Value(4), Value(3), Value(2), Value(5) }.Where(value => value.Length > 0));
                name = Required(name, person ? 160 : 200, "published name");
                var address = string.Join(", ", new[] { Value(12), Value(13),
                    string.Join(' ', new[] { Value(14), Value(15) }.Where(value => value.Length > 0)),
                    Value(16), Value(18), Value(20), Value(19) }.Where(value => value.Length > 0));
                var original = Date(Value(9));
                var expiry = Date(Value(10));
                if (identities.TryGetValue(number, out var previous))
                {
                    // Company/branch alias rows may vary only their published business name and DBA.
                    var aliases = type is CompanyType or BranchType && previous[0].Trim() == type;
                    if (!Enumerable.Range(0, Headers.Length).Where(index => !aliases || index is not (6 or 7))
                        .All(index => previous[index] == fields[index]))
                        throw new InvalidDataException("Conflicting Idaho records share a license number.");
                }
                else identities.Add(number, fields);
                rows.Add(new(number, name, type, licenseType, Value(8), original, expiry,
                    Optional(Value(7), 200), Optional(address, 500), fields));
                if (rows.Count > 500_000) throw new InvalidDataException("Idaho export exceeds the row limit.");
                if (rows.Count % 10_000 == 0) progress?.Invoke(rows.Count);
            }
        }
        catch (Exception ex) when (ex is MalformedLineException or XmlException or DecoderFallbackException)
        {
            throw new InvalidDataException("Malformed Idaho DOPL export.", ex);
        }
        if (rows.Count == 0) throw new InvalidDataException("Idaho export contains no active records.");
        return rows;
    }

    public static long CheckFile(string path, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, ct, progress).Count;

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Idaho acquisition manifest.");
        if (entries.Count != 1 || entries[0].File != FileName)
            throw new InvalidDataException("Idaho archive must contain the canonical official export.");
        var entry = entries[0];
        if (entry.SourceUrl != SourcePage || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
            || entry.Columns.Count != 1 || !entry.Columns.TryGetValue("23", out var count) || count != entry.Rows
            || entry.SourceRowCount is { } published && published != entry.Rows)
            throw new InvalidDataException("Invalid Idaho acquisition manifest.");
        var path = FloridaBulkImportService.ArchiveFile(root, FileName);
        await using var file = File.OpenRead(path);
        if (file.Length != entry.Bytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
            entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Idaho export integrity verification failed.");
        if (CheckFile(path, ct, rows => progress?.Invoke(FileName, rows, file.Length)) != entry.Rows)
            throw new InvalidDataException("Idaho export row count differs from its manifest.");
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var source = ReadRecords(FloridaBulkImportService.ArchiveFile(root, FileName), ct);
        var records = source.DistinctBy(row => row.LicenseNumber, StringComparer.Ordinal).ToList();
        var people = records.Where(IsPerson).ToList();
        var firms = records.Where(row => row.SourceLicenseType == CompanyType).ToList();
        var locations = records.Where(row => row.SourceLicenseType == BranchType).ToList();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + entries[0].Sha256.ToUpperInvariant()));
        var id = new Guid(digest.AsSpan(0, 16));
        await RegulatoryLicenseeNameIndex.EnsureAsync(db, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatorySourceRecords.CountAsync(row => row.SnapshotId == id, ct) != source.Count
                || await db.RegulatoryLicensees.CountAsync(row => row.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(row => row.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.CountAsync(row => row.SnapshotId == id, ct) != locations.Count
                || await db.RegulatoryFirmLicenseeAffiliations.AnyAsync(row => row.SnapshotId == id, ct))
                throw new InvalidDataException("Existing Idaho snapshot conflicts with the validated archive.");
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
            [[id, State, "Idaho DOPL unrestricted active REC all-types public export", SourcePage, ParserVersion,
                entries[0].VerifiedAtUtc.UtcDateTime, now, RegulatorySnapshotStatuses.Approved, now, now]], ct);
        long total = 0;
        foreach (var group in source.Select((record, index) => (Record: record, Row: index + 1)).Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatorySourceRecords",
                ["Id", "SnapshotId", "SourceRowNumber", "LicenseNumber", "LicenseType", "LicenseStatus", "SourceRecordJson"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, row.Row, row.Record.LicenseNumber,
                    row.Record.SourceLicenseType, row.Record.Status, JsonSerializer.Serialize(new
                    {
                        source_file = FileName, source_row = row.Row,
                        fields = Headers.Zip(row.Record.Fields).ToDictionary(pair => pair.First, pair => pair.Second)
                    }) }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in people.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "SourceLicenseType", "LicenseStatus",
                    "OriginalLicenseDate", "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, State, row.LicenseNumber, row.Name,
                    row.LicenseType, row.SourceLicenseType, row.Status, row.OriginalIssueDate, row.ExpirationDate,
                    false, false, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in firms.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "TradeName", "LicenseStatus",
                    "LicenseIssuedDate", "LicenseRenewalDate", "Address", "Industry"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, State, row.LicenseNumber, row.Name, row.TradeName,
                    row.Status, row.OriginalIssueDate, row.ExpirationDate, row.Address, RegulatoryIndustries.RealEstate }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in locations.Chunk(100))
        {
            // The export contains no parent-company credential. Names/DBAs are not proof of ownership.
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLocations",
                ["Id", "SnapshotId", "FirmId", "State", "RegistrationNumber", "Name", "Address", "Kind", "MatchMethod"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, null, State, row.LicenseNumber, row.Name,
                    row.Address, "Branch office (Active)", RegulatoryLocationMatchMethods.NoMatch }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;
    }

    private static IEnumerable<string[]> ReadCsv(string path, CancellationToken ct)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true), true)
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        while (!parser.EndOfData) { ct.ThrowIfCancellationRequested(); yield return parser.ReadFields()!; }
    }

    private static IEnumerable<string[]> ReadOds(Stream input, CancellationToken ct)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        if (archive.Entries.Count > 30 || archive.Entries.Count(entry => entry.FullName == "content.xml") != 1)
            throw new InvalidDataException("Idaho ODS package structure changed.");
        var entry = archive.GetEntry("content.xml")!;
        if (entry.Length is <= 0 or > 128L * 1024 * 1024)
            throw new InvalidDataException("Idaho ODS content exceeds its size limit.");
        using var content = entry.Open();
        using var xml = XmlReader.Create(content, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128L * 1024 * 1024
        });
        var tables = 0;
        var inTable = false;
        while (!xml.EOF)
        {
            ct.ThrowIfCancellationRequested();
            if (xml.NodeType == XmlNodeType.Element && xml.NamespaceURI == Table.NamespaceName)
            {
                if (xml.LocalName == "table")
                {
                    if (++tables != 1) throw new InvalidDataException("Idaho ODS contains multiple sheets.");
                    inTable = true;
                }
                else if (xml.LocalName == "table-row")
                {
                    if (!inTable) throw new InvalidDataException("Unexpected Idaho ODS row.");
                    var row = (XElement)XNode.ReadFrom(xml);
                    if (row.Attribute(Table + "number-rows-repeated") is { } repeated && repeated.Value != "1")
                        throw new InvalidDataException("Idaho ODS repeated-row layout changed.");
                    var cells = new List<string>();
                    foreach (var cell in row.Elements())
                    {
                        var valueType = cell.Attribute(Office + "value-type")?.Value;
                        if (cell.Name != Table + "table-cell" || cell.Attribute(Table + "formula") is not null
                            || cell.Attribute(Table + "number-columns-repeated") is { } columns && columns.Value != "1"
                            || cell.Attribute(Table + "number-columns-spanned") is { } width && width.Value != "1"
                            || cell.Attribute(Table + "number-rows-spanned") is { } height && height.Value != "1"
                            || valueType is not (null or "string") && !(valueType == "date" && cells.Count is 9 or 10))
                            throw new InvalidDataException("Idaho ODS cell layout changed.");
                        var value = string.Join("\n", cell.Elements(Text + "p").Select(Paragraph));
                        if (valueType == "date" && (!DateOnly.TryParseExact(cell.Attribute(Office + "date-value")?.Value,
                            ["yyyy-MM-dd", "yyyy-MM-dd'T00:00:00Z'"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var storedDate)
                            || Date(value) != storedDate))
                            throw new InvalidDataException("Idaho ODS date disagrees with its published text.");
                        cells.Add(value);
                        if (cells.Count > Headers.Length) throw new InvalidDataException("Idaho ODS column count changed.");
                    }
                    yield return cells.ToArray();
                    continue;
                }
            }
            if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "table" && xml.NamespaceURI == Table.NamespaceName)
                inTable = false;
            xml.Read();
        }
        if (tables != 1) throw new InvalidDataException("Idaho ODS omitted its license sheet.");
    }

    private static string Paragraph(XElement paragraph)
    {
        var result = new StringBuilder();
        foreach (var node in paragraph.Nodes())
        {
            if (node is XText text) result.Append(text.Value);
            else if (node is XElement element)
            {
                if (element.Name == Text + "s")
                {
                    var value = element.Attribute(Text + "c")?.Value ?? "1";
                    if (!int.TryParse(value, out var spaces) || spaces is < 1 or > 1000)
                        throw new InvalidDataException("Invalid Idaho ODS whitespace.");
                    result.Append(' ', spaces);
                }
                else if (element.Name == Text + "tab") result.Append('\t');
                else if (element.Name == Text + "line-break") result.Append('\n');
                else result.Append(Paragraph(element));
            }
        }
        return result.ToString();
    }

    private static string Required(string value, int maximum, string field) =>
        value.Length is > 0 && value.Length <= maximum ? value : throw new InvalidDataException("Invalid Idaho " + field + ".");
    private static string? Optional(string value, int maximum) => value.Length == 0 ? null : Required(value, maximum, "field length");
    private static DateOnly? Date(string value) => value.Length == 0 ? null
        : DateOnly.TryParseExact(value, ["d-MMM-yyyy", "dd-MMM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new InvalidDataException("Invalid Idaho published date.");
}
