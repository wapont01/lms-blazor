using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lms.Application.Services;

public sealed record VirginiaDporRecord(
    long SourceRowNumber,
    string FileName,
    string LicenseNumber,
    string Name,
    string LicenseType,
    string LicenseStatus,
    string? City,
    string? Address,
    DateOnly? ExpirationDate,
    DateOnly? CertificationDate);

public sealed record VirginiaDporLocation(VirginiaDporRecord Branch, string? FirmLicenseNumber, string? MatchMethod);

/// <summary>One DPOR regulant list: which license type it holds, its status and whether it lists people or businesses.</summary>
public sealed record VirginiaDporList(string FileName, string Occupation, string LicenseType, string LicenseStatus,
    VirginiaDporListKind Kind, string Rank);

public enum VirginiaDporListKind { Individual, Firm, BranchOffice, SettlementAgent }

public sealed class VirginiaDporBulkImportService(ApplicationDbContext db)
{
    public const string State = "Virginia";
    public const string ParserVersion = "virginia-dpor-regulant-v1";
    public const string SourcePage = "https://www.dpor.virginia.gov/RegulantLists";
    public const string FileBaseUrl = "https://www.dpor.virginia.gov/sites/default/files/Records%20and%20Documents/Regulant%20List/";
    public const string Board = "02";
    public const string SettlementAgentType = "Settlement Agent";
    public const string BranchOfficeKind = "Branch office";

