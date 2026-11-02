using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lms.Application.Services;

public sealed record FloridaBulkFileDefinition(string Name, string RelativePath, int Columns)
{
    public string? SourceUrl { get; init; }
    public string Encoding { get; init; } = "utf-8 (optional BOM)";
    public string Url => SourceUrl ?? "https://www2.myfloridalicense.com/sto/file_download/extracts/" + Name;
}

public sealed record FloridaBulkManifestEntry(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("rows")] long Rows,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("source_url")] string SourceUrl,
    [property: JsonPropertyName("verified_at_utc")] DateTimeOffset VerifiedAtUtc,
    [property: JsonPropertyName("acquisition")] string Acquisition,
    [property: JsonPropertyName("columns")] Dictionary<string, long> Columns)
{
    [JsonPropertyName("source_published_at")] public DateTimeOffset? SourcePublishedAt => null;
    [JsonPropertyName("encoding")] public string Encoding { get; init; } = "utf-8 (optional BOM)";
    [JsonPropertyName("source_last_modified_utc")] public DateTimeOffset? SourceLastModifiedUtc { get; init; }
    [JsonPropertyName("source_etag")] public string? SourceETag { get; init; }
    [JsonPropertyName("parsing")] public string Parsing { get; init; } = "Quoted physical rows split on DBPR field boundary \",\"; exact field count required; literal interior quotes preserved.";
    [JsonPropertyName("source_row_count")] public long? SourceRowCount { get; init; }
    [JsonPropertyName("source_row_count_url")] public string? SourceRowCountUrl { get; init; }
    [JsonPropertyName("source_row_count_verified_at_utc")] public DateTimeOffset? SourceRowCountVerifiedAtUtc { get; init; }
}

public sealed record FloridaBulkRow(string LicenseNumber, string Name, string? Type, string? Level,
    string Status, DateOnly? OriginalDate, DateOnly? IssuedDate, DateOnly? RenewalDate,
    string? EmployerNumber, string? EmployerName, string? TradeName, string? Address, bool IsFirm);

public static class FloridaBulkCsv
{
    public static string[] ReadFields(string line, int expectedColumns)
    {
        ArgumentNullException.ThrowIfNull(line);
        line = line.TrimStart('\uFEFF');
        if (line.Length < 2 || line[0] != '"' || line[^1] != '"')
            throw new InvalidDataException("Invalid quoted DBPR physical row.");
        var fields = line[1..^1].Split("\",\"", StringSplitOptions.None);
        if (fields.Length != expectedColumns)
            throw new InvalidDataException($"Expected {expectedColumns} fields, found {fields.Length}.");
        return fields.Select(f => f.Trim()).ToArray();
    }
}

