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

public sealed record ColoradoDoraRecord(
    long SourceRowNumber,
    string? LicensePrefix,
    string? LicenseNumber,
    string LicenseType,
    string? LicenseStatus,
    string Name,
    DateOnly? IssueDate,
    DateOnly? RenewalDate,
    IReadOnlyDictionary<string, string> SourceFields);

public sealed class ColoradoDoraBulkImportService(ApplicationDbContext db)
{
    public const string ParserVersion = "colorado-dora-4zse-6bnw-v1";
    public const string SourceUrl = "https://data.colorado.gov/resource/4zse-6bnw.csv";
    public const string RowCountUrl = "https://data.colorado.gov/resource/4zse-6bnw.json?$select=count(*)";
    public const string SourcePage = "https://data.colorado.gov/Regulations/Licensed-Real-Estate-Professionals-in-Colorado/4zse-6bnw/about_data";
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
        [new("licensed-real-estate-professionals.csv", "licensed-real-estate-professionals.csv", 20) { SourceUrl = SourceUrl }];

    public static IReadOnlyList<string> Headers { get; } = Array.AsReadOnly(new[]
    {
        "lastName", "firstName", "middleName", "suffix", "entityName", "city", "state", "zipCode",
        "licensePrefix", "licenseNumber", "licenseType", "licenseFirstIssueDate", "licenseLastRenewedDate",
        "licenseExpirationDate", "licenseStatus", "caseNumber", "programAction", "disciplineEffectiveDate",
        "disciplineCompleteDate", "verifyLicense"
    });

