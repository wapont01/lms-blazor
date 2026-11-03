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

public sealed record DelawareDprRecord(
    long SourceRowNumber,
    string LicenseNumber,
    string Name,
    string Profession,
    string LicenseType,
    string LicenseStatus,
    string? City,
    string? Address,
    DateOnly? IssueDate,
    DateOnly? ExpirationDate);

public sealed record DelawareDprLocation(DelawareDprRecord Branch, string? FirmLicenseNumber, string? MatchMethod);

public sealed class DelawareDprBulkImportService(ApplicationDbContext db)
{
    public const string State = "Delaware";
    public const string ParserVersion = "delaware-dpr-socrata-v1";
    public const string SourcePage = "https://data.delaware.gov/Licenses-and-Certifications/Professional-and-Occupational-Licensing/pjnv-eaih";
    public const string FileName = "real-estate-professional-licenses.csv";
    public const string SourceUrl = "https://data.delaware.gov/resource/pjnv-eaih.csv";
    public const string RealEstateProfession = "Real Estate";
    public const string AppraiserProfession = "Real Estate Appraisers";
    // Pre-escaped so the requested URL matches the URI the HTTP client reports back.
    public const string Where = "profession_id%20in('Real%20Estate','Real%20Estate%20Appraisers')";
    // Socrata names the count column count_1 when a filter is applied, so it is aliased.
    public const string RowCountUrl = "https://data.delaware.gov/resource/pjnv-eaih.json?$select=count(*)%20as%20count&$where=" + Where;
    // Socrata's row identifier gives a stable total order for the paged export.
    public const string OrderBy = ":id";
    public const string MainOfficePermit = "Main Office Permit";
    public const string BranchOfficePermit = "Branch Office Permit";
    public const string BranchOfficeKind = "Branch office permit";

    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
        [new(FileName, FileName, 15) { SourceUrl = SourceUrl }];

    public static IReadOnlyList<string> Headers { get; } = Array.AsReadOnly(new[]
    {
        "last_name", "first_name", "combined_name", "license_no", "profession_id", "license_type", "city", "state",
        "zip_code", "country", "issue_date", "expiration_date", "disciplinary_action", "license_status", "count"
    });