/// <summary>Validates immutable DBPR archives before opening a short-lived bulk write transaction.</summary>
public sealed class FloridaBulkImportService(ApplicationDbContext db)
{
    public const string ParserVersion = "dbpr-physical-v1";
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new FloridaBulkFileDefinition("REALESTATE2501LICENSE_1.csv", @"licensees\real-estate\REALESTATE2501LICENSE_1.csv", 21),
        new FloridaBulkFileDefinition("RealEstateCorpLicense.csv", @"licensees\firms\RealEstateCorpLicense.csv", 21),
        new FloridaBulkFileDefinition("RealEstateSchoolLicense.csv", @"licensees\schools-instructors\RealEstateSchoolLicense.csv", 21),
        new FloridaBulkFileDefinition("lic64appr.csv", @"licensees\appraisal\lic64appr.csv", 20),
        new FloridaBulkFileDefinition("re_salesperson.csv", @"continuing-education\real-estate\re_salesperson.csv", 12),
        new FloridaBulkFileDefinition("re_appraiser.csv", @"continuing-education\appraisal\re_appraiser.csv", 12)
    });

    public static string ArchiveFile(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
            || relative.Contains(':') || relative.Split('\\', '/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Invalid relative archive filename.");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive filename escapes its directory.");
        for (var current = new FileInfo(path) as FileSystemInfo; current is not null; current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Archive paths must not contain symbolic links.");
        return path;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken cancellationToken = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Missing archive manifest.");
        // Validate all manifest names, including excluded regional files, before selecting statewide inputs.
        foreach (var entry in entries)
            ArchiveFile(root, entry.File);
        var selected = new List<FloridaBulkManifestEntry>();
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = entries.Where(e => e.File.Replace('/', '\\') == definition.RelativePath).ToList();
            if (matches.Count != 1)
                throw new InvalidDataException($"Expected one manifest entry for {definition.Name}.");
            var entry = matches[0];
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(definition.Columns.ToString(CultureInfo.InvariantCulture), out var declaredRows)
                || declaredRows != entry.Rows)
                throw new InvalidDataException($"Invalid manifest metadata for {definition.Name}.");
            var path = ArchiveFile(root, entry.File);
            await using (var file = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
                if (file.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Archive hash/size mismatch: {definition.Name}.");
            }
            long rows = 0;
            await foreach (var fields in ReadFieldsAsync(path, definition.Columns, cancellationToken))
            {
                var row = ParseRow(definition, fields);
                if (row is not null)
                {
                    var identity = Identity(row);
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row))));
                    if (identities.TryGetValue(identity, out var previous) && previous != hash)
                        throw new InvalidDataException($"Conflicting duplicate license {row.LicenseNumber} ({row.Type}).");
                    identities[identity] = hash;
                }
                rows++;
                if (rows % 10000 == 0) progress?.Invoke(definition.Name, rows, entry.Bytes);
            }
            if (rows != entry.Rows)
                throw new InvalidDataException($"Row count mismatch: {definition.Name}.");
            progress?.Invoke(definition.Name, rows, entry.Bytes);
            selected.Add(entry);
        }
        if (identities.Count == 0) throw new InvalidDataException("No directory records in archive.");
        return selected.AsReadOnly();
    }

    public static async IAsyncEnumerable<string[]> ReadFieldsAsync(string path, int columns,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        long lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++lineNumber == 1) line = line.TrimStart('\uFEFF');
            yield return FloridaBulkCsv.ReadFields(line, columns);
        }
        if (lineNumber == 0) throw new InvalidDataException("Empty DBPR file.");
    }

    public static FloridaBulkRow? ParseRow(FloridaBulkFileDefinition definition, string[] f)
    {
        if (f.Length != definition.Columns) throw new InvalidDataException("Invalid DBPR field count.");
        if (f.All(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Empty DBPR record.");
        if (definition.Columns == 12)
        {
            Date(f[7], false);
            Date(f[11], false);
            return null; // CE history is archived, not a license or an inferred eligibility policy.
        }
        if (definition.Columns == 20)
        {
            if (f[0] != "64") throw new InvalidDataException("Unexpected appraisal board.");
            var type = f[1] switch
            {
                "RI" => "Registered Trainee Appraiser", "RH" => "Licensed Appraiser",
                "RD" => "Certified Residential Appraiser", "RZ" => "General Appraiser",
                "IR" => "Residential Instructor", "GA" => "General Instructor",
                "TP" => "Temporary Practice Permit", _ => f[1]
            };
            if (f[1].Length == 0 || !f[12].All(char.IsAsciiDigit) || f[12].Length == 0)
                throw new InvalidDataException("Invalid appraisal license identity.");
            var primary = f[13] switch { "C" => "Current", "P" => "Current with Probation", "S" => "Suspended", _ => f[13] };
            var secondary = f[14] switch { "A" => "Active", "I" => "Inactive", _ => f[14] };
            var appraisalOriginal = Date(f[15], false);
            var appraisalIssued = Date(f[16], false);
            var appraisalRenewal = Date(f[17], false);
            if (f[1] is "1" or "2" or "PVDR") return null;
            var managementCompany = f[1] is "MC" or "FMC";
            return new(f[1] + f[12].TrimStart('0'), Required(f[2], managementCompany ? 200 : 160), managementCompany ? null : type, null,
                Status(primary, secondary), appraisalOriginal, appraisalIssued, appraisalRenewal,
                null, null, Null(f[3]), managementCompany ? Null(string.Join(", ", f[5..11].Where(v => v.Length > 0))) : null, managementCompany);
        }
        var rank = f[3].Split(' ', 2)[0];
        var category = f[0].Split(' ', 2)[0];
        if (definition.Name == "REALESTATE2501LICENSE_1.csv" && category != "2501"
            || definition.Name == "RealEstateCorpLicense.csv" && category is not ("2502" or "2503" or "2504")
            || definition.Name == "RealEstateSchoolLicense.csv" && category is not ("2505" or "2506" or "2507"))
            throw new InvalidDataException("Unexpected real-estate category.");
        if (rank.Length == 0 || f[11].Length == 0 || !f[11].All(char.IsAsciiDigit))
            throw new InvalidDataException("Invalid real-estate license identity.");
        var number = (rank == "BL" ? "BK" : rank) + f[11].TrimStart('0');
        if (f[17].Length > 0 && f[17] != number && !(rank == "BL" && f[17] == "BL" + f[11].TrimStart('0')))
            throw new InvalidDataException($"License number/rank mismatch: {f[17]}.");
        var original = Date(f[14], true);
        var issued = Date(f[15], true);
        var renewal = Date(f[16], true);
        var status = Status(f[12], f[13]);
        if (category is "2506" or "2507") return null; // Schools/locations remain in the immutable source archive.
        var firm = category is "2502" or "2503" or "2504";
        return new(number, Required(f[1], firm ? 200 : 160),
            firm ? null : rank switch { "BK" => "Broker", "BL" => "Broker Sales", "SL" => "Sales Associate", "ZH" => "Real Estate Instructor", _ => f[3] },
            null, status, original, issued, renewal, Null(f[20]), Null(f[19]), Null(f[2]),
            firm ? Null(string.Join(", ", f[4..10].Where(v => v.Length > 0))) : null, firm);
    }

    private static string Status(string primary, string secondary)
    {
        primary = primary switch { "Probation" => "Current with Probation", "Invol Inactive" => "Involuntary Inactive", _ => primary };
        secondary = secondary == "Invol Inactive" ? "Involuntary Inactive" : secondary;
        return Required(string.Join(", ", new[] { primary, secondary }.Where(s => s.Length > 0)), 40);
    }

    private static DateOnly? Date(string value, bool realEstate)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // DBPR uses two-digit years; an explicit cutoff avoids machine-culture calendar changes.
        if (realEstate)
        {
            if (value.Length != 9 || !DateOnly.TryParseExact(value[..7] + (int.Parse(value[7..], CultureInfo.InvariantCulture) <= 49 ? "20" : "19") + value[7..],
                "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new InvalidDataException($"Invalid DBPR date '{value}'.");
            return parsed;
        }
        if (!DateOnly.TryParseExact(value, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException($"Invalid DBPR date '{value}'.");
        return date;
    }

    private static string Required(string value, int maximum) => string.IsNullOrWhiteSpace(value) || value.Length > maximum
        ? throw new InvalidDataException("Missing or overlength DBPR field.") : value;
    private static string? Null(string value) => value.Length == 0 ? null : value;
    private static string Identity(FloridaBulkRow row) => (row.IsFirm ? "F:" : "L:") + row.LicenseNumber + ":" + row.Type;

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken cancellationToken = default,
        Action<string, long, long>? progress = null, Action<long>? stagingProgress = null)
    {
        var entries = await ValidateArchiveAsync(root, cancellationToken, progress);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion + string.Join("|", entries.Select(e => e.Sha256.ToUpperInvariant()))));
        var snapshotId = new Guid(digest.AsSpan(0, 16));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(s => s.Id == snapshotId, cancellationToken);
        if (existing is not null)
        {
            if (existing.State != "Florida" || existing.ParserVersion != ParserVersion
                || existing.Status is not (RegulatorySnapshotStatuses.PendingReview or RegulatorySnapshotStatuses.Approved))
                throw new InvalidDataException("The validated bulk archive conflicts with an existing snapshot.");
            if (existing.Status == RegulatorySnapshotStatuses.PendingReview)
            {
                existing.Status = RegulatorySnapshotStatuses.Approved;
                existing.ReviewedAtUtc = DateTime.UtcNow;
                existing.ReviewedByUserId = null;
            }
            var currentId = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.State == "Florida")).Select(snapshot => (Guid?)snapshot.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (currentId != snapshotId || existing.ActivatedAtUtc is null)
                existing.ActivatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return snapshotId;
        }
        await InsertBatch(connection, transaction.GetDbTransaction(), "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [new object?[] { snapshotId, "Florida", "DBPR weekly bulk (coverage/source publication date unverified)",
                Definitions[0].Url, ParserVersion, entries.Max(e => e.VerifiedAtUtc).UtcDateTime, DateTime.UtcNow,
                RegulatorySnapshotStatuses.Approved, DateTime.UtcNow, DateTime.UtcNow }], cancellationToken);
        var firms = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var employers = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long stagedRecords = 0;
        // Firms first, then people: relationships only where an employer number uniquely matches an actual firm.
        foreach (var firmPass in new[] { true, false })
        {
            var batch = new List<object?[]>();
            var affiliations = new List<object?[]>();
            foreach (var definition in Definitions.Where(d => d.Columns != 12
                && ((d.Name == "RealEstateCorpLicense.csv") == firmPass || d.Columns == 20)))
            {
                await foreach (var fields in ReadFieldsAsync(ArchiveFile(root, definition.RelativePath), definition.Columns, cancellationToken))
                {
                    var row = ParseRow(definition, fields);
                    if (row is null || row.IsFirm != firmPass || !seen.Add(Identity(row))) continue;
                    var id = Guid.NewGuid();
                    if (firmPass)
                    {
                        firms.Add(row.LicenseNumber, id);
                        var numeric = new string(row.LicenseNumber.SkipWhile(c => !char.IsAsciiDigit(c)).ToArray());
                        employers[numeric] = employers.ContainsKey(numeric) ? null : id;
                        batch.Add([id, snapshotId, "Florida", row.LicenseNumber, row.Name, row.TradeName, row.Status, row.IssuedDate, row.RenewalDate, row.Address]);
                    }
                    else
                    {
                        batch.Add([id, snapshotId, "Florida", row.LicenseNumber, row.Name, row.Type, row.Level, row.Status,
                            row.OriginalDate, row.IssuedDate, row.RenewalDate, false, false, false]);
                        if (row.EmployerNumber is { } employer)
                        {
                            Guid? firmId = firms.TryGetValue(employer, out var exact) ? exact
                                : employers.TryGetValue(employer, out var numericMatch) ? numericMatch : null;
                            if (firmId.HasValue)
                                affiliations.Add([Guid.NewGuid(), snapshotId, firmId.Value, id, false]);
                        }
                    }
                    if (batch.Count >= 50) await Flush();
                }
            }
            await Flush();
            async Task Flush()
            {
                if (batch.Count == 0) return;
                await InsertBatch(connection, transaction.GetDbTransaction(), firmPass ? "RegulatoryFirms" : "RegulatoryLicensees",
                    firmPass ? ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "TradeName", "LicenseStatus", "LicenseIssuedDate", "LicenseRenewalDate", "Address"]
                        : ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel", "LicenseStatus", "OriginalLicenseDate", "LicenseIssuedDate", "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"], batch, cancellationToken);
                if (affiliations.Count > 0)
                    await InsertBatch(connection, transaction.GetDbTransaction(), "RegulatoryFirmLicenseeAffiliations",
                        ["Id", "SnapshotId", "FirmId", "LicenseeId", "IsBrokerInCharge"], affiliations, cancellationToken);
                batch.Clear();
                affiliations.Clear();
                stagedRecords = seen.Count;
                if (stagedRecords % 1000 == 0) stagingProgress?.Invoke(stagedRecords);
            }
        }
        stagingProgress?.Invoke(stagedRecords);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        return snapshotId;
    }

    internal static async Task InsertBatch(DbConnection connection, DbTransaction transaction, string table,
        string[] columns, List<object?[]> rows, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 120;
        var values = new List<string>();
        foreach (var row in rows)
        {
            var names = new List<string>();
            foreach (var value in row)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@p" + command.Parameters.Count;
                parameter.Value = value switch
                {
                    null => DBNull.Value, Guid guid => guid.ToString().ToUpperInvariant(),
                    DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateTime time => time.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
                    bool flag => flag ? 1 : 0, _ => value
                };
                command.Parameters.Add(parameter);
                names.Add(parameter.ParameterName);
            }
            values.Add("(" + string.Join(",", names) + ")");
        }
        command.CommandText = $"INSERT INTO \"{table}\" ({string.Join(",", columns.Select(c => "\"" + c + "\""))}) VALUES {string.Join(",", values)}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
