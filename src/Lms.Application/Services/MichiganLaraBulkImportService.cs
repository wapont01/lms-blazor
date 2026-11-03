using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExcelDataReader;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lms.Application.Services;

public sealed record MichiganLaraRecord(string LicenseNumber, string Name, string LicenseType,
    string Status, DateOnly? IssueDate, DateOnly? ExpirationDate, string? Address, string? Email);
public sealed record MichiganLaraEmployer(string LicenseNumber, string BrokerLicenseNumber, DateOnly? StartDate);

public sealed class MichiganLaraBulkImportService(ApplicationDbContext db)
{
    public const string State = "Michigan";
    public const string ParserVersion = "michigan-lara-reports-v1";
    public const string SourcePage = "https://www.michigan.gov/lara/bureau-list/bpl/license-lists-and-reports";
    public const string ReportBase = "https://aca-prod.accela.com/MILARA/Report/";
    public const string LicenseFile = "real-estate-licenses.xlsx";
    public const string EmployerFile = "real-estate-employers.xlsx";
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(LicenseFile, LicenseFile, 20) { SourceUrl = ReportUrl("32411") },
        new(EmployerFile, EmployerFile, 19) { SourceUrl = ReportUrl("32088") }
    ];
    public static string ReportUrl(string reportId) =>
        ReportBase + "ReportParameter.aspx?module=Licenses&reportID=" + reportId + "&reportType=LINK_REPORT_LIST";
    public static readonly string[] LicenseHeaders =
    [
        "Last Name", "First Name", "Middle Name", "Suffix", "Facility Name", "Profession", "Type",
        "Specialities", "License No", "Status", "Issue Date", "Expiration", "Addr 1", "Addr 2",
        "Addr 3", "Addr City", "State", "Zip", "County", "Email"
    ];
    public static readonly string[] EmployerHeaders =
    [
        "License Number", "License Issue Date", "License Type", "Licensee First Name", "Licensee Middle Name",
        "Licensee Last Name", "Licensee Suffix", "Licensee Business Name", "Expiration Date", "License Status",
        "Licensee City", "Licensee ZIP Code", "Start Date at Broker", "Broker", "Broker License Number",
        "Broker Expiration Date", "Broker Status", "Broker City", "Broker ZIP Code"
    ];
    private static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "Salesperson", "Prin Associate Broker", "Non Prin Associate Broker", "Broker Individual", "Broker Company", "Branch Office"
    };

    public static bool IsFirm(MichiganLaraRecord record) => record.LicenseType is "Broker Company" or "Broker Individual";
    public static bool IsPerson(MichiganLaraRecord record) => record.LicenseType is not ("Broker Company" or "Branch Office");

    public static List<MichiganLaraRecord> ReadRecords(string path, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRows(path, LicenseHeaders, false, ct, progress).Select(row =>
        {
            if (Text(row[5]) != "Real Estate" || !Types.Contains(Text(row[6])))
                throw new InvalidDataException("Michigan report includes an unexpected profession or license type.");
            var type = Text(row[6]);
            var personName = string.Join(' ', new[] { row[1], row[2], row[0], row[3] }.Select(Text).Where(value => value.Length > 0));
            var facilityName = Text(row[4]);
            var name = type is "Broker Company" or "Branch Office"
                ? facilityName.Length > 0 ? facilityName : personName
                : personName.Length > 0 ? personName : facilityName;
            var address = string.Join(", ", new[] { row[12], row[13], row[14], row[15], row[16], row[17] }
                .Select(Text).Where(value => value.Length > 0));
            return new MichiganLaraRecord(Required(Text(row[8]), 40, "license number"), Required(name, 200, "name"),
                type, Required(Text(row[9]), 40, "status"), Date(row[10]), Date(row[11]),
                Optional(address, 500), Optional(Text(row[19]), 254));
        }).ToList();

    public static List<MichiganLaraEmployer> ReadEmployers(string path, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRows(path, EmployerHeaders, true, ct, progress).Select(row =>
        {
            if (Text(row[2]) is not ("Real Estate Salesperson License" or "Real Estate Principal Associate Broker License"
                or "Real Estate Non Principal Associate Broker License" or "Real Estate Branch Office License") || Text(row[9]) != "Active")
                throw new InvalidDataException("Michigan employer report includes an unexpected profession or license status.");
            return new MichiganLaraEmployer(Required(Text(row[0]), 40, "employee license number"),
                Text(row[14]).Length == 0 ? "" : Required(Text(row[14]), 40, "broker license number"), Date(row[12]));
        }).ToList();

    public static long CheckFile(string path, string name, CancellationToken ct = default, Action<long>? progress = null) =>
        name switch
        {
            LicenseFile => ReadRecords(path, ct, progress).Count,
            EmployerFile => ReadRows(path, EmployerHeaders, true, ct, progress).Count,
            _ => throw new InvalidDataException("Unknown Michigan report.")
        };

    private static List<object?[]> ReadRows(string path, string[] headers, bool title, CancellationToken ct, Action<long>? progress)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
        if (reader.ResultsCount != 1 || !reader.Read() || reader.FieldCount != headers.Length)
            throw new InvalidDataException("Michigan workbook layout changed.");
        if (title)
        {
            if (!Text(reader.GetValue(0)).StartsWith("\"Active\" -", StringComparison.Ordinal) || !reader.Read())
                throw new InvalidDataException("Michigan employer report title is missing.");
        }
        if (!Enumerable.Range(0, headers.Length).Select(i => Text(reader.GetValue(i))).SequenceEqual(headers))
            throw new InvalidDataException("Michigan workbook header changed.");
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var values = Enumerable.Range(0, headers.Length).Select(reader.GetValue).ToArray();
            if (values.All(value => Text(value).Length == 0)) continue;
            rows.Add(values);
            if (rows.Count % 10_000 == 0) progress?.Invoke(rows.Count);
        }
        if (rows.Count == 0) throw new InvalidDataException("Michigan report contains no license records.");
        return rows;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Michigan acquisition manifest.");
        if (entries.Count != Definitions.Count || entries.Select(entry => entry.File).Distinct().Count() != entries.Count)
            throw new InvalidDataException("Michigan archive must contain both official reports.");
        foreach (var definition in Definitions)
        {
            var entry = entries.SingleOrDefault(entry => entry.File == definition.RelativePath)
                ?? throw new InvalidDataException("Michigan archive omitted an official report.");
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(definition.Columns.ToString(CultureInfo.InvariantCulture), out var count) || count != entry.Rows)
                throw new InvalidDataException("Invalid Michigan acquisition manifest.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using var file = File.OpenRead(path);
            if (file.Length != entry.Bytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
                entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Michigan report integrity verification failed.");
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, file.Length)) != entry.Rows)
                throw new InvalidDataException("Michigan report row count differs from its manifest.");
        }
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var records = ReadRecords(FloridaBulkImportService.ArchiveFile(root, LicenseFile), ct)
            .GroupBy(record => record.LicenseNumber, StringComparer.Ordinal).Select(group =>
            {
                var distinct = group.Distinct().ToList();
                if (distinct.Count != 1) throw new InvalidDataException("Conflicting Michigan records share a license number.");
                return distinct[0];
            }).ToList();
        var employers = ReadEmployers(FloridaBulkImportService.ArchiveFile(root, EmployerFile), ct);
        var firms = records.Where(IsFirm).ToList();
        var people = records.Where(IsPerson).ToList();
        var locations = records.Where(record => record.LicenseType == "Branch Office").ToList();
        var firmIds = firms.ToDictionary(record => record.LicenseNumber, _ => Guid.NewGuid());
        var personIds = people.ToDictionary(record => record.LicenseNumber, _ => Guid.NewGuid());
        var peopleByNumber = people.ToDictionary(record => record.LicenseNumber);
        var affiliations = employers.Where(row => firmIds.ContainsKey(row.BrokerLicenseNumber)
            && personIds.ContainsKey(row.LicenseNumber)).Distinct().ToList();
        var branchOwners = employers.GroupBy(row => row.LicenseNumber)
            .ToDictionary(group => group.Key, group => group.Select(row => row.BrokerLicenseNumber).Distinct().ToList());
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion
            + string.Join("", entries.OrderBy(entry => entry.File, StringComparer.Ordinal).Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatoryLicensees.CountAsync(row => row.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(row => row.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.CountAsync(row => row.SnapshotId == id, ct) != locations.Count
                || await db.RegulatoryFirmLicenseeAffiliations.CountAsync(row => row.SnapshotId == id, ct) != affiliations.Count)
                throw new InvalidDataException("Existing Michigan snapshot conflicts with the validated archive.");
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
            [[id, State, "Michigan LARA real estate license and employer reports (current licenses)", SourcePage, ParserVersion,
                entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, now, RegulatorySnapshotStatuses.Approved, now, now]], ct);
        long total = 0;
        foreach (var group in firms.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "LicenseStatus", "LicenseIssuedDate", "LicenseRenewalDate", "Address", "Industry"],
                group.Select(row => new object?[] { firmIds[row.LicenseNumber], id, State, row.LicenseNumber, row.Name,
                    row.Status, row.IssueDate, row.ExpirationDate, row.Address, RegulatoryIndustries.RealEstate }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in people.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "SourceLicenseType", "LicenseStatus",
                    "LicenseIssuedDate", "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"],
                group.Select(row => new object?[] { personIds[row.LicenseNumber], id, State, row.LicenseNumber,
                    row.Name.Length > 160 ? row.Name[..160] : row.Name, row.LicenseType, "Real Estate - " + row.LicenseType,
                    row.Status, row.IssueDate, row.ExpirationDate, false, false, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in locations.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLocations",
                ["Id", "SnapshotId", "FirmId", "State", "RegistrationNumber", "Name", "Address", "Kind", "MatchMethod"],
                group.Select(row =>
                {
                    Guid? owner = branchOwners.TryGetValue(row.LicenseNumber, out var owners)
                        && owners.Count == 1 && firmIds.TryGetValue(owners[0], out var ownerId) ? ownerId : null;
                    return new object?[] { Guid.NewGuid(), id, owner, State, row.LicenseNumber, row.Name, row.Address,
                        "Branch office (" + row.Status + ")", owner.HasValue ? "Published broker license" : RegulatoryLocationMatchMethods.NoMatch };
                }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in affiliations.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLicenseeAffiliations",
                ["Id", "SnapshotId", "FirmId", "LicenseeId", "RelationshipEffectiveDate", "IsBrokerInCharge", "ContactEmail"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, firmIds[row.BrokerLicenseNumber],
                    personIds[row.LicenseNumber], row.StartDate, false, peopleByNumber[row.LicenseNumber].Email }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;
    }

    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    private static string Required(string value, int length, string field) =>
        value.Length is 0 || value.Length > length ? throw new InvalidDataException($"Invalid Michigan {field}.") : value;
    private static string? Optional(string value, int length) =>
        value.Length == 0 ? null : Required(value, length, "field");
    private static DateOnly? Date(object? value)
    {
        if (value is null || Text(value).Length == 0) return null;
        if (value is DateTime time) return DateOnly.FromDateTime(time);
        if (value is double number && number is >= 1 and <= 100000)
            return DateOnly.FromDateTime(DateTime.FromOADate(number));
        if (DateOnly.TryParse(Text(value), CultureInfo.InvariantCulture, out var date)) return date;
        throw new InvalidDataException("Invalid Michigan report date.");
    }
}
