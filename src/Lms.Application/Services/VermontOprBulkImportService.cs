using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelDataReader;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lms.Application.Services;

public sealed record VermontOprRecord(string LicenseNumber, string Name, string LicenseType, string Status,
    DateOnly? IssueDate, DateOnly? EffectiveDate, DateOnly? ExpirationDate, string? Address, string? BranchName);

public sealed class VermontOprBulkImportService(ApplicationDbContext db)
{
    public const string State = "Vermont";
    public const string ParserVersion = "vermont-opr-all-status-latest-effective-v1";
    public const string SourcePage = "https://sos.vermont.gov/opr/find-a-professional/";
    public const string GuestUrl = "https://secure.professionals.vermont.gov/prweb/PRServletCustom/V9csDxL3sXkkjMC_FR2HrA%5B%5B*/!STANDARD?UserIdentifier=LicenseLookupGuestUser";
    public static IReadOnlyDictionary<string, string> ProfessionTypes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NGLP083-MAIN"] = "Real Estate Brokerage Firm - Main Office",
        ["NGLP081"] = "Real Estate Broker",
        ["NGLP081-EMGY"] = "Real Estate Broker - Emergency Temporary License",
        ["NGLP081-TEMP"] = "Real Estate Broker - Temporary License",
        ["NGLP083-BRCH"] = "Real Estate Brokerage Firm - Branch Office",
        ["NGLP083-SOLE"] = "Real Estate Brokerage Firm - Sole Proprietorship",
        ["NGLP082"] = "Real Estate Salesperson",
        ["NGLP082-EMGY"] = "Real Estate Salesperson - Emergency Temporary License",
        ["NGLP082-TEMP"] = "Real Estate Salesperson - Temporary"
    };
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } = ProfessionTypes.Keys
        .Select(code => new FloridaBulkFileDefinition(code + ".xlsx", code + ".xlsx", IsEntityCode(code) ? 16 : 17)
        { SourceUrl = GuestUrl }).ToArray();
    public static readonly string[] PersonHeaders =
    [
        "LicenseNumber", "Status", "First Name", "Last Name", "Middle Name", "Address1", "Address2",
        "City", "State", "Country", "Zip", "License First Issuance Date", "License Effective Date",
        "License Expiration", "Specialty", "Supervisor", "Employer"
    ];
    public static readonly string[] EntityHeaders =
    [
        "LicenseNumber", "Status", "BusinessName", "Branch Name", "AddressLine 1", "AddressLine 2",
        "City", "State", "Country", "ZipCode", "License First Issuance Date", "License Effective Date",
        "License Expiration", "Specialty", "Employer", "Supervisor"
    ];
    public static string ProfessionCode(string name) => Definitions.Any(file => file.Name == name)
        ? name[..^5] : throw new InvalidDataException("Unknown Vermont roster category.");
    private static bool IsEntityCode(string code) => code.StartsWith("NGLP083-", StringComparison.Ordinal);
    public static bool IsPerson(VermontOprRecord row) => row.LicenseType.StartsWith("Real Estate Broker", StringComparison.Ordinal)
        && !row.LicenseType.StartsWith("Real Estate Brokerage", StringComparison.Ordinal)
        || row.LicenseType.StartsWith("Real Estate Salesperson", StringComparison.Ordinal);
    public static bool IsBranch(VermontOprRecord row) => row.LicenseType == ProfessionTypes["NGLP083-BRCH"];

    public static List<VermontOprRecord> ReadRecords(string path, string name, CancellationToken ct = default, Action<long>? progress = null)
    {
        var code = ProfessionCode(name);
        var entity = IsEntityCode(code);
        var headers = entity ? EntityHeaders : PersonHeaders;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
        if (reader.ResultsCount != 1 || !reader.Read() || reader.FieldCount != headers.Length
            || !Enumerable.Range(0, headers.Length).Select(i => Text(reader.GetValue(i))).SequenceEqual(headers))
            throw new InvalidDataException("Vermont roster workbook layout or headers changed.");
        var rows = new List<VermontOprRecord>();
        var parts = code[4..].Split('-');
        var licensePattern = "^" + parts[0] + @"\.\d{7}" + (parts.Length == 2 ? "-?" + parts[1] : "") + "$";
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var values = Enumerable.Range(0, headers.Length).Select(i => Text(reader.GetValue(i))).ToArray();
            // OPR emits a repeated header and/or an empty formatted row for zero-result categories.
            if (values.All(value => value.Length == 0) || values.SequenceEqual(headers)) continue;
            if (!Regex.IsMatch(values[0], licensePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new InvalidDataException("Vermont license number does not match its published profession type.");
            var recordName = entity ? values[2] : string.Join(' ', new[] { values[2], values[4], values[3] }.Where(value => value.Length > 0));
            var address = string.Join(", ", (entity ? values[4..10] : values[5..11]).Where(value => value.Length > 0));
            var dateOffset = entity ? 10 : 11;
            // Historical entity rosters contain two genuinely blank business names; do not fabricate replacements.
            if (entity && recordName.Length > 200) throw new InvalidDataException("Invalid Vermont business name.");
            rows.Add(new(values[0], entity ? recordName : Required(recordName, 160, "name"), ProfessionTypes[code],
                Required(values[1], 40, "status"), Date(reader.GetValue(dateOffset)), Date(reader.GetValue(dateOffset + 1)),
                Date(reader.GetValue(dateOffset + 2)), Optional(address, 500), entity ? Optional(values[3], 200) : null));
            if (rows.Count % 10_000 == 0) progress?.Invoke(rows.Count);
        }
        SelectCurrent(rows);
        return rows;
    }

    public static IReadOnlyList<VermontOprRecord> SelectCurrent(IEnumerable<VermontOprRecord> rows) =>
        rows.GroupBy(row => row.LicenseNumber, StringComparer.Ordinal).Select(group =>
        {
            var distinct = group.Distinct().ToArray();
            if (distinct.Length == 1) return distinct[0];
            if (distinct.Any(row => row.EffectiveDate is null)
                || distinct.Select(row => (row.Name, row.LicenseType)).Distinct().Count() != 1)
                throw new InvalidDataException("Conflicting Vermont identities or undated license periods.");
            var newest = distinct.Where(row => row.EffectiveDate == distinct.Max(value => value.EffectiveDate)).ToArray();
            if (newest.Length != 1) throw new InvalidDataException("Conflicting Vermont records share the latest license effective date.");
            return newest[0];
        }).OrderBy(row => row.LicenseNumber, StringComparer.Ordinal).ToArray();

    public static long CheckFile(string path, string name, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, name, ct, progress).Count;

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Vermont acquisition manifest.");
        if (entries.Count != Definitions.Count || entries.Select(entry => entry.File).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidDataException("Vermont archive must contain all nine official profession categories.");
        foreach (var definition in Definitions)
        {
            var entry = entries.SingleOrDefault(entry => entry.File == definition.RelativePath)
                ?? throw new InvalidDataException("Vermont archive omitted an official category.");
            if (entry.SourceUrl != definition.Url || entry.Rows < 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(definition.Columns.ToString(CultureInfo.InvariantCulture), out var count) || count != entry.Rows)
                throw new InvalidDataException("Invalid Vermont acquisition manifest.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using var file = File.OpenRead(path);
            if (file.Length != entry.Bytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
                entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Vermont roster integrity verification failed.");
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, file.Length)) != entry.Rows)
                throw new InvalidDataException("Vermont roster row count differs from its manifest.");
        }
        if (entries.Sum(entry => entry.Rows) == 0) throw new InvalidDataException("Vermont archive contains no license records.");
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var records = SelectCurrent(Definitions.SelectMany(file => ReadRecords(
            FloridaBulkImportService.ArchiveFile(root, file.RelativePath), file.Name, ct)));
        var people = records.Where(IsPerson).ToArray();
        var firms = records.Where(row => !IsPerson(row) && !IsBranch(row)).ToArray();
        var branches = records.Where(IsBranch).ToArray();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + string.Join("",
            entries.OrderBy(entry => entry.File, StringComparer.Ordinal).Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatoryLicensees.CountAsync(row => row.SnapshotId == id, ct) != people.Length
                || await db.RegulatoryFirms.CountAsync(row => row.SnapshotId == id, ct) != firms.Length
                || await db.RegulatoryFirmLocations.CountAsync(row => row.SnapshotId == id, ct) != branches.Length
                || await db.RegulatoryFirmLicenseeAffiliations.AnyAsync(row => row.SnapshotId == id, ct))
                throw new InvalidDataException("Existing Vermont snapshot conflicts with the validated archive.");
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
            [[id, State, "Vermont OPR real estate rosters (all four status filters; latest source effective-dated period per license)",
                SourcePage, ParserVersion, entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, now,
                RegulatorySnapshotStatuses.Approved, now, now]], ct);
        long total = 0;
        foreach (var group in people.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "SourceLicenseType", "LicenseStatus",
                    "LicenseIssuedDate", "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, State, row.LicenseNumber, row.Name, row.LicenseType,
                    row.LicenseType, row.Status, row.IssueDate, row.ExpirationDate, false, false, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in firms.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "LicenseStatus", "LicenseIssuedDate", "LicenseRenewalDate", "Address", "Industry"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, State, row.LicenseNumber, row.Name,
                    row.Status, row.IssueDate, row.ExpirationDate, row.Address, RegulatoryIndustries.RealEstate }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in branches.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLocations",
                ["Id", "SnapshotId", "FirmId", "State", "RegistrationNumber", "Name", "Address", "Kind", "MatchMethod"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, null, State, row.LicenseNumber,
                    row.BranchName ?? row.Name, row.Address, "Branch office (" + row.Status + ")", RegulatoryLocationMatchMethods.NoMatch }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;
    }

    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    private static string Required(string value, int length, string field) =>
        value.Length == 0 || value.Length > length ? throw new InvalidDataException($"Invalid Vermont {field}.") : value;
    private static string? Optional(string value, int length) => value.Length == 0 ? null : Required(value, length, "field");
    private static DateOnly? Date(object? value)
    {
        if (value is null || Text(value).Length == 0) return null;
        if (value is DateTime time) return DateOnly.FromDateTime(time);
        if (DateOnly.TryParseExact(Text(value), ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        throw new InvalidDataException("Invalid Vermont source date.");
    }
}