    public static List<ColoradoDoraRecord> ReadRecords(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("DORA roster has no header.");
        if (!headers.Select(value => value.TrimStart('\uFEFF')).SequenceEqual(Headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("DORA roster layout changed; expected the published 20-column schema.");

        var records = new List<ColoradoDoraRecord>();
        long rowNumber = 1;
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var fields = parser.ReadFields() ?? throw new InvalidDataException($"Empty DORA roster row near line {parser.LineNumber}.");
            rowNumber++;
            if (fields.Length != Headers.Count)
                throw new InvalidDataException($"Invalid DORA row shape at line {parser.LineNumber}.");

            var sourceFields = new Dictionary<string, string>(Headers.Count, StringComparer.Ordinal);
            for (var index = 0; index < Headers.Count; index++)
                sourceFields.Add(Headers[index], fields[index].Trim());

            var prefix = Optional(sourceFields["licensePrefix"]);
            var number = Optional(sourceFields["licenseNumber"]);
            var type = Required(sourceFields["licenseType"], "license type", 120);
            var status = Required(sourceFields["licenseStatus"], "license status", 40);
            if (prefix?.Length > 20 || number?.Length > 40)
                throw new InvalidDataException($"Overlength DORA credential identity at line {parser.LineNumber}.");

            var name = Optional(sourceFields["entityName"])
                ?? JoinName(sourceFields["firstName"], sourceFields["middleName"], sourceFields["lastName"], sourceFields["suffix"])
                ?? (number is null ? string.Empty : $"Name not published ({number})");
            if (name.Length > 160)
                throw new InvalidDataException($"DORA name exceeds the regulatory directory limit at line {parser.LineNumber}.");

            records.Add(new(rowNumber, prefix, number, type, status, name,
                Date(sourceFields["licenseFirstIssueDate"]),
                Date(sourceFields["licenseExpirationDate"]),
                sourceFields));
            if (records.Count % 10_000 == 0) progress?.Invoke(records.Count);
            if (records.Count > 1_000_000)
                throw new InvalidDataException("DORA export exceeded the one-million-row safety limit.");
        }

        if (records.Count == 0)
            throw new InvalidDataException("DORA roster contains no records.");
        progress?.Invoke(records.Count);
        return records;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifestStream = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(
            manifestStream, cancellationToken: ct) ?? throw new InvalidDataException("Missing DORA acquisition manifest.");
        if (entries.Count != 1)
            throw new InvalidDataException("DORA archive must contain exactly one statewide CSV.");

        var entry = entries[0];
        if (entry.File != Definitions[0].RelativePath || entry.SourceUrl != SourceUrl
            || entry.VerifiedAtUtc == default || entry.Rows <= 0 || entry.Bytes <= 0
            || entry.Columns is null || entry.Columns.Count != 1
            || !entry.Columns.TryGetValue("20", out var declaredRows) || declaredRows != entry.Rows)
            throw new InvalidDataException("Invalid DORA acquisition metadata.");

        var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
        await using (var stream = File.OpenRead(path))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DORA archive size/hash mismatch.");
        }

        var rows = ReadRecords(path, ct, count => progress?.Invoke(Definitions[0].Name, count, entry.Bytes));
        if (rows.Count != entry.Rows)
            throw new InvalidDataException("DORA archive row count does not match its acquisition manifest.");
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var entry = entries.Single();
        var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
        var records = ReadRecords(path, ct, count => progress?.Invoke(Definitions[0].Name, count, entry.Bytes));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + entry.Sha256.ToUpperInvariant()));
        var snapshotId = new Guid(digest.AsSpan(0, 16));
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == snapshotId, ct);
        if (existing is not null)
        {
            if (existing.State != "Colorado" || existing.ParserVersion != ParserVersion
                || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatorySourceRecords.CountAsync(record => record.SnapshotId == snapshotId, ct) != records.Count)
                throw new InvalidDataException("Existing Colorado DORA snapshot does not match the validated archive.");
            return snapshotId;
        }

        var licensees = records.Where(record => record.LicenseNumber is not null)
            .GroupBy(record => $"{record.LicensePrefix}\u001f{record.LicenseNumber}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var connection = db.Database.GetDbConnection();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var sqlTransaction = transaction.GetDbTransaction();
        var importedAt = DateTime.UtcNow;
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [[snapshotId, "Colorado", "Colorado DORA professional licenses (4zse-6bnw)",
                SourcePage, ParserVersion, entry.VerifiedAtUtc.UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        long inserted = 0;
        foreach (var group in records.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var sourceRows = group.Select(record => new object?[]
            {
                Guid.NewGuid(), snapshotId, record.SourceRowNumber, record.LicensePrefix, record.LicenseNumber,
                record.LicenseType, record.LicenseStatus, JsonSerializer.Serialize(record.SourceFields)
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatorySourceRecords",
                ["Id", "SnapshotId", "SourceRowNumber", "LicensePrefix", "LicenseNumber", "LicenseType", "LicenseStatus", "SourceRecordJson"],
                sourceRows, ct);
            inserted += group.Length;
            staged?.Invoke(inserted);
        }

        foreach (var group in licensees.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var licenseeRows = group.Select(record => new object?[]
            {
                Guid.NewGuid(), snapshotId, "Colorado", record.LicenseNumber, record.Name,
                record.LicensePrefix, null, record.LicenseType, record.LicenseStatus,
                record.IssueDate, record.IssueDate, record.RenewalDate,
                false, false, null, null, null, null, false
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel",
                    "SourceLicenseType", "LicenseStatus", "LicenseIssuedDate", "OriginalLicenseDate", "LicenseRenewalDate",
                    "IsRestricted", "EthicsAndAgencyOnly", "IsAdditionalLicenseRecord", "UpdateHours", "ElectiveHours",
                    "PostlicensingHours", "IsBicEligible"],
                licenseeRows, ct);
        }

        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        staged?.Invoke(records.Count);
        return snapshotId;
    }

    private static string Required(string value, string name, int maximumLength)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            throw new InvalidDataException($"Missing or overlength DORA {name}.");
        return normalized;
    }

    private static string? Optional(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0 || normalized.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? null : normalized;
    }

    private static string? JoinName(params string[] values)
    {
        var parts = values.Select(Optional).Where(value => value is not null).ToArray();
        return parts.Length == 0 ? null : string.Join(' ', parts);
    }

    private static DateOnly? Date(string value)
    {
        var normalized = Optional(value);
        if (normalized is null) return null;
        return DateOnly.TryParseExact(normalized, ["MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidDataException($"Invalid DORA date value: {normalized}.");
    }
}
