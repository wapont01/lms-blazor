using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public sealed record NevadaNredImportResult(
    Guid SnapshotId,
    int SourceRows,
    int NumberedLicenses,
    int RowsWithoutLicenseNumber,
    IReadOnlyDictionary<string, int> SourceRowsByType);

public sealed class NevadaNredBulkImportService(ApplicationDbContext db)
{
    public const string ParserVersion = "nred-active-licensee-lists-v2-all-2026-09";
    public const string SourceUrl = "https://red.nv.gov/Content/Administration/Active_Licensee_Lists/";
    public static readonly DateTime CaptureDateUtc = new(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<string> RealEstateLicenseTypes { get; } =
        Array.AsReadOnly(new[]
        {
            "Broker", "Broker-Salesperson", "Business Broker", "Community Association Manager",
            "Property Manager", "Salesperson", "Timeshare Agent"
        });

    public static IReadOnlyList<string> AllLicenseTypes { get; } =
        Array.AsReadOnly(new[]
        {
            "Appraisal Management Company",
            "Appraiser",
            "Broker",
            "Broker-Salesperson",
            "Business Broker",
            "Community Association Manager",
            "Energy Auditor",
            "Homeowners Association",
            "Inspector of Structures",
            "Property Manager",
            "Salesperson",
            "Timeshare Agent"
        });

    private static readonly (string FileName, string LicenseType, string NumberColumn)[] SourceFiles =
    [
        ("NRED-ACTIVE-AMC.xlsx", "Appraisal Management Company", "License No"),
        ("NRED-ACTIVE-APPRAISERS.xlsx", "Appraiser", "License No"),
        ("NRED-ACTIVE-BROKERS.xlsx", "Broker", "License No"),
        ("NRED-ACTIVE-BROKER-SALESPERSONS.xlsx", "Broker-Salesperson", "License No"),
        ("NRED-ACTIVE-BUSINESS-BROKERS.xlsx", "Business Broker", "License No"),
        ("NRED-ACTIVE-CAM.xlsx", "Community Association Manager", "License No"),
        ("NRED-ACTIVE-ENERGY-AUDITORS.xlsx", "Energy Auditor", "License No"),
        ("NRED-ACTIVE-HOA.xlsx", "Homeowners Association", "SOS#"),
        ("NRED-ACTIVE-IOS.xlsx", "Inspector of Structures", "License No"),
        ("NRED-ACTIVE-PROPERTY-MANAGERS.xlsx", "Property Manager", "License No"),
        ("NRED-ACTIVE-SALESPERSONS.xlsx", "Salesperson", "License No"),
        ("NRED-ACTIVE-TIMESHARE-AGENTS.xlsx", "Timeshare Agent", "License No")
    ];

    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
        Array.AsReadOnly(SourceFiles.Select(source => new FloridaBulkFileDefinition(source.FileName, source.FileName, 0)
        {
            SourceUrl = "https://red.nv.gov/uploadedFiles/rednvgov/Content/Administration/Public_Records/" + source.FileName,
            Encoding = "Open XML workbook"
        }).ToArray());

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public async Task<NevadaNredImportResult> ImportCaptureAsync(
        string? workspaceRoot = null, CancellationToken ct = default)
    {
        var sourceRoot = CaptureSeedRoot.Resolve(workspaceRoot)
            ?? throw new DirectoryNotFoundException("Could not locate the workspace source root.");
        var captureRoot = Path.Combine(sourceRoot, "data", "rosters", "nevada", "undated");
        var manifestPath = Path.Combine(captureRoot, "manifest.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        var entries = manifest.RootElement.GetProperty("Files").EnumerateArray()
            .ToDictionary(entry => Path.GetFileName(entry.GetProperty("Path").GetString()!), StringComparer.OrdinalIgnoreCase);
        var sources = SourceFiles.Select(source =>
        {
            if (!entries.TryGetValue(source.FileName, out var entry))
                throw new InvalidDataException($"Nevada source manifest is missing {source.FileName}.");
            return new FloridaBulkManifestEntry(entry.GetProperty("Path").GetString()!, 0,
                entry.GetProperty("Bytes").GetInt64(), entry.GetProperty("Sha256").GetString()!,
                Definitions.Single(file => file.Name == source.FileName).Url, new(CaptureDateUtc),
                "existing capture", new());
        }).ToArray();
        return await ImportSourcesAsync(captureRoot, sources, CaptureDateUtc, ct);
    }

    public static long CheckFile(string path, string fileName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var source = SourceFiles.Single(source => source.FileName == fileName);
        return ReadWorkbook(path, source.FileName, source.LicenseType, source.NumberColumn, ct).Count;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Nevada archive manifest is empty.");
        if (entries.Count != Definitions.Count)
            throw new InvalidDataException("Nevada archive must contain all 12 supported NRED lists.");
        foreach (var definition in Definitions)
        {
            var matches = entries.Where(entry => entry.File == definition.RelativePath).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Nevada archive requires one {definition.Name}.");
            var entry = matches[0];
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default)
                throw new InvalidDataException($"Invalid Nevada acquisition evidence for {definition.Name}.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Nevada archive hash/size mismatch for {definition.Name}.");
            if (CheckFile(path, definition.Name, ct) != entry.Rows)
                throw new InvalidDataException($"Nevada archive row count mismatch for {definition.Name}.");
            progress?.Invoke(definition.Name, entry.Rows, entry.Bytes);
        }
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var result = await ImportSourcesAsync(root, entries, entries.Max(entry => entry.VerifiedAtUtc).UtcDateTime, ct, staged);
        return result.SnapshotId;
    }

    private async Task<NevadaNredImportResult> ImportSourcesAsync(
        string captureRoot, IReadOnlyList<FloridaBulkManifestEntry> entries, DateTime retrievedAtUtc,
        CancellationToken ct, Action<long>? staged = null)
    {
        var rows = new List<NevadaNredSourceRow>();
        var hashes = new List<string>(SourceFiles.Length);
        var rowsByType = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in SourceFiles)
        {
            ct.ThrowIfCancellationRequested();
            var entry = entries.Single(entry => Path.GetFileName(entry.File) == source.FileName);
            var path = FloridaBulkImportService.ArchiveFile(captureRoot, entry.File);
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length != entry.Bytes)
                throw new InvalidDataException($"Nevada source size mismatch for {source.FileName}.");
            await using (var stream = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                var expectedHash = entry.Sha256;
                if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Nevada source hash mismatch for {source.FileName}.");
                hashes.Add(hash);
            }

            var sourceRows = ReadWorkbook(path, source.FileName, source.LicenseType, source.NumberColumn, ct);
            rowsByType.Add(source.LicenseType, sourceRows.Count);
            rows.AddRange(sourceRows);
        }

