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

public sealed record WestVirginiaRecRecord(
    long SourceRowNumber,
    string FileName,
    string LicenseNumber,
    string Name,
    string LicenseType,
    string LicenseStatus,
    string CompanyNumber,
    string CompanyName,
    string BranchNumber,
    string? Address);

public sealed record WestVirginiaRecLicensee(WestVirginiaRecRecord Record, string SourceLicenseType);

public sealed record WestVirginiaRecFirm(
    string CompanyNumber,
    string CompanyName,
    string? Address);

public sealed record WestVirginiaRecLocation(
    string CompanyNumber,
    string BranchNumber,
    string CompanyName,
    string? Address);

public sealed record WestVirginiaRecAffiliation(string LicenseNumber, string CompanyNumber);

public sealed class WestVirginiaRecBulkImportService(ApplicationDbContext db)
{
    public const string State = "West Virginia";
    public const string ParserVersion = "west-virginia-rec-active-rosters-v2";
    public const string SourcePage = "https://rec.wv.gov/active-licensee-rosters";
    public const string SalespersonFile = "SalespersonRoster.xlsx";
    public const string AssociateBrokerFile = "AssociateBrokerRoster.xlsx";
    public const string BrokerFile = "BrokerRoster.xlsx";
    public const string BranchOfficeKind = "Branch office";

    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(SalespersonFile, SalespersonFile, 12) { SourceUrl = "https://rec.wv.gov/media/305/download" },
        new(AssociateBrokerFile, AssociateBrokerFile, 12) { SourceUrl = "https://rec.wv.gov/media/306/download" },
        new(BrokerFile, BrokerFile, 12) { SourceUrl = "https://rec.wv.gov/media/307/download" }
    ];

    public static IReadOnlyList<string> Headers { get; } = Array.AsReadOnly(new[]
    {
        "Name", "LicenseNumber", "LicenseType", "CompanyNumber", "CompanyName", "BranchNumber", "Status", "Email",
        "CompanyStreet", "CompanyCity", "CompanyState", "CompanyZip"
    });

    private static IReadOnlyDictionary<string, string> LicenseTypes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SalespersonFile] = "Salesperson",
            [AssociateBrokerFile] = "Associate Broker",
            [BrokerFile] = "Broker"
        };

    public static long CheckFile(string path, string fileName, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, fileName, ct, progress).Count;

    public static List<WestVirginiaRecRecord> ReadRecords(string path, string fileName, CancellationToken ct = default,
        Action<long>? progress = null)
    {
        var records = new List<WestVirginiaRecRecord>();
        ReadRows(path, fileName, ct, progress, (fields, row) =>
        {
            var licenseNumber = Required(fields[1], 40, "license number", row).ToUpperInvariant();
            var licenseType = Required(fields[2], 40, "license type", row);
            if (!licenseType.Equals(LicenseTypes[fileName], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected West Virginia license type '{licenseType}' in {fileName} at row {row}.");
            var status = Required(fields[6], 40, "license status", row);
            if (!status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected West Virginia license status '{status}' in {fileName} at row {row}.");

            var companyNumber = Required(fields[3], 40, "company number", row);
            var branchNumber = Required(fields[5], 40, "branch number", row);
            if (!companyNumber.All(char.IsAsciiDigit) || !branchNumber.All(char.IsAsciiDigit))
                throw new InvalidDataException($"Invalid West Virginia company or branch number in {fileName} at row {row}.");

            var name = Required(CollapseWhitespace(fields[0]), 160, "licensee name", row);
            var companyName = Required(CollapseWhitespace(fields[4]), 200, "company name", row);
            var address = Address(fields[8], fields[9], fields[10], fields[11]);
            records.Add(new(row, fileName, licenseNumber, name, LicenseTypes[fileName], "Active", companyNumber,
                companyName, branchNumber, address));
        });
        return records;
    }

    public static List<WestVirginiaRecLicensee> SelectLicensees(IEnumerable<WestVirginiaRecRecord> records) =>
        records.GroupBy(record => record.LicenseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rows = group.ToList();
                var types = rows.Select(record => record.LicenseType).Distinct(StringComparer.Ordinal)
                    .OrderBy(TypeRank).ThenBy(type => type, StringComparer.Ordinal).ToArray();
                var record = rows.OrderByDescending(item => item.Name.Length)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .ThenBy(item => TypeRank(item.LicenseType))
                    .ThenBy(item => item.FileName, StringComparer.Ordinal)
                    .ThenBy(item => item.SourceRowNumber)
                    .First();
                var preferredType = types[0];
                record = record with { LicenseType = preferredType };
                return new WestVirginiaRecLicensee(record, string.Join(", ", types));
            })
            .OrderBy(item => item.Record.LicenseNumber, StringComparer.Ordinal)
            .ToList();

    public static List<WestVirginiaRecFirm> SelectFirms(IEnumerable<WestVirginiaRecRecord> records) =>
        records.GroupBy(record => record.CompanyNumber, StringComparer.Ordinal)
            .Select(group =>
            {
                var companyNames = group.Select(record => record.CompanyName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(name => name.Length)
                    .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(name => name, StringComparer.Ordinal).ToArray();
                if (companyNames.Length != 1)
                    throw new InvalidDataException($"West Virginia company {group.Key} has conflicting published names.");
                var mainOffice = group.Where(record => record.BranchNumber == "00")
                    .OrderByDescending(record => record.Address?.Length ?? 0)
                    .ThenBy(record => record.Address, StringComparer.Ordinal)
                    .FirstOrDefault();
                return new WestVirginiaRecFirm(group.Key, companyNames[0], mainOffice?.Address);
            })
            .OrderBy(firm => firm.CompanyNumber, StringComparer.Ordinal)
            .ToList();

    public static List<WestVirginiaRecLocation> SelectLocations(IEnumerable<WestVirginiaRecRecord> records) =>
        records.Where(record => record.BranchNumber != "00")
            .GroupBy(record => (record.CompanyNumber, record.BranchNumber))
            .Select(group =>
            {
                var names = group.Select(record => record.CompanyName).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(name => name.Length)
                    .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(name => name, StringComparer.Ordinal).ToArray();
                if (names.Length != 1)
                    throw new InvalidDataException($"West Virginia branch {group.Key.BranchNumber} for company {group.Key.CompanyNumber} has conflicting published names.");
                var branch = group.OrderByDescending(record => record.Address?.Length ?? 0)
                    .ThenBy(record => record.Address, StringComparer.Ordinal).First();
                return new WestVirginiaRecLocation(group.Key.CompanyNumber, group.Key.BranchNumber, names[0], branch.Address);
            })
            .OrderBy(location => location.CompanyNumber, StringComparer.Ordinal)
            .ThenBy(location => location.BranchNumber, StringComparer.Ordinal)
            .ToList();

    public static List<WestVirginiaRecAffiliation> SelectAffiliations(IEnumerable<WestVirginiaRecRecord> records) =>
        records.Select(record => new WestVirginiaRecAffiliation(record.LicenseNumber, record.CompanyNumber))
            .Distinct()
            .OrderBy(item => item.LicenseNumber, StringComparer.Ordinal)
            .ThenBy(item => item.CompanyNumber, StringComparer.Ordinal)
            .ToList();

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing West Virginia REC acquisition manifest.");
        if (entries.Count != Definitions.Count)
            throw new InvalidDataException("West Virginia REC archive must contain all three active licensee rosters.");
        foreach (var entry in entries) FloridaBulkImportService.ArchiveFile(root, entry.File);

        var ordered = new List<FloridaBulkManifestEntry>();
        foreach (var definition in Definitions)
        {
            ct.ThrowIfCancellationRequested();
            var matches = entries.Where(entry => entry.File == definition.RelativePath).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"West Virginia REC archive requires one {definition.Name}.");
            var entry = matches[0];
            var columnCount = definition.Columns.ToString(CultureInfo.InvariantCulture);
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(columnCount, out var declaredRows) || declaredRows != entry.Rows)
                throw new InvalidDataException($"Invalid West Virginia REC acquisition metadata for {definition.Name}.");

            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using (var stream = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"West Virginia REC archive size/hash mismatch for {definition.Name}.");
            }

            var rows = CheckFile(path, definition.Name, ct, count => progress?.Invoke(definition.Name, count, entry.Bytes));
            if (rows != entry.Rows)
                throw new InvalidDataException($"West Virginia REC archive row count mismatch for {definition.Name}.");
            progress?.Invoke(definition.Name, rows, entry.Bytes);
            ordered.Add(entry);
        }
        return ordered;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var records = entries.SelectMany(entry =>
                ReadRecords(FloridaBulkImportService.ArchiveFile(root, entry.File), Path.GetFileName(entry.File), ct))
            .ToList();
        var people = SelectLicensees(records);
        var firms = SelectFirms(records);
        var locations = SelectLocations(records);
        var affiliations = SelectAffiliations(records);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion
            + string.Concat(entries.Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved)
                throw new InvalidDataException("West Virginia REC archive conflicts with an existing snapshot.");
            if (await db.RegulatoryLicensees.CountAsync(item => item.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(item => item.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.CountAsync(item => item.SnapshotId == id, ct) != locations.Count
                || await db.RegulatoryFirmLicenseeAffiliations.CountAsync(item => item.SnapshotId == id, ct) != affiliations.Count)
                throw new InvalidDataException("Existing West Virginia REC snapshot coverage does not match the validated archive.");
            var current = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(snapshot => snapshot.State == State))
                .Select(snapshot => snapshot.Id).FirstAsync(ct);
            if (current != id)
            {
                existing.ActivatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return id;
        }

        var connection = db.Database.GetDbConnection();
        var sqlTransaction = transaction.GetDbTransaction();
        var importedAt = DateTime.UtcNow;
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [[id, State,
                "West Virginia REC active licensee rosters (salespersons, associate brokers and brokers; active-only source coverage)",
                SourcePage, ParserVersion, entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        var personIds = people.ToDictionary(item => item.Record.LicenseNumber, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        var firmIds = firms.ToDictionary(item => item.CompanyNumber, _ => Guid.NewGuid(), StringComparer.Ordinal);
        long total = 0;
        foreach (var group in firms.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            // CompanyNumber is a published company ID, not evidence of a separate firm license or status.
            var rows = group.Select(firm => new object?[]
            {
                firmIds[firm.CompanyNumber], id, State, firm.CompanyNumber, firm.CompanyName, null, null,
                null, null, firm.Address, null, RegulatoryIndustries.RealEstate
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
                Guid.NewGuid(), id, firmIds[location.CompanyNumber], State, location.CompanyNumber + ":" + location.BranchNumber,
                location.CompanyName, location.Address, BranchOfficeKind, null
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLocations",
                ["Id", "SnapshotId", "FirmId", "State", "RegistrationNumber", "Name", "Address", "Kind", "MatchMethod"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        foreach (var group in people.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(item => new object?[]
            {
                personIds[item.Record.LicenseNumber], id, State, item.Record.LicenseNumber, item.Record.Name,
                item.Record.LicenseType, null, item.SourceLicenseType, item.Record.LicenseStatus,
                null, null, null, null, null, null, false, false, false
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel", "SourceLicenseType",
                    "LicenseStatus", "OriginalLicenseDate", "LicenseIssuedDate", "LicenseRenewalDate",
                    "RelatedLicenseNumber", "RelatedLicenseType", "RelatedLicenseName",
                    "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        foreach (var group in affiliations.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(item => new object?[]
            {
                Guid.NewGuid(), id, firmIds[item.CompanyNumber], personIds[item.LicenseNumber], null, false
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLicenseeAffiliations",
                ["Id", "SnapshotId", "FirmId", "LicenseeId", "RelationshipEffectiveDate", "IsBrokerInCharge"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        staged?.Invoke(total);
        return id;
    }

    private static long ReadRows(string path, string fileName, CancellationToken ct, Action<long>? progress,
        Action<string[], long> handle)
    {
        if (!LicenseTypes.ContainsKey(fileName))
            throw new InvalidDataException("Unknown West Virginia REC source file.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        if (reader.ResultsCount != 1 || !reader.Read() || reader.FieldCount != Headers.Count)
            throw new InvalidDataException($"West Virginia REC {fileName} workbook layout changed.");
        for (var index = 0; index < Headers.Count; index++)
        {
            var actual = Cell(reader, index);
            if (!string.Equals(actual, Headers[index], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"West Virginia REC {fileName} header changed at column {index + 1}; expected '{Headers[index]}', found '{actual}'.");
        }

        long rows = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var fields = Enumerable.Range(0, reader.FieldCount).Select(index => Cell(reader, index)).ToArray();
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            rows++;
            handle(fields, rows + 1);
            if (rows % 10_000 == 0) progress?.Invoke(rows);
            if (rows > 1_000_000)
                throw new InvalidDataException($"West Virginia REC {fileName} exceeded the one-million-row safety limit.");
        }
        if (rows == 0)
            throw new InvalidDataException($"West Virginia REC {fileName} contains no active licensees.");
        progress?.Invoke(rows);
        return rows;
    }

    private static int TypeRank(string type) => type switch
    {
        "Broker" => 0,
        "Associate Broker" => 1,
        "Salesperson" => 2,
        _ => 3
    };

    private static string Cell(IExcelDataReader reader, int index) => reader.GetValue(index) switch
    {
        null => string.Empty,
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
        var value => value.ToString()?.Trim() ?? string.Empty
    };

    private static string Required(string value, int maxLength, string field, long row)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maxLength)
            throw new InvalidDataException($"Missing or overlength West Virginia {field} at row {row}.");
        return normalized;
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string? Address(string street, string city, string state, string zip)
    {
        var normalizedStreet = CollapseWhitespace(street);
        var normalizedCity = CollapseWhitespace(city);
        var region = string.Join(' ', new[] { CollapseWhitespace(state), CollapseWhitespace(zip) }
            .Where(value => value.Length > 0));
        var address = string.Join(", ", new[] { normalizedStreet, normalizedCity, region }
            .Where(value => value.Length > 0));
        return address.Length == 0 ? null : address;
    }
}
