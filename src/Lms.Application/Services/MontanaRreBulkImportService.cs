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

public sealed record MontanaRreRecord(string LicenseNumber, string Name, string LicenseType,
    string SourceLicenseType, string Status, DateOnly? ExpirationDate, string[] Fields);

public sealed class MontanaRreBulkImportService(ApplicationDbContext db)
{
    public const string State = "Montana";
    public const string ParserVersion = "montana-rre-csv-v1";
    public const string SourcePage = "https://ebizws.mt.gov/PUBLICPORTAL/searchform?mylist=licenses";
    public const string FileName = "realty-regulation-licenses.csv";
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(FileName, FileName, 10) { SourceUrl = SourcePage }
    ];
    public static readonly string[] Headers =
    [
        "License Number", "License Type", "Status", "Expiration Date", "Business Name",
        "Last Name", "First Name", "City", "State", "Zip"
    ];
    private static readonly IReadOnlyDictionary<string, string[]> TypePrefixes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Broker License"] = ["RRE-BRO-LIC-"],
            ["Salesperson License"] = ["RRE-RBS-LIC-"],
            ["Salesperson Temporary License"] = ["RRE-RBS-PRV-", "RRE-RBS-TMP-"],
            ["Timeshare Salesperson License"] = ["RRE-TSS-LIC-"],
            ["Property Manager License"] = ["RRE-RPM-LIC-"],
            ["Property Manager  License"] = ["RRE-RPM-LIC-"],
            ["Property Manager Temporary License"] = ["RRE-RPM-TMP-"],
            ["Pre-Licensing Instructor License"] = ["RRE-PLI-LIC-"],
            ["Pre-Licensing Course License"] = ["RRE-PLC-LIC-"],
            ["Timeshare Offering Registration (license)"] = ["RRE-TSD-LIC-"]
        };

    public static bool IsPerson(MontanaRreRecord row) =>
        row.SourceLicenseType is not ("Pre-Licensing Course License" or "Timeshare Offering Registration (license)");

    public static List<MontanaRreRecord> ReadRecords(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true), true)
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var records = new List<MontanaRreRecord>();
        var identities = new Dictionary<string, string[]>(StringComparer.Ordinal);
        try
        {
            if (parser.EndOfData || !parser.ReadFields()!.SequenceEqual(Headers))
                throw new InvalidDataException("Montana Realty Regulation CSV header changed.");
            while (!parser.EndOfData)
            {
                ct.ThrowIfCancellationRequested();
                var fields = parser.ReadFields()!;
                if (fields.Length != Headers.Length)
                    throw new InvalidDataException("Montana Realty Regulation CSV column count changed.");
                string Text(int index) => fields[index].Trim();
                var number = Required(Text(0), 40, "license number");
                var sourceType = Text(1);
                if (!TypePrefixes.TryGetValue(sourceType, out var prefixes)
                    || !prefixes.Any(prefix => number.StartsWith(prefix, StringComparison.Ordinal)
                        && number.Length > prefix.Length && number[prefix.Length..].All(char.IsAsciiDigit)))
                    throw new InvalidDataException("Unexpected Montana license type or license number.");
                var name = string.Join(' ', new[] { Text(6), Text(5) }.Where(value => value.Length > 0));
                if (name.Length == 0) name = Text(4);
                if (name.Length > 160) throw new InvalidDataException("Montana published name exceeds the model limit.");
                if (identities.TryGetValue(number, out var previous) && !previous.SequenceEqual(fields))
                    throw new InvalidDataException("Conflicting Montana records share a license number.");
                identities.TryAdd(number, fields);
                records.Add(new(number, name, sourceType == "Property Manager  License" ? "Property Manager License" : sourceType,
                    sourceType, Required(Text(2), 40, "status"), Date(Text(3)), fields));
                if (records.Count % 10_000 == 0) progress?.Invoke(records.Count);
            }
        }
        catch (MalformedLineException ex) { throw new InvalidDataException("Malformed Montana Realty Regulation CSV.", ex); }
        if (records.Count == 0) throw new InvalidDataException("Montana export contains no license records.");
        return records;
    }

    public static long CheckFile(string path, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, ct, progress).Count;

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Montana acquisition manifest.");
        if (entries.Count != 1 || entries[0].File != FileName)
            throw new InvalidDataException("Montana archive must contain the canonical official CSV export.");
        var entry = entries[0];
        if (entry.SourceUrl != SourcePage || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
            || entry.Columns.Count != 1 || !entry.Columns.TryGetValue("10", out var count) || count != entry.Rows
            || entry.SourceRowCount is { } published && published != entry.Rows)
            throw new InvalidDataException("Invalid Montana acquisition manifest.");
        var path = FloridaBulkImportService.ArchiveFile(root, FileName);
        await using var file = File.OpenRead(path);
        if (file.Length != entry.Bytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
            entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Montana export integrity verification failed.");
        if (CheckFile(path, ct, rows => progress?.Invoke(FileName, rows, file.Length)) != entry.Rows)
            throw new InvalidDataException("Montana export row count differs from its manifest.");
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var source = ReadRecords(FloridaBulkImportService.ArchiveFile(root, FileName), ct);
        var people = source.Where(IsPerson).DistinctBy(row => row.LicenseNumber, StringComparer.Ordinal).ToList();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + entries[0].Sha256.ToUpperInvariant()));
        var id = new Guid(digest.AsSpan(0, 16));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatorySourceRecords.CountAsync(row => row.SnapshotId == id, ct) != source.Count
                || await db.RegulatoryLicensees.CountAsync(row => row.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.AnyAsync(row => row.SnapshotId == id, ct)
                || await db.RegulatoryFirmLocations.AnyAsync(row => row.SnapshotId == id, ct)
                || await db.RegulatoryFirmLicenseeAffiliations.AnyAsync(row => row.SnapshotId == id, ct))
                throw new InvalidDataException("Existing Montana snapshot conflicts with the validated archive.");
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
            [[id, State, "Montana Realty Regulation manually acquired public license export", SourcePage, ParserVersion,
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
                    "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, State, row.LicenseNumber, row.Name,
                    row.LicenseType, row.SourceLicenseType, row.Status, row.ExpirationDate, false, false, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;
    }

    private static string Required(string value, int maximum, string field) =>
        value.Length is > 0 && value.Length <= maximum ? value : throw new InvalidDataException("Invalid Montana " + field + ".");
    private static DateOnly? Date(string value) => value.Length == 0 ? null
        : DateOnly.TryParseExact(value, ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new InvalidDataException("Invalid Montana published expiration date.");
}
