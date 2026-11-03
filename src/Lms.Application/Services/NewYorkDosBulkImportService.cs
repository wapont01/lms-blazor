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

public sealed record NewYorkDosRecord(
    long SourceRowNumber,
    string LicenseNumber,
    string LicenseType,
    string SourceLicenseType,
    string Name,
    string? BusinessName,
    DateOnly? ExpirationDate,
    IReadOnlyDictionary<string, string> SourceFields);

public sealed class NewYorkDosBulkImportService(ApplicationDbContext db)
{
    public const string State = "New York";
    public const string ParserVersion = "newyork-dos-yg7h-zjbf-v1";
    public const string SourceUrl = "https://data.ny.gov/resource/yg7h-zjbf.csv";
    public const string RowCountUrl = "https://data.ny.gov/resource/yg7h-zjbf.json?$select=count(*)";
    public const string SourcePage = "https://data.ny.gov/Economic-Development/Active-Real-Estate-Salespersons-and-Brokers/yg7h-zjbf";
    public const string FileName = "active-real-estate-salespersons-and-brokers.csv";
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
        [new(FileName, FileName, 11) { SourceUrl = SourceUrl }];

    public static IReadOnlyList<string> Headers { get; } = Array.AsReadOnly(new[]
    {
        "business_name", "business_address_1", "business_address_2", "business_city", "business_state",
        "business_zip", "county", "license_holder_name", "license_expiration_date", "license_number", "license_type"
    });

    public static List<NewYorkDosRecord> ReadRecords(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("NY DOS roster has no header.");
        if (!headers.Select(value => value.TrimStart('\uFEFF')).SequenceEqual(Headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("NY DOS roster layout changed; expected the published 11-column schema.");

        var records = new List<NewYorkDosRecord>();
        long rowNumber = 1;
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var fields = parser.ReadFields() ?? throw new InvalidDataException($"Empty NY DOS roster row near line {parser.LineNumber}.");
            rowNumber++;
            if (fields.Length != Headers.Count)
                throw new InvalidDataException($"Invalid NY DOS row shape at line {parser.LineNumber}.");

            var sourceFields = new Dictionary<string, string>(Headers.Count, StringComparer.Ordinal);
            for (var index = 0; index < Headers.Count; index++)
                sourceFields.Add(Headers[index], fields[index].Trim());

            var number = Optional(sourceFields["license_number"]);
            if (number is null || number.Length > 40)
                throw new InvalidDataException($"Missing or overlength NY DOS license number at line {parser.LineNumber}.");
            var sourceType = Optional(sourceFields["license_type"]);
            if (sourceType is null || sourceType.Length > 120)
                throw new InvalidDataException($"Missing or overlength NY DOS license type at line {parser.LineNumber}.");
            var business = Optional(sourceFields["business_name"]);
            var holder = Optional(sourceFields["license_holder_name"]);
            var name = holder ?? business ?? $"Name not published ({number})";
            if (name.Length > 160 || business?.Length > 160)
                throw new InvalidDataException($"NY DOS name exceeds the regulatory directory limit at line {parser.LineNumber}.");

            records.Add(new(rowNumber, number, DisplayType(sourceType), sourceType, name,
                holder is null ? null : business, Date(sourceFields["license_expiration_date"]), sourceFields));
            if (records.Count % 10_000 == 0) progress?.Invoke(records.Count);
            if (records.Count > 1_000_000)
                throw new InvalidDataException("NY DOS export exceeded the one-million-row safety limit.");
        }

        if (records.Count == 0)
            throw new InvalidDataException("NY DOS roster contains no records.");
        progress?.Invoke(records.Count);
        return records;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifestStream = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(
            manifestStream, cancellationToken: ct) ?? throw new InvalidDataException("Missing NY DOS acquisition manifest.");
        if (entries.Count != 1)
            throw new InvalidDataException("NY DOS archive must contain exactly one statewide CSV.");

        var entry = entries[0];
        if (entry.File != Definitions[0].RelativePath || entry.SourceUrl != SourceUrl
            || entry.VerifiedAtUtc == default || entry.Rows <= 0 || entry.Bytes <= 0
            || entry.Columns is null || entry.Columns.Count != 1
            || !entry.Columns.TryGetValue("11", out var declaredRows) || declaredRows != entry.Rows)
            throw new InvalidDataException("Invalid NY DOS acquisition metadata.");

        var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
        await using (var stream = File.OpenRead(path))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("NY DOS archive size/hash mismatch.");
        }

        var rows = ReadRecords(path, ct, count => progress?.Invoke(Definitions[0].Name, count, entry.Bytes));
        if (rows.Count != entry.Rows)
            throw new InvalidDataException("NY DOS archive row count does not match its acquisition manifest.");
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
            if (existing.State != State || existing.ParserVersion != ParserVersion
                || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatorySourceRecords.CountAsync(record => record.SnapshotId == snapshotId, ct) != records.Count)
                throw new InvalidDataException("Existing New York DOS snapshot does not match the validated archive.");
            return snapshotId;
        }

        // DOS lists a few licensees once per business address; keep one directory entry per credential.
        var licensees = records
            .GroupBy(record => $"{record.SourceLicenseType}\u001f{record.LicenseNumber}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var connection = db.Database.GetDbConnection();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var sqlTransaction = transaction.GetDbTransaction();
        var importedAt = DateTime.UtcNow;
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [[snapshotId, State, "New York DOS active real estate salespersons and brokers (yg7h-zjbf)",
                SourcePage, ParserVersion, entry.VerifiedAtUtc.UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        long inserted = 0;
        foreach (var group in records.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var sourceRows = group.Select(record => new object?[]
            {
                Guid.NewGuid(), snapshotId, record.SourceRowNumber, null, record.LicenseNumber,
                record.SourceLicenseType, "Active", JsonSerializer.Serialize(record.SourceFields)
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
                Guid.NewGuid(), snapshotId, State, record.LicenseNumber, record.Name,
                record.LicenseType, null, record.SourceLicenseType, "Active",
                null, null, record.ExpirationDate, record.BusinessName, record.BusinessName is null ? null : "Company",
                false, false, null, null, null, null, false
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel",
                    "SourceLicenseType", "LicenseStatus", "LicenseIssuedDate", "OriginalLicenseDate", "LicenseRenewalDate",
                    "RelatedLicenseName", "RelatedLicenseType",
                    "IsRestricted", "EthicsAndAgencyOnly", "IsAdditionalLicenseRecord", "UpdateHours", "ElectiveHours",
                    "PostlicensingHours", "IsBicEligible"],
                licenseeRows, ct);
        }

        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        staged?.Invoke(records.Count);
        return snapshotId;
    }

    // "REAL ESTATE SALESPERSON" -> "Real Estate Salesperson"; fits the 40-character directory column.
    private static string DisplayType(string sourceType)
    {
        var display = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(sourceType.ToLowerInvariant());
        return display.Length <= 40 ? display : display[..40];
    }

    private static string? Optional(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static DateOnly? Date(string value)
    {
        var normalized = Optional(value);
        if (normalized is null) return null;
        return DateTime.TryParseExact(normalized, ["yyyy-MM-dd'T'HH:mm:ss.fff", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd", "MM/dd/yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateOnly.FromDateTime(date)
            : throw new InvalidDataException($"Invalid NY DOS date value: {normalized}.");
    }
}
