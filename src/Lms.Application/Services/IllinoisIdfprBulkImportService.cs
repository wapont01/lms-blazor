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

public sealed record IllinoisIdfprRecord(
    long SourceRowNumber,
    string LicenseNumber,
    string Name,
    string Profession,
    string LicenseType,
    string LicenseStatus,
    string? City,
    string? Address,
    DateOnly? IssueDate,
    DateOnly? ExpirationDate,
    DateOnly? EffectiveDate,
    string Description,
    string? TradeName);

public sealed record IllinoisIdfprLocation(IllinoisIdfprRecord Branch, string? FirmLicenseNumber, string? MatchMethod);

public sealed class IllinoisIdfprBulkImportService(ApplicationDbContext db)
{
    public const string State = "Illinois";
    public const string ParserVersion = "illinois-idfpr-socrata-v1";
    public const string SourcePage = "https://data.illinois.gov/Business-and-Workforce/Professional-Licensing/pzzh-kp68";
    public const string FileName = "real-estate-and-appraisal-licenses.csv";
    public const string SourceUrl = "https://data.illinois.gov/resource/pzzh-kp68.csv";
    public const string RealEstateProfession = "REAL ESTATE";
    public const string AppraiserProfession = "APPRAISAL";
    // Pre-escaped so the requested URL matches the URI the HTTP client reports back.
    public const string Where = "license_type%20in('REAL%20ESTATE','APPRAISAL')%20AND%20description%20not%20in('LICENSED%20APPRAISER%20CE%20COURSE','LICENSED%20APPRAISER%20PRE-LIC%20COURSE','LICENSED%20REAL%20ESTATE%20CE%20COURSE','LICENSED%20REAL%20ESTATE%20PRE-LICENSE%20COURSE')";
    // Socrata names the count column count_1 when a filter is applied, so it is aliased.
    public const string RowCountUrl = "https://data.illinois.gov/resource/pzzh-kp68.json?$select=count(*)%20as%20count&$where=" + Where;
    public const string OrderBy = ":id";

    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
        [new(FileName, FileName, 30) { SourceUrl = SourceUrl }];

    public static IReadOnlyList<string> Headers { get; } = Array.AsReadOnly(new[]
    {
        "license_type", "description", "license_number", "license_status", "business", "title", "first_name", "middle",
        "last_name", "prefix", "suffix", "business_name", "businessdba", "original_issue_date", "effective_date",
        "expiration_date", "city", "state", "zip", "county", "specialty_qualifier", "controlled_substance_schedule",
        "delegated_controlled_substance", "ever_disciplined", "lastmodifieddate", "case_number", "action",
        "discipline_start_date", "discipline_end_date", "discipline_reason"
    });