        if (rows.Count == 0)
            throw new InvalidDataException("Nevada source workbooks contained no rows.");

        var licensees = Deduplicate(rows);
        if (licensees.Count == 0)
            throw new InvalidDataException("Nevada source workbooks contained no usable numbered licenses.");
        var digestInput = ParserVersion + string.Concat(hashes.OrderBy(value => value, StringComparer.Ordinal));
        var snapshotHash = SHA256.HashData(Encoding.UTF8.GetBytes(digestInput));
        var snapshotId = new Guid(snapshotHash.AsSpan(0, 16));
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == snapshotId, ct);
        if (existing is not null)
        {
            var sourceCount = await db.RegulatorySourceRecords.CountAsync(record => record.SnapshotId == snapshotId, ct);
            var licenseeCount = await db.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshotId, ct);
            if (existing.State != "Nevada" || existing.ParserVersion != ParserVersion
                || existing.Status != RegulatorySnapshotStatuses.Approved
                || sourceCount != rows.Count || licenseeCount != licensees.Count)
                throw new InvalidDataException("Existing all-category Nevada snapshot does not match the validated sources.");
            var currentId = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.AsNoTracking()
                    .Where(snapshot => snapshot.State == "Nevada")).Select(snapshot => (Guid?)snapshot.Id)
                .FirstOrDefaultAsync(ct);
            if (currentId != snapshotId || existing.ActivatedAtUtc is null)
            {
                existing.ActivatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return new(snapshotId, rows.Count, licensees.Count,
                rows.Count(row => row.LicenseNumber is null), rowsByType);
        }

        var importedAt = DateTime.UtcNow;
        var snapshot = new RegulatoryDataSnapshot
        {
            Id = snapshotId,
            State = "Nevada",
            SourceName = "Nevada NRED active licensing lists (12 source types)",
            SourceUrl = SourceUrl,
            ParserVersion = ParserVersion,
            RetrievedAtUtc = retrievedAtUtc,
            ImportedAtUtc = importedAt,
            ActivatedAtUtc = importedAt,
            ReviewedAtUtc = importedAt,
            Status = RegulatorySnapshotStatuses.Approved
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.RegulatoryDataSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);

        long rowNumber = 0;
        foreach (var batch in rows.Chunk(250))
        {
            db.RegulatorySourceRecords.AddRange(batch.Select(row => new RegulatorySourceRecord
            {
                SnapshotId = snapshotId,
                SourceRowNumber = ++rowNumber,
                LicenseNumber = row.LicenseNumber,
                LicenseType = row.LicenseType,
                LicenseStatus = "Active",
                SourceRecordJson = JsonSerializer.Serialize(new
                {
                    row.SourceFile,
                    row.SourceFields
                })
            }));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            staged?.Invoke(rowNumber);
        }

        foreach (var batch in licensees.Chunk(250))
        {
            db.RegulatoryLicensees.AddRange(batch.Select(row => new RegulatoryLicensee
            {
                SnapshotId = snapshotId,
                State = "Nevada",
                LicenseNumber = row.LicenseNumber,
                Name = row.Name,
                LicenseType = row.LicenseType,
                SourceLicenseType = row.LicenseType,
                LicenseLevel = row.LicenseType.Length <= 20 ? row.LicenseType : null,
                LicenseStatus = "Active",
                LicenseIssuedDate = row.FirstIssuedDate,
                OriginalLicenseDate = row.FirstIssuedDate,
                LicenseRenewalDate = row.ExpirationDate,
                RelatedLicenseName = row.Company,
                RelatedLicenseType = row.Company is null ? null : "Company"
            }));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(ct);
        return new(snapshotId, rows.Count, licensees.Count,
            rows.Count(row => row.LicenseNumber is null), rowsByType);
    }

    private static List<NevadaNredSourceRow> ReadWorkbook(
        string path, string fileName, string licenseType, string numberColumn, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException($"Nevada workbook '{fileName}' has no first worksheet.");
        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);
        var sourceRows = sheet.Root?.Element(SpreadsheetNamespace + "sheetData")?
            .Elements(SpreadsheetNamespace + "row").ToArray();
        if (sourceRows is null || sourceRows.Length < 2)
            throw new InvalidDataException($"Nevada workbook '{fileName}' contains no data rows.");

        var headers = ReadRow(sourceRows[0], sharedStrings);
        if (headers.Length == 0 || headers.Any(string.IsNullOrWhiteSpace)
            || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException($"Nevada workbook '{fileName}' has invalid column headers.");
        var columns = headers.Select((value, index) => (Name: value.Trim(), Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { numberColumn, "Name" })
            if (!columns.ContainsKey(required))
                throw new InvalidDataException($"Nevada workbook '{fileName}' is missing column '{required}'.");

        var result = new List<NevadaNredSourceRow>(sourceRows.Length - 1);
        foreach (var sourceRow in sourceRows.Skip(1))
        {
            ct.ThrowIfCancellationRequested();
            var values = ReadRow(sourceRow, sharedStrings);
            if (values.All(string.IsNullOrWhiteSpace))
                continue;
            string Value(string key) => columns.TryGetValue(key, out var index) && index < values.Length
                ? values[index].Trim() : string.Empty;

            var sourceFields = headers.Select((header, index) =>
                    new KeyValuePair<string, string>(header.Trim(), index < values.Length ? values[index].Trim() : string.Empty))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var number = Optional(Value(numberColumn));
            var name = Optional(Value("Name"));
            var company = Optional(Value("Company"));
            result.Add(new(fileName, licenseType, number, name, company,
                ParseDate(Value("First Issuance Date"), fileName, sourceRow),
                ParseDate(Value("Expiration Date"), fileName, sourceRow),
                sourceFields));
        }
        return result;
    }

    private static List<RegulatoryLicenseeSource> Deduplicate(IReadOnlyList<NevadaNredSourceRow> rows)
    {
        var byIdentity = new Dictionary<string, RegulatoryLicenseeSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.LicenseType == "Homeowners Association" || row.LicenseNumber is null || row.Name is null
                || row.LicenseNumber.Equals("SAME AS MASTER", StringComparison.OrdinalIgnoreCase))
                continue;
            if (row.LicenseNumber.Length > 40 || row.Name.Length > 160 || row.LicenseType.Length > 40
                || row.Company?.Length > 160)
                throw new InvalidDataException($"Nevada {row.LicenseType} data exceeds the LMS field limits.");

            var key = $"{row.LicenseType}\u001f{row.LicenseNumber}";
            var candidate = new RegulatoryLicenseeSource(row.LicenseNumber, row.Name, row.LicenseType,
                row.Company, row.FirstIssuedDate, row.ExpirationDate);
            if (!byIdentity.TryGetValue(key, out var existing))
            {
                byIdentity.Add(key, candidate);
                continue;
            }

            var dateComparison = Nullable.Compare(candidate.ExpirationDate, existing.ExpirationDate);
            if (dateComparison > 0)
            {
                byIdentity[key] = candidate;
                continue;
            }
            if (dateComparison < 0)
                continue;
            if (candidate.Name != existing.Name || candidate.FirstIssuedDate != existing.FirstIssuedDate
                || candidate.Company != existing.Company)
                throw new InvalidDataException($"Nevada {row.LicenseType} has conflicting duplicate license '{row.LicenseNumber}'.");
        }
        return byIdentity.Values.ToList();
    }

    private static string[] ReadRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<int, string>();
        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
        {
            var reference = cell.Attribute("r")?.Value
                ?? throw new InvalidDataException("A Nevada workbook cell has no reference.");
            var columnIndex = GetColumnIndex(reference);
            var type = cell.Attribute("t")?.Value;
            var value = type == "inlineStr"
                ? string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value))
                : cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
            if (type == "s")
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    || index < 0 || index >= sharedStrings.Count)
                    throw new InvalidDataException($"Nevada workbook cell '{reference}' has an invalid shared string.");
                value = sharedStrings[index];
            }
            cells[columnIndex] = value;
        }

        if (cells.Count == 0)
            return [];
        var values = Enumerable.Repeat(string.Empty, cells.Keys.Max() + 1).ToArray();
        foreach (var (index, value) in cells)
            values[index] = value;
        return values;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Root?.Elements(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToList() ?? throw new InvalidDataException("Nevada workbook shared strings are invalid.");
    }

    private static int GetColumnIndex(string reference)
    {
        var index = 0;
        var found = false;
        foreach (var character in reference)
        {
            var upper = char.ToUpperInvariant(character);
            if (upper is < 'A' or > 'Z')
                break;
            found = true;
            index = checked(index * 26 + upper - 'A' + 1);
        }
        if (!found)
            throw new InvalidDataException($"Nevada workbook cell reference '{reference}' is invalid.");
        return index - 1;
    }

    private static DateOnly? ParseDate(string value, string fileName, XElement row)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Nevada workbook '{fileName}' has an invalid date at row {row.Attribute("r")?.Value}.", exception);
            }
        }
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        throw new InvalidDataException($"Nevada workbook '{fileName}' has an invalid date '{value}' at row {row.Attribute("r")?.Value}.");
    }

    private static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("N/A", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();

    private sealed record NevadaNredSourceRow(
        string SourceFile,
        string LicenseType,
        string? LicenseNumber,
        string? Name,
        string? Company,
        DateOnly? FirstIssuedDate,
        DateOnly? ExpirationDate,
        IReadOnlyDictionary<string, string> SourceFields);

    private sealed record RegulatoryLicenseeSource(
        string LicenseNumber,
        string Name,
        string LicenseType,
        string? Company,
        DateOnly? FirstIssuedDate,
        DateOnly? ExpirationDate);
}