    public static IReadOnlyList<VirginiaDporList> Lists { get; } =
    [
        new("0225s_act.txt", "25", "Salesperson", "Active", VirginiaDporListKind.Individual, "SALP"),
        new("0225s_inact.txt", "25", "Salesperson", "Inactive", VirginiaDporListKind.Individual, "SALP"),
        new("0225a_act.txt", "25", "Associate Broker", "Active", VirginiaDporListKind.Individual, "BRKR"),
        new("0225a_inact.txt", "25", "Associate Broker", "Inactive", VirginiaDporListKind.Individual, "BRKR"),
        new("0225p_act.txt", "25", "Principal Broker", "Active", VirginiaDporListKind.Individual, "BRKR"),
        new("0225p_inact.txt", "25", "Principal Broker", "Inactive", VirginiaDporListKind.Individual, "BRKR"),
        new("0225o_act.txt", "25", "Sole Proprietor", "Active", VirginiaDporListKind.Individual, "BRKR"),
        new("0225o_inact.txt", "25", "Sole Proprietor", "Inactive", VirginiaDporListKind.Individual, "BRKR"),
        new("0226f__crnt.txt", "26", "Firm", "Current", VirginiaDporListKind.Firm, "FIRM"),
        new("0226l__crnt.txt", "26", "Business Entity", "Current", VirginiaDporListKind.Firm, "ENTY"),
        new("0226o__crnt.txt", "26", "Sole Proprietorship", "Current", VirginiaDporListKind.Firm, "SOLE"),
        new("0226b__crnt.txt", "26", "Branch Office", "Current", VirginiaDporListKind.BranchOffice, "BRNH"),
        new("0227__crnt.txt", "27", SettlementAgentType, "Current", VirginiaDporListKind.SettlementAgent, "")
    ];

    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } = Lists
        .Select(list => new FloridaBulkFileDefinition(list.FileName, list.FileName, 20)
            { SourceUrl = FileBaseUrl + list.FileName, Encoding = "latin-1 (ASCII published)" })
        .ToArray();

    public static IReadOnlyList<string> Headers { get; } = Array.AsReadOnly(new[]
    {
        "BOARD", "OCCUPATION", "CERTIFICATE #", "INDIVIDUAL NAME", "BUSINESS NAME", "FIRST LINE ADDRESS", "SECOND LINE ADDRESS",
        "P O BOX #", "CITY", "STATE", "FIVE DIGIT ZIP CODE", "ZIP CODE EXTENSION", "PROVINCE", "COUNTRY", "POSTAL CODE",
        "EXPIRATION DATE", "CERTIFICATION DATE", "LICENSE RANK", "LICENSE SPECIALTY", "EMAILADDRESS"
    });

    public static VirginiaDporList List(string fileName) =>
        Lists.SingleOrDefault(list => list.FileName == fileName) ?? throw new InvalidDataException("Unknown Virginia DPOR source file.");

    /// <summary>The settlement agent list is currently published with a header only.</summary>
    public static bool AllowsEmpty(string fileName) => List(fileName).Kind == VirginiaDporListKind.SettlementAgent;

    public static long CheckFile(string path, string fileName, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, fileName, ct, progress).Count;

    public static List<VirginiaDporRecord> ReadRecords(string path, string fileName, CancellationToken ct = default,
        Action<long>? progress = null)
    {
        var list = List(fileName);
        var records = new List<VirginiaDporRecord>();
        using var reader = new StreamReader(path, Encoding.Latin1, detectEncodingFromByteOrderMarks: false);
        var header = reader.ReadLine() ?? throw new InvalidDataException($"Virginia DPOR {fileName} has no header.");
        if (!header.Split('\t').Select(value => value.Trim()).SequenceEqual(Headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Virginia DPOR {fileName} layout changed; expected the published {Headers.Count}-column schema.");
        long line = 1;
        while (reader.ReadLine() is { } text)
        {
            ct.ThrowIfCancellationRequested();
            line++;
            if (text.Trim().Length == 0) continue;
            var fields = Normalize(text.Split('\t'), line, fileName);
            if (fields[0] != Board || fields[1] != list.Occupation)
                throw new InvalidDataException($"Unexpected Virginia DPOR board/occupation in {fileName} at line {line}.");
            var certificate = fields[2].Trim();
            if (certificate.Length is 0 or > 10 || !certificate.All(char.IsAsciiDigit))
                throw new InvalidDataException($"Invalid Virginia DPOR certificate number in {fileName} at line {line}.");
            if (list.Rank.Length > 0 && !fields[17].Trim().Equals(list.Rank, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected Virginia DPOR license rank '{fields[17].Trim()}' in {fileName} at line {line}.");
            var number = Board + list.Occupation + certificate;
            var individual = Collapse(fields[3]);
            var business = Collapse(fields[4]);
            var name = (list.Kind == VirginiaDporListKind.Individual ? individual ?? business : business ?? individual)
                ?? $"Name not published ({number})";
            var city = Collapse(fields[8]);
            var zip = Collapse(fields[10]);
            var extension = Collapse(fields[11]);
            if (zip is not null && extension is not null && extension != "0000") zip += "-" + extension;
            var region = string.Join(' ', new[] { Collapse(fields[9]) ?? Collapse(fields[12]), zip ?? Collapse(fields[14]) }
                .Where(part => part is not null));
            var pobox = Collapse(fields[7]);
            var address = string.Join(", ", new[] { Collapse(fields[5]), Collapse(fields[6]), pobox is null ? null : "PO Box " + pobox,
                    city, region.Length == 0 ? null : region, Collapse(fields[13]) }
                .Where(part => part is not null));
            records.Add(new(line, fileName, number,
                Truncate(name, list.Kind == VirginiaDporListKind.Individual ? 160 : 200)!, list.LicenseType, list.LicenseStatus,
                city?.ToUpperInvariant(), Truncate(address.Length == 0 ? null : address, 500),
                Date(fields[15], fileName, line), Date(fields[16], fileName, line)));
            if (records.Count % 10_000 == 0) progress?.Invoke(records.Count);
            if (records.Count > 1_000_000)
                throw new InvalidDataException($"Virginia DPOR {fileName} exceeded the one-million-row safety limit.");
        }
        if (records.Count == 0 && !AllowsEmpty(fileName))
            throw new InvalidDataException($"Virginia DPOR {fileName} contains no records.");
        progress?.Invoke(records.Count);
        return records;
    }

    /// <summary>
    /// A few business names contain a literal tab, which adds columns; the extra values belong to the business name
    /// because every column after it still lines up.
    /// </summary>
    private static string[] Normalize(string[] fields, long line, string fileName)
    {
        if (fields.Length == Headers.Count) return fields;
        if (fields.Length < Headers.Count || fields.Length > Headers.Count + 3)
            throw new InvalidDataException($"Invalid Virginia DPOR row shape in {fileName} at line {line}.");
        var extra = fields.Length - Headers.Count;
        return fields.Take(4)
            .Append(string.Join(' ', fields.Skip(4).Take(extra + 1).Select(part => part.Trim())))
            .Concat(fields.Skip(5 + extra))
            .ToArray();
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Virginia DPOR acquisition manifest.");
        if (entries.Count != Definitions.Count)
            throw new InvalidDataException("Virginia DPOR archive must contain every real estate regulant list.");
        var ordered = new List<FloridaBulkManifestEntry>();
        foreach (var definition in Definitions)
        {
            var matches = entries.Where(entry => entry.File == definition.RelativePath).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Virginia DPOR archive requires one {definition.Name}.");
            var entry = matches[0];
            var columns = definition.Columns.ToString(CultureInfo.InvariantCulture);
            if (entry.SourceUrl != definition.Url || entry.Rows < 0 || entry.Rows == 0 && !AllowsEmpty(definition.Name)
                || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(columns, out var declared) || declared != entry.Rows)
                throw new InvalidDataException($"Invalid Virginia DPOR acquisition metadata for {definition.Name}.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using (var stream = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Virginia DPOR archive size/hash mismatch for {definition.Name}.");
            }
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, entry.Bytes)) != entry.Rows)
                throw new InvalidDataException($"Virginia DPOR archive row count mismatch for {definition.Name}.");
            progress?.Invoke(definition.Name, entry.Rows, entry.Bytes);
            ordered.Add(entry);
        }
        return ordered;
    }

    /// <summary>One record per license number: active before inactive, then the latest expiration, then the first listed.</summary>
    public static List<VirginiaDporRecord> Distinct(IEnumerable<VirginiaDporRecord> records) =>
        records.Select((record, order) => (Record: record, Order: order))
            .GroupBy(item => item.Record.LicenseNumber, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.Record.LicenseStatus == "Inactive" ? 1 : 0)
                .ThenByDescending(item => item.Record.ExpirationDate ?? DateOnly.MinValue)
                .ThenBy(item => item.Order).First())
            .OrderBy(item => item.Order)
            .Select(item => item.Record)
            .ToList();

    public static string FirmIndustry(VirginiaDporRecord firm) =>
        firm.LicenseType == SettlementAgentType ? RegulatoryIndustries.Settlement : RegulatoryIndustries.RealEstate;

    /// <summary>
    /// DPOR lists each branch office under its firm's business name without the firm's license number, so branches are
    /// linked by name to a firm, business entity or sole proprietorship, preferring one in the same city.
    /// </summary>
    public static List<VirginiaDporLocation> SelectLocations(IReadOnlyList<VirginiaDporRecord> firms, IReadOnlyList<VirginiaDporRecord> branches)
    {
        var matcher = new RegulatoryLocationMatcher(firms.Where(firm => firm.LicenseType != SettlementAgentType)
            .Select(firm => new RegulatoryLocationFirm(firm.LicenseNumber, firm.Name, firm.City)));
        return branches.Select(branch =>
        {
            var match = matcher.Match(branch.Name, branch.City);
            return new VirginiaDporLocation(branch, match.FirmLicenseNumber, match.MatchMethod);
        }).ToList();
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var byKind = entries.Select(entry => (List: List(Path.GetFileName(entry.File)),
                Records: ReadRecords(FloridaBulkImportService.ArchiveFile(root, entry.File), Path.GetFileName(entry.File), ct)))
            .ToList();
        List<VirginiaDporRecord> Of(params VirginiaDporListKind[] kinds) =>
            Distinct(byKind.Where(item => kinds.Contains(item.List.Kind)).SelectMany(item => item.Records));
        var people = Of(VirginiaDporListKind.Individual);
        var firms = Of(VirginiaDporListKind.Firm, VirginiaDporListKind.SettlementAgent);
        var locations = SelectLocations(firms, Of(VirginiaDporListKind.BranchOffice));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion
            + string.Concat(entries.Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved)
                throw new InvalidDataException("Virginia DPOR archive conflicts with an existing snapshot.");
            if (await db.RegulatoryLicensees.CountAsync(record => record.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(record => record.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.CountAsync(record => record.SnapshotId == id, ct) != locations.Count)
                throw new InvalidDataException("Existing Virginia DPOR snapshot coverage does not match the validated archive.");
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
            [[id, State, "Virginia DPOR Real Estate Board regulant lists: active and inactive licensees, current firms, branch offices and settlement agents",
                SourcePage, ParserVersion, entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        var firmIds = firms.ToDictionary(firm => firm.LicenseNumber, _ => Guid.NewGuid(), StringComparer.Ordinal);
        long total = 0;
        foreach (var group in firms.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(firm => new object?[]
            {
                firmIds[firm.LicenseNumber], id, State, firm.LicenseNumber, firm.Name, null, firm.LicenseStatus,
                firm.CertificationDate, firm.ExpirationDate, firm.Address, null, FirmIndustry(firm)
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
                BranchOfficeKind, location.MatchMethod
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
                person.LicenseType, person.LicenseStatus,
                person.CertificationDate, null, person.ExpirationDate, null, null, null, false, false, false
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

    // DPOR pads names with trailing and doubled spaces.
    private static string? Collapse(string value)
    {
        var normalized = string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static DateOnly? Date(string value, string fileName, long line)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0) return null;
        return DateOnly.TryParseExact(normalized, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidDataException($"Invalid Virginia DPOR date '{normalized}' in {fileName} at line {line}.");
    }
}