    private static readonly IReadOnlySet<string> BrokerageTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LICENSED REAL ESTATE BROKER CORPORATION", "LICENSED REAL ESTATE BROKER PARTNERSHIP",
        "LICENSED REAL ESTATE LIMITED LIABILITY FIRM", "LICENSED REAL ESTATE BROKER ASSOCIATION"
    };
    private static readonly IReadOnlySet<string> EducationTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LICENSED REAL ESTATE CE SCHOOL", "LICENSED REAL ESTATE PRE-LICENSE SCHOOL",
        "LICENSED REAL ESTATE EDUCATION PROVIDER", "LICENSED APPRAISER EDUCATION PROVIDER"
    };
    private static readonly IReadOnlyDictionary<string, string> LocationKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["LICENSED REAL ESTATE BRANCH OFFICE"] = "Branch office",
        ["LICENSED REAL ESTATE VIRTUAL OFFICE"] = "Virtual office",
        ["LICENSED REAL ESTATE PRE-LICENSE BRANCH"] = "Pre-license school branch",
        ["REAL ESTATE EDUCATION PROVIDER VIRTUAL OFFICE"] = "Education provider virtual office"
    };
    private static readonly IReadOnlySet<string> CourseTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LICENSED APPRAISER CE COURSE", "LICENSED APPRAISER PRE-LIC COURSE",
        "LICENSED REAL ESTATE CE COURSE", "LICENSED REAL ESTATE PRE-LICENSE COURSE"
    };

    /// <summary>Imported credentials not offered until appraisal and instructor products exist.</summary>
    public static IReadOnlyList<string> AppraiserLicenseTypes { get; } =
    [
        "Associate Real Estate Trainee Appraiser", "Certified General Real Estate Appraiser",
        "Certified Residential Real Estate Appraiser", "State Licensed Real Estate Appraiser",
        "Temporary Practice Real Estate Appraiser", "CE Instructor", "Pre-License Instructor"
    ];

    public static long CheckFile(string path, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRows(path, ct, progress, (_, _) => { });

    public static List<IllinoisIdfprRecord> ReadRecords(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        var records = new List<IllinoisIdfprRecord>();
        ReadRows(path, ct, progress, (row, line) =>
        {
            var number = Optional(row[2]);
            if (number is null) return;
            if (number.Length > 40)
                throw new InvalidDataException($"Overlength Illinois license number at line {line}.");
            var profession = Required(row[0], 60, "profession", line);
            if (profession is not (RealEstateProfession or AppraiserProfession))
                throw new InvalidDataException($"Unexpected Illinois profession '{profession}' at line {line}.");
            var description = Required(row[1], 120, "description", line);
            if (CourseTypes.Contains(description)) return;
            var type = FriendlyType(description);
            var isBusiness = BrokerageTypes.Contains(description) || EducationTypes.Contains(description)
                || description == "APPRAISAL MANAGEMENT COMPANY" || LocationKinds.ContainsKey(description);
            var person = string.Join(' ', new[] { row[6], row[7], row[8], row[10] }
                .Select(part => part.Trim()).Where(part => part.Length > 0));
            var name = (isBusiness ? Optional(row[11]) : person.Length > 0 ? person : Optional(row[11]))
                ?? $"Name not published ({number})";
            var city = Optional(row[16]);
            var region = string.Join(' ', new[] { Optional(row[17]), Optional(row[18]) }.Where(part => part is not null));
            var address = string.Join(", ", new[] { city, region.Length == 0 ? null : region }.Where(part => part is not null));
            records.Add(new(line, number.ToUpperInvariant(), Truncate(name, isBusiness ? 200 : 160)!, profession, type,
                Required(row[3], 40, "license status", line), city?.ToUpperInvariant(), address.Length == 0 ? null : address,
                Date(row[13]), Date(row[15]), Date(row[14]), description, Truncate(Optional(row[12]), 200)));
        });
        return records;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Illinois IDFPR acquisition manifest.");
        var definition = Definitions[0];
        if (entries.Count != 1 || entries[0].File != definition.RelativePath)
            throw new InvalidDataException("Illinois IDFPR archive must contain the real estate professional license export.");
        var entry = entries[0];
        var columns = definition.Columns.ToString(CultureInfo.InvariantCulture);
        if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
            || entry.Columns is null || entry.Columns.Count != 1
            || !entry.Columns.TryGetValue(columns, out var declared) || declared != entry.Rows)
            throw new InvalidDataException("Invalid Illinois IDFPR acquisition metadata.");
        var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
        await using (var stream = File.OpenRead(path))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Illinois IDFPR archive size/hash mismatch.");
        }
        if (CheckFile(path, ct, rows => progress?.Invoke(definition.Name, rows, entry.Bytes)) != entry.Rows)
            throw new InvalidDataException("Illinois IDFPR archive row count mismatch.");
        progress?.Invoke(definition.Name, entry.Rows, entry.Bytes);
        return [entry];
    }

    /// <summary>
    /// Disciplinary actions repeat license numbers; keep the latest expiration,
    /// then the latest of the effective and issue dates, then the first published row.
    /// </summary>
    public static List<IllinoisIdfprRecord> Distinct(IEnumerable<IllinoisIdfprRecord> records) =>
        records.GroupBy(record => record.LicenseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.ExpirationDate ?? DateOnly.MinValue)
                .ThenByDescending(record => LatestIssueDate(record))
                .ThenBy(record => record.SourceRowNumber).First())
            .OrderBy(record => record.SourceRowNumber)
            .ToList();

    private static DateOnly LatestIssueDate(IllinoisIdfprRecord record)
    {
        var issue = record.IssueDate ?? DateOnly.MinValue;
        var effective = record.EffectiveDate ?? DateOnly.MinValue;
        return issue > effective ? issue : effective;
    }

    public static bool IsFirm(IllinoisIdfprRecord record) =>
        BrokerageTypes.Contains(record.Description) || EducationTypes.Contains(record.Description)
        || record.Description == "APPRAISAL MANAGEMENT COMPANY";

    public static string FirmIndustry(IllinoisIdfprRecord firm) =>
        EducationTypes.Contains(firm.Description) ? RegulatoryIndustries.Education
        : firm.Description == "APPRAISAL MANAGEMENT COMPANY" ? RegulatoryIndustries.Appraisal : RegulatoryIndustries.RealEstate;

    /// <summary>Branch business names identify owners; prefer the same city and then an active firm.</summary>
    public static List<IllinoisIdfprLocation> SelectLocations(IReadOnlyList<IllinoisIdfprRecord> records)
    {
        var matcher = new RegulatoryLocationMatcher(records.Where(IsFirm)
            .Select(firm => new RegulatoryLocationFirm(firm.LicenseNumber, firm.Name, firm.City, IsActive(firm.LicenseStatus))));
        return records.Where(record => LocationKinds.ContainsKey(record.Description)).Select(branch =>
        {
            var match = matcher.Match(branch.Name, branch.City);
            return new IllinoisIdfprLocation(branch, match.FirmLicenseNumber, match.MatchMethod);
        }).ToList();
    }

    private static bool IsActive(string status) => status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);

    private static string FriendlyType(string description)
    {
        const string prefix = "LICENSED REAL ESTATE ";
        var value = description.StartsWith(prefix, StringComparison.Ordinal) ? description[prefix.Length..] : description;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant())
            .Replace("Ce ", "CE ", StringComparison.Ordinal).Replace("Pre-license", "Pre-License", StringComparison.Ordinal);
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var records = Distinct(ReadRecords(FloridaBulkImportService.ArchiveFile(root, entries[0].File), ct));
        var firms = records.Where(IsFirm).ToList();
        var locations = SelectLocations(records);
        var people = records.Where(record => !IsFirm(record) && !LocationKinds.ContainsKey(record.Description)).ToList();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + entries[0].Sha256.ToUpperInvariant()));
        var id = new Guid(digest.AsSpan(0, 16));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved)
                throw new InvalidDataException("Illinois IDFPR archive conflicts with an existing snapshot.");
            if (await db.RegulatoryLicensees.CountAsync(record => record.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(record => record.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.CountAsync(record => record.SnapshotId == id, ct) != locations.Count)
                throw new InvalidDataException("Existing Illinois IDFPR snapshot coverage does not match the validated archive.");
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
            [[id, State, "Illinois Department of Financial and Professional Regulation open data: real estate and appraisal licenses (all statuses)",
                SourcePage, ParserVersion, entries[0].VerifiedAtUtc.UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        var firmIds = firms.ToDictionary(firm => firm.LicenseNumber, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var group in firms.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(firm => new object?[]
            {
                firmIds[firm.LicenseNumber], id, State, firm.LicenseNumber, firm.Name, firm.TradeName, firm.LicenseStatus,
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
                $"{LocationKinds[location.Branch.Description]} ({location.Branch.LicenseStatus})", location.MatchMethod
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
                Truncate(person.Profession + " - " + person.Description, 120), person.LicenseStatus,
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
        var actual = parser.ReadFields() ?? throw new InvalidDataException("Illinois IDFPR dataset has no header.");
        if (!actual.Select(value => value.TrimStart('\uFEFF').Trim()).SequenceEqual(Headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Illinois IDFPR dataset layout changed; expected the published {Headers.Count}-column schema.");

        long rows = 0;
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var line = parser.LineNumber;
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 1 && fields[0].Length == 0) continue;
            if (fields.Length != Headers.Count)
                throw new InvalidDataException($"Invalid Illinois IDFPR row shape at line {line}.");
            rows++;
            handle(fields, line);
            if (rows % 10_000 == 0) progress?.Invoke(rows);
            if (rows > 2_000_000)
                throw new InvalidDataException("Illinois IDFPR dataset exceeded the two-million-row safety limit.");
        }
        if (rows == 0)
            throw new InvalidDataException("Illinois IDFPR dataset contains no records.");
        progress?.Invoke(rows);
        return rows;
    }

    private static string Required(string value, int maxLength, string field, long line)
    {
        var normalized = Optional(value);
        if (normalized is null || normalized.Length > maxLength)
            throw new InvalidDataException($"Missing or overlength Illinois {field} at line {line}.");
        return normalized;
    }

    private static string? Optional(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static DateOnly? Date(string value)
    {
        var normalized = Optional(value);
        if (normalized is null) return null;
        if (!DateTime.TryParseExact(normalized, ["MM/dd/yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException($"Invalid Illinois date value: {normalized}.");
        return DateOnly.FromDateTime(date);
    }
}