    /// <summary>Appraisal management companies are firms of the appraisal industry.</summary>
    public static IReadOnlySet<string> AppraisalCompanyTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Appraisal Management Company", "Federally Regulated AMC-Registry Only"
    };

    /// <summary>Individual appraiser credentials; imported but not offered until appraisal products exist.</summary>
    public static IReadOnlyList<string> AppraiserLicenseTypes { get; } =
    [
        "Appraiser Trainee", "Certified Assessor", "Certified General Real Property Appraise", "Cert. Residential Real Property Appr",
        "Licensed Real Property Appraiser", "Temporary Practice Permit"
    ];

    public static long CheckFile(string path, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRows(path, ct, progress, (_, _) => { });

    public static List<DelawareDprRecord> ReadRecords(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        var records = new List<DelawareDprRecord>();
        ReadRows(path, ct, progress, (row, line) =>
        {
            var number = Optional(row[3]);
            if (number is null || number.Length > 40)
                throw new InvalidDataException($"Missing or overlength Delaware license number at line {line}.");
            var profession = Required(row[4], 60, "profession", line);
            if (profession is not (RealEstateProfession or AppraiserProfession))
                throw new InvalidDataException($"Unexpected Delaware profession '{profession}' at line {line}.");
            var type = Required(row[5], 40, "license type", line);
            var isFirm = type is MainOfficePermit or BranchOfficePermit || AppraisalCompanyTypes.Contains(type);
            var person = string.Join(' ', new[] { row[1], row[0] }.Select(part => part.Trim()).Where(part => part.Length > 0));
            var name = (isFirm ? Optional(row[2]) ?? Optional(row[0]) : person.Length > 0 ? person : Optional(row[2]))
                ?? $"Name not published ({number})";
            var city = Optional(row[6]);
            var region = string.Join(' ', new[] { Optional(row[7]), Optional(row[8]) }.Where(part => part is not null));
            var country = Optional(row[9]);
            var address = string.Join(", ", new[] { city, region.Length == 0 ? null : region,
                country is null || country.Equals("United States", StringComparison.OrdinalIgnoreCase) ? null : country }
                .Where(part => part is not null));
            records.Add(new(line, number.ToUpperInvariant(), Truncate(name, isFirm ? 200 : 160)!, profession, type,
                Required(row[13], 40, "license status", line), city?.ToUpperInvariant(), address.Length == 0 ? null : address,
                Date(row[10]), Date(row[11])));
        });
        return records;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Delaware DPR acquisition manifest.");
        var definition = Definitions[0];
        if (entries.Count != 1 || entries[0].File != definition.RelativePath)
            throw new InvalidDataException("Delaware DPR archive must contain the real estate professional license export.");
        var entry = entries[0];
        var columns = definition.Columns.ToString(CultureInfo.InvariantCulture);
        if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
            || entry.Columns is null || entry.Columns.Count != 1
            || !entry.Columns.TryGetValue(columns, out var declared) || declared != entry.Rows)
            throw new InvalidDataException("Invalid Delaware DPR acquisition metadata.");
        var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
        await using (var stream = File.OpenRead(path))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Delaware DPR archive size/hash mismatch.");
        }
        if (CheckFile(path, ct, rows => progress?.Invoke(definition.Name, rows, entry.Bytes)) != entry.Rows)
            throw new InvalidDataException("Delaware DPR archive row count mismatch.");
        progress?.Invoke(definition.Name, entry.Rows, entry.Bytes);
        return [entry];
    }

    /// <summary>
    /// A few license numbers are published twice (an expired earlier term and a later one); keep the latest expiration,
    /// then the latest issue date, then the first published row.
    /// </summary>
    public static List<DelawareDprRecord> Distinct(IEnumerable<DelawareDprRecord> records) =>
        records.GroupBy(record => record.LicenseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.ExpirationDate ?? DateOnly.MinValue)
                .ThenByDescending(record => record.IssueDate ?? DateOnly.MinValue)
                .ThenBy(record => record.SourceRowNumber).First())
            .OrderBy(record => record.SourceRowNumber)
            .ToList();

    public static bool IsFirm(DelawareDprRecord record) =>
        record.LicenseType == MainOfficePermit || AppraisalCompanyTypes.Contains(record.LicenseType);

    public static string FirmIndustry(DelawareDprRecord firm) =>
        AppraisalCompanyTypes.Contains(firm.LicenseType) ? RegulatoryIndustries.Appraisal : RegulatoryIndustries.RealEstate;

    /// <summary>
    /// DPR does not publish which brokerage holds a branch office permit; branches are linked by name to a main office
    /// permit, preferring one in the same city and then an active one.
    /// </summary>
    public static List<DelawareDprLocation> SelectLocations(IReadOnlyList<DelawareDprRecord> records)
    {
        var matcher = new RegulatoryLocationMatcher(records.Where(record => record.LicenseType == MainOfficePermit)
            .Select(firm => new RegulatoryLocationFirm(firm.LicenseNumber, firm.Name, firm.City, IsActive(firm.LicenseStatus))));
        return records.Where(record => record.LicenseType == BranchOfficePermit).Select(branch =>
        {
            var match = matcher.Match(branch.Name, branch.City);
            return new DelawareDprLocation(branch, match.FirmLicenseNumber, match.MatchMethod);
        }).ToList();
    }

    private static bool IsActive(string status) => status.Equals("Active", StringComparison.OrdinalIgnoreCase);

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var records = Distinct(ReadRecords(FloridaBulkImportService.ArchiveFile(root, entries[0].File), ct));
        var firms = records.Where(IsFirm).ToList();
        var locations = SelectLocations(records);
        var people = records.Where(record => !IsFirm(record) && record.LicenseType != BranchOfficePermit).ToList();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + entries[0].Sha256.ToUpperInvariant()));
        var id = new Guid(digest.AsSpan(0, 16));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved)
                throw new InvalidDataException("Delaware DPR archive conflicts with an existing snapshot.");
            if (await db.RegulatoryLicensees.CountAsync(record => record.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(record => record.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.CountAsync(record => record.SnapshotId == id, ct) != locations.Count)
                throw new InvalidDataException("Existing Delaware DPR snapshot coverage does not match the validated archive.");
            var current = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(snapshot => snapshot.State == State))
                .Select(snapshot => snapshot.Id).FirstAsync(ct);
            if (current != id) { existing.ActivatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
            return id;
        }

        var connection = db.Database.GetDbConnection();
        var sqlTransaction = transaction.GetDbTransaction();
        var importedAt = DateTime.UtcNow;
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [[id, State, "Delaware Division of Professional Regulation open data: real estate and real estate appraiser licenses (all statuses)",
                SourcePage, ParserVersion, entries[0].VerifiedAtUtc.UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        var firmIds = firms.ToDictionary(firm => firm.LicenseNumber, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var group in firms.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(firm => new object?[]
            {
                firmIds[firm.LicenseNumber], id, State, firm.LicenseNumber, firm.Name, null, firm.LicenseStatus,
                firm.IssueDate, firm.ExpirationDate, firm.Address, null, FirmIndustry(firm)
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "TradeName", "LicenseStatus",
                    "LicenseIssuedDate", "LicenseRenewalDate", "Address", "QualifyingBrokerName", "Industry"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        foreach (var group in locations.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(location => new object?[]
            {
                Guid.NewGuid(), id, location.FirmLicenseNumber is { } owner ? firmIds[owner] : null,
                State, location.Branch.LicenseNumber, location.Branch.Name, location.Branch.Address,
                $"{BranchOfficeKind} ({location.Branch.LicenseStatus})", location.MatchMethod
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLocations",
                ["Id", "SnapshotId", "FirmId", "State", "RegistrationNumber", "Name", "Address", "Kind", "MatchMethod"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        foreach (var group in people.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(person => new object?[]
            {
                Guid.NewGuid(), id, State, person.LicenseNumber, person.Name, person.LicenseType, null,
                Truncate(person.Profession + " - " + person.LicenseType, 120), person.LicenseStatus,
                null, person.IssueDate, person.ExpirationDate, null, null, null, false, false, false
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel", "SourceLicenseType",
                    "LicenseStatus", "OriginalLicenseDate", "LicenseIssuedDate", "LicenseRenewalDate",
                    "RelatedLicenseNumber", "RelatedLicenseType", "RelatedLicenseName",
                    "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        staged?.Invoke(total);
        return id;
    }

    private static long ReadRows(string path, CancellationToken ct, Action<long>? progress, Action<string[], long> handle)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var actual = parser.ReadFields() ?? throw new InvalidDataException("Delaware DPR dataset has no header.");
        if (!actual.Select(value => value.TrimStart('\uFEFF').Trim()).SequenceEqual(Headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Delaware DPR dataset layout changed; expected the published {Headers.Count}-column schema.");

        long rows = 0;
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var line = parser.LineNumber;
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 1 && fields[0].Length == 0) continue;
            if (fields.Length != Headers.Count)
                throw new InvalidDataException($"Invalid Delaware DPR row shape at line {line}.");
            rows++;
            handle(fields, line);
            if (rows % 10_000 == 0) progress?.Invoke(rows);
            if (rows > 1_000_000)
                throw new InvalidDataException("Delaware DPR dataset exceeded the one-million-row safety limit.");
        }
        if (rows == 0)
            throw new InvalidDataException("Delaware DPR dataset contains no records.");
        progress?.Invoke(rows);
        return rows;
    }

    private static string Required(string value, int maxLength, string field, long line)
    {
        var normalized = Optional(value);
        if (normalized is null || normalized.Length > maxLength)
            throw new InvalidDataException($"Missing or overlength Delaware {field} at line {line}.");
        return normalized;
    }

    private static string? Optional(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    // DPR uses 1900-01-01 as a placeholder for unknown dates.
    private static DateOnly? Date(string value)
    {
        var normalized = Optional(value);
        if (normalized is null) return null;
        if (!DateTime.TryParseExact(normalized, ["yyyy-MM-dd'T'HH:mm:ss.fff", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException($"Invalid Delaware date value: {normalized}.");
        return date.Year <= 1900 ? null : DateOnly.FromDateTime(date);
    }
}
