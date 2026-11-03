using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Services;

public sealed record MaineAlmsRecord(string LicenseNumber, string Name, string LicenseType, string Status,
    DateOnly? ExpirationDate, string? Address, string? ResponsibleIndividual, string? ResponsibleLicenseNumber,
    string? Email, string[] Fields);

public sealed class MaineAlmsBulkImportService(ApplicationDbContext db)
{
    public const string State = "Maine";
    public const string ParserVersion = "maine-alms-active-csv-v1";
    public const string SourcePage = "https://www.pfr.maine.gov/almsonline/almsquery/welcome.aspx?board=4060";
    public const string IndividualFile = "active-individuals.csv";
    public const string CompanyFile = "active-companies.csv";
    public const string ExportUrl = "https://www.pfr.maine.gov/ALMSOnline/ALMSQuery/ExportToCSV.aspx";
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(IndividualFile, IndividualFile, 40) { SourceUrl = SearchUrl(false) },
        new(CompanyFile, CompanyFile, 40) { SourceUrl = SearchUrl(true) }
    ];
    public static string SearchUrl(bool company) =>
        "https://www.pfr.maine.gov/almsonline/almsquery/Search" + (company ? "Company" : "Individual") + ".aspx";
    public static readonly string[] Headers =
    [
        "Sort Name", "Mailing Name", "License Number", "Profession", "Status", "Address Line 1", "Address Line 2",
        "Address Line 3", "Address Line 4", "City", "State", "Zip", "County FIPS", "County", "Country", "Phone",
        "Fax", "Address Directions", "Mailing Care Of", "Mailing Address Line 1", "Mailing Address Line 2",
        "Mailing Address Line 3", "Mailing Address Line 4", "Mailing City", "Mailing State", "Mailing Zip",
        "Mailing County FIPS", "Mailing County", "Mailing Country", "Mailing Address Directions", "NAIC Code",
        "Expiration Date", "Responsible Individual", "RI License Number", "Authorities", "Department", "Agency",
        "Board", "EmailAddress", "CE Required By Date"
    ];
    private static readonly IReadOnlyDictionary<string, string> PersonTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SALES AGENT"] = "Sales Agent", ["ASSOCIATE BROKER"] = "Associate Broker",
        ["BROKER"] = "Broker", ["DESIGNATED BROKER"] = "Designated Broker"
    };
    private static readonly IReadOnlySet<string> CompanyTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "LIMITED LIABILITY", "INDIVIDUAL PROPRIETORSHIP", "BRANCH OFFICE", "CORPORATION", "PARTNERSHIP", "ASSOCIATION"
    };

    public static List<MaineAlmsRecord> ReadRecords(string path, string name, CancellationToken ct = default, Action<long>? progress = null)
    {
        if (name is not (IndividualFile or CompanyFile)) throw new InvalidDataException("Unknown Maine export.");
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true), true)
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        if (parser.EndOfData || !parser.ReadFields()!.SequenceEqual(Headers))
            throw new InvalidDataException("Maine ALMS CSV header changed.");
        var rows = new List<MaineAlmsRecord>();
        try
        {
            while (!parser.EndOfData)
            {
                ct.ThrowIfCancellationRequested();
                var fields = parser.ReadFields()!;
                if (fields.Length != Headers.Length) throw new InvalidDataException("Maine ALMS CSV column count changed.");
                string Text(int index) => fields[index].Trim();
                var type = Text(3);
                if (Text(37) != "REAL ESTATE COMMISSION" || Text(4) != "ACTIVE"
                    || (name == IndividualFile ? !PersonTypes.ContainsKey(type) : !CompanyTypes.Contains(type)))
                    throw new InvalidDataException("Maine export includes an unexpected board, status or license type.");
                var address = string.Join(", ", Enumerable.Range(5, 7).Select(Text).Where(value => value.Length > 0));
                rows.Add(new(Required(Text(2), 40, "license number"),
                    Required(Text(0), name == IndividualFile ? 160 : 200, "published name"), type, Text(4),
                    Date(Text(31)), Optional(address, 500), Optional(Text(32), 160), Optional(Text(33), 40),
                    Optional(Text(38), 254), fields));
                if (rows.Count % 10_000 == 0) progress?.Invoke(rows.Count);
            }
        }
        catch (MalformedLineException ex) { throw new InvalidDataException("Malformed Maine ALMS CSV.", ex); }
        if (rows.Count == 0) throw new InvalidDataException("Maine export contains no active license rows.");
        return rows;
    }

    public static long CheckFile(string path, string name, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, name, ct, progress).Count;

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Maine acquisition manifest.");
        if (entries.Count != Definitions.Count || entries.Select(entry => entry.File).Distinct().Count() != entries.Count)
            throw new InvalidDataException("Maine archive must contain both official active exports.");
        foreach (var definition in Definitions)
        {
            var entry = entries.SingleOrDefault(entry => entry.File == definition.RelativePath)
                ?? throw new InvalidDataException("Maine archive omitted an official export.");
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns.Count != 1 || !entry.Columns.TryGetValue("40", out var count) || count != entry.Rows
                || entry.SourceRowCount is { } published && published != entry.Rows)
                throw new InvalidDataException("Invalid Maine acquisition manifest.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using var file = File.OpenRead(path);
            if (file.Length != entry.Bytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
                entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Maine export integrity verification failed.");
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, file.Length)) != entry.Rows)
                throw new InvalidDataException("Maine export row count differs from its manifest.");
        }
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var source = Definitions.SelectMany(file => ReadRecords(FloridaBulkImportService.ArchiveFile(root, file.RelativePath),
            file.Name, ct).Select((row, index) => (File: file.Name, Row: index + 1, Record: row))).ToList();
        var records = source.GroupBy(row => row.Record.LicenseNumber, StringComparer.Ordinal).Select(group =>
        {
            var first = group.First();
            foreach (var row in group.Skip(1))
            {
                // Company exports repeat credentials for published aliases; no other source facts may disagree.
                var skipNames = first.File == CompanyFile && row.File == CompanyFile;
                if (row.File != first.File || !first.Record.Fields.Skip(skipNames ? 2 : 0)
                    .SequenceEqual(row.Record.Fields.Skip(skipNames ? 2 : 0)))
                    throw new InvalidDataException("Conflicting Maine records share a license number.");
            }
            return first.Record;
        }).ToList();
        var people = records.Where(row => PersonTypes.ContainsKey(row.LicenseType)).ToList();
        var firms = records.Where(row => CompanyTypes.Contains(row.LicenseType) && row.LicenseType != "BRANCH OFFICE").ToList();
        var locations = records.Where(row => row.LicenseType == "BRANCH OFFICE").ToList();
        var personIds = people.ToDictionary(row => row.LicenseNumber, _ => Guid.NewGuid());
        var firmIds = firms.ToDictionary(row => row.LicenseNumber, _ => Guid.NewGuid());
        var affiliations = firms.Where(row => row.ResponsibleLicenseNumber is { } number && personIds.ContainsKey(number)).ToList();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion
            + string.Join("", entries.OrderBy(entry => entry.File, StringComparer.Ordinal).Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatorySourceRecords.CountAsync(row => row.SnapshotId == id, ct) != source.Count
                || await db.RegulatoryLicensees.CountAsync(row => row.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(row => row.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.CountAsync(row => row.SnapshotId == id, ct) != locations.Count
                || await db.RegulatoryFirmLicenseeAffiliations.CountAsync(row => row.SnapshotId == id, ct) != affiliations.Count)
                throw new InvalidDataException("Existing Maine snapshot conflicts with the validated archive.");
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
            [[id, State, "Maine ALMS unrestricted active real estate individuals and companies", SourcePage, ParserVersion,
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
        foreach (var group in firms.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "LicenseStatus", "LicenseRenewalDate", "Address", "QualifyingBrokerName", "Industry"],
                group.Select(row => new object?[] { firmIds[row.LicenseNumber], id, State, row.LicenseNumber, row.Name,
                    row.Status, row.ExpirationDate, row.Address, row.ResponsibleIndividual, RegulatoryIndustries.RealEstate }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in people.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "SourceLicenseType", "LicenseStatus",
                    "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"],
                group.Select(row => new object?[] { personIds[row.LicenseNumber], id, State, row.LicenseNumber, row.Name,
                    PersonTypes[row.LicenseType], row.LicenseType, row.Status, row.ExpirationDate, false, false, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in locations.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLocations",
                ["Id", "SnapshotId", "FirmId", "State", "RegistrationNumber", "Name", "Address", "Kind", "MatchMethod"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, null, State, row.LicenseNumber, row.Name,
                    row.Address, "Branch office (" + row.Status + ")", RegulatoryLocationMatchMethods.NoMatch }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in affiliations.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLicenseeAffiliations",
                ["Id", "SnapshotId", "FirmId", "LicenseeId", "IsBrokerInCharge", "ContactEmail"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, firmIds[row.LicenseNumber],
                    personIds[row.ResponsibleLicenseNumber!], false, row.Email }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;
    }

    private static string Required(string value, int maximum, string field) =>
        value.Length is > 0 && value.Length <= maximum ? value : throw new InvalidDataException("Invalid Maine " + field + ".");
    private static string? Optional(string value, int maximum) =>
        value.Length == 0 ? null : Required(value, maximum, "field length");
    private static DateOnly? Date(string value) => value.Length == 0 ? null
        : DateOnly.TryParseExact(value, ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new InvalidDataException("Invalid Maine published expiration date.");
}
