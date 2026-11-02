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

public sealed record TexasBulkRecord(string Type, string Number, string Name, string Status,
    DateOnly? OriginalDate, DateOnly? ExpirationDate, string? RelatedType, string? RelatedNumber,
    string? RelatedName, DateOnly? RelatedDate, bool Supervisor)
{
    public string County { get; init; } = "";
}

public sealed class TexasBulkImportService(ApplicationDbContext db)
{
    public const string ParserVersion = "trec-bulk-v1";
    public const string SourceUrl = "https://data.texas.gov/api/views/s7ft-44qi/rows.csv?accessType=DOWNLOAD";
    public const string RowCountUrl = "https://data.texas.gov/resource/s7ft-44qi.json?$select=count(*)";
    public static string CredentialKey(string number) => number.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
        [new("license-holders.csv", "license-holders.csv", 20) { SourceUrl = SourceUrl }];
    private static readonly string[] Headers =
        ["License Type", "License Number", "Full Name", "Suffix", "Status", "Original License Date",
        "License Expiration Date", "Designated Supervisor Flag", "County", "Related License Type",
        "Related License Number", "Related License Full Name", "Related License Suffix",
        "Related License Start Date", "Agency Identifier", "Key Name", "First Name", "Middle Name", "Last Name", "Updated"];

    public static List<TexasBulkRecord> ReadRecords(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true))
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("Texas roster has no header.");
        if (!headers.SequenceEqual(Headers)) throw new InvalidDataException("Texas roster layout changed; expected the published 20-column schema.");
        var records = new List<TexasBulkRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var fields = parser.ReadFields() ?? throw new InvalidDataException("Empty Texas roster row.");
            if (fields.Length != Headers.Length) throw new InvalidDataException($"Invalid Texas row shape at line {parser.LineNumber}.");
            var type = Required(fields[0], 40);
            if (type is not ("Broker Individual" or "Broker Company" or "Sales Agent"))
                throw new InvalidDataException($"Unknown Texas credential type: {type}.");
            var number = Number(fields[1], type);
            if (!seen.Add(type + ":" + CredentialKey(number))) throw new InvalidDataException($"Duplicate Texas credential: {number}.");
            var name = Null(fields[2]) ?? $"Name not published ({number})";
            name = Required(name, type == "Broker Company" ? 200 : 160);
            var status = Required(fields[4], 40);
            if (fields[7] is not ("" or "0" or "1")) throw new InvalidDataException("Unknown Texas supervisor flag.");
            var relatedType = Null(fields[9]);
            var relatedNumber = Null(fields[10]);
            if (relatedNumber is not null)
            {
                if (relatedType is not ("Broker Individual" or "Broker Company" or "Sales Agent"))
                    throw new InvalidDataException("Unknown Texas related credential type.");
                relatedNumber = Number(relatedNumber, relatedType);
            }
            records.Add(new(type, number, name, status, Date(fields[5]), Date(fields[6]), relatedType,
                relatedNumber, Null(fields[11]), Date(fields[13]), fields[7] == "1") { County = fields[8] });
            if (records.Count % 10_000 == 0) progress?.Invoke(records.Count);
        }
        if (!records.Any(r => r.Type == "Broker Individual") || !records.Any(r => r.Type == "Sales Agent")
            || !records.Any(r => r.Type == "Broker Company"))
            throw new InvalidDataException("Texas roster must include individual brokers, sales agents and broker companies.");
        return records;
    }

    private static string Required(string value, int max) =>
        string.IsNullOrWhiteSpace(value) || value.Length > max ? throw new InvalidDataException("Missing or overlength Texas field.") : value;
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Number(string value, string type)
    {
        var suffix = type switch { "Broker Company" => "-BB", "Broker Individual" => "-B", _ => "-SA" };
        if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || value.Length == suffix.Length || value[..^suffix.Length].TrimEnd(' ').Length == 0
            || !value[..^suffix.Length].TrimEnd(' ').All(char.IsAsciiDigit))
            throw new InvalidDataException($"Invalid Texas {type} license number: {value}.");
        return Required(value, 40);
    }
    private static DateOnly? Date(string value) => string.IsNullOrWhiteSpace(value) ? null
        : DateOnly.TryParseExact(value, ["MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : throw new InvalidDataException($"Invalid Texas date: {value}.");

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Texas manifest.");
        if (entries.Count != 1) throw new InvalidDataException("Texas archive must contain exactly one statewide roster.");
        var entry = entries[0];
        if (entry.File != Definitions[0].RelativePath || entry.SourceUrl != SourceUrl || entry.VerifiedAtUtc == default
            || entry.Rows <= 0 || entry.SourceRowCount != entry.Rows || entry.SourceRowCountUrl != RowCountUrl
            || entry.SourceRowCountVerifiedAtUtc is null || entry.SourceRowCountVerifiedAtUtc == default(DateTimeOffset)
            || entry.Columns is null || entry.Columns.Count != 1
            || !entry.Columns.TryGetValue("20", out var count) || count != entry.Rows)
            throw new InvalidDataException("Invalid Texas acquisition metadata.");
        var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
        await using (var file = File.OpenRead(path))
            if (file.Length != entry.Bytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Texas archive size/hash mismatch.");
        var rows = ReadRecords(path, ct, rows => progress?.Invoke(Definitions[0].Name, rows, entry.Bytes));
        if (rows.Count != entry.Rows) throw new InvalidDataException("Texas archive row count mismatch.");
        progress?.Invoke(Definitions[0].Name, rows.Count, entry.Bytes);
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var records = ReadRecords(FloridaBulkImportService.ArchiveFile(root, entries[0].File), ct);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + entries[0].Sha256.ToUpperInvariant()));
        var id = new Guid(digest.AsSpan(0, 16));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != "Texas" || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved)
                throw new InvalidDataException("Texas archive conflicts with an existing snapshot.");
            if (await db.RegulatoryLicensees.CountAsync(r => r.SnapshotId == id, ct) != records.Count(r => r.Type != "Broker Company")
                || await db.RegulatoryFirms.CountAsync(r => r.SnapshotId == id, ct) != records.Count(r => r.Type == "Broker Company"))
                throw new InvalidDataException("Existing Texas snapshot coverage does not match the validated archive.");
            var current = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(s => s.State == "Texas"))
                .Select(s => s.Id).FirstAsync(ct);
            if (current != id) { existing.ActivatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
            return id;
        }
        var connection = db.Database.GetDbConnection();
        var sqlTransaction = transaction.GetDbTransaction();
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [new object?[] { id, "Texas", "TREC official statewide license holders (all published statuses)",
                SourceUrl, ParserVersion, entries[0].VerifiedAtUtc.UtcDateTime, DateTime.UtcNow,
                RegulatorySnapshotStatuses.Approved, DateTime.UtcNow, DateTime.UtcNow }], ct);
        var firms = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var people = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var group in records.Chunk(50))
        {
            var firmRows = new List<object?[]>();
            var personRows = new List<object?[]>();
            foreach (var record in group)
            {
                var recordId = Guid.NewGuid();
                if (record.Type == "Broker Company")
                {
                    firms.Add(CredentialKey(record.Number), recordId);
                    firmRows.Add([recordId, id, "Texas", record.Number, record.Name, record.Status,
                        record.OriginalDate, record.ExpirationDate, record.RelatedType == "Broker Individual" ? record.RelatedName : null]);
                }
                else
                {
                    people.Add(CredentialKey(record.Number), recordId);
                    personRows.Add([recordId, id, "Texas", record.Number, record.Name,
                        record.Type == "Broker Individual" ? "Broker" : "Sales Agent", record.Type, record.Status,
                        record.OriginalDate, record.OriginalDate, record.ExpirationDate, record.RelatedNumber,
                        record.RelatedType, record.RelatedName, false, false, false]);
                }
            }
            if (firmRows.Count > 0) await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "LicenseStatus", "LicenseIssuedDate", "LicenseRenewalDate", "QualifyingBrokerName"], firmRows, ct);
            if (personRows.Count > 0) await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel", "LicenseStatus", "OriginalLicenseDate",
                    "LicenseIssuedDate", "LicenseRenewalDate", "RelatedLicenseNumber", "RelatedLicenseType", "RelatedLicenseName", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"], personRows, ct);
            total += group.Length;
            if (total % 1000 == 0) staged?.Invoke(total);
        }
        var affiliations = new List<object?[]>();
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            Guid firmId, personId;
            if (record.RelatedNumber is null) continue;
            var qualifying = record.Type == "Broker Company" && record.RelatedType == "Broker Individual";
            if (qualifying && firms.TryGetValue(CredentialKey(record.Number), out firmId) && people.TryGetValue(CredentialKey(record.RelatedNumber), out personId)
                || record.Type != "Broker Company" && record.RelatedType == "Broker Company"
                    && people.TryGetValue(CredentialKey(record.Number), out personId) && firms.TryGetValue(CredentialKey(record.RelatedNumber), out firmId))
            {
                affiliations.Add([Guid.NewGuid(), id, firmId, personId, record.RelatedDate, qualifying]);
                if (affiliations.Count == 50) await Flush();
            }
        }
        await Flush();
        staged?.Invoke(total);
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;

        async Task Flush()
        {
            if (affiliations.Count == 0) return;
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLicenseeAffiliations",
                ["Id", "SnapshotId", "FirmId", "LicenseeId", "RelationshipEffectiveDate", "IsBrokerInCharge"], affiliations, ct);
            affiliations.Clear();
        }
    }
}
