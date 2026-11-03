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

public sealed record ArizonaAdreIndividual(
    long SourceRowNumber,
    string LicenseNumber,
    string Name,
    string LicenseType,
    string LicenseStatus,
    string? EmploymentType,
    DateOnly? OriginalDate,
    DateOnly? ExpirationDate,
    string? EmployerLicenseNumber,
    string? EmployerName,
    DateTime? LastModified);

public sealed record ArizonaAdreEntity(
    long SourceRowNumber,
    string LicenseNumber,
    string LegalName,
    string? TradeName,
    string LicenseType,
    string LicenseStatus,
    DateOnly? OriginalDate,
    DateOnly? ExpirationDate,
    string? Address,
    string? DesignatedBrokerLicenseNumber,
    DateTime? LastModified);

public sealed class ArizonaAdreBulkImportService(ApplicationDbContext db)
{
    public const string State = "Arizona";
    public const string ParserVersion = "arizona-adre-pdb-v1";
    public const string RealEstateCategory = "Real Estate";
    public const string SourcePage = "https://services.azre.gov/PdbWeb/List/ViewLists";
    public const string IndividualsFile = "Individuals.csv";
    public const string EntitiesFile = "Entities.csv";
    public const string IndividualsUrl = "https://services.azre.gov/PdbWeb/List/DownloadList/1";
    public const string EntitiesUrl = "https://services.azre.gov/PdbWeb/List/DownloadList/2";

    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(IndividualsFile, IndividualsFile, 22) { SourceUrl = IndividualsUrl },
        new(EntitiesFile, EntitiesFile, 18) { SourceUrl = EntitiesUrl }
    ];

    public static IReadOnlyList<string> IndividualHeaders { get; } = Array.AsReadOnly(new[]
    {
        "LastName", "FirstName", "MiddleName", "OriginalDate", "ExpireDate", "LicNumber", "LicCategory", "LicType",
        "LicStatus", "EmploymentType", "EmployerLegalName", "EmployerDBAName", "EmployerLicNumber", "EmployerPhone",
        "EmployerFax", "MailingAddress1", "MailingAddress2", "MailingCity", "MailingState", "MailingZip",
        "MailingCounty", "LastModified"
    });

    public static IReadOnlyList<string> EntityHeaders { get; } = Array.AsReadOnly(new[]
    {
        "LicNumber", "LegalName", "DBAName", "OriginalDate", "ExpireDate", "LicStatus", "LicType", "LicCategory",
        "Phone", "Fax", "Address1", "Address2", "City", "State", "Zip", "County", "DesignatedBrokerLicNumber",
        "LastModified"
    });

    /// <summary>Counts every physical source row (all categories) after validating the file layout.</summary>
    public static long CheckFile(string path, string fileName, CancellationToken ct = default, Action<long>? progress = null) =>
        fileName switch
        {
            IndividualsFile => ReadRows(path, IndividualHeaders, "ADRE individuals", ct, progress, (_, _) => { }),
            EntitiesFile => ReadRows(path, EntityHeaders, "ADRE entities", ct, progress, (_, _) => { }),
            _ => throw new InvalidDataException("Unknown Arizona ADRE source file.")
        };

    public static List<ArizonaAdreIndividual> ReadIndividuals(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        var records = new List<ArizonaAdreIndividual>();
        ReadRows(path, IndividualHeaders, "ADRE individuals", ct, progress, (row, line) =>
        {
            if (!string.Equals(row["LicCategory"], RealEstateCategory, StringComparison.OrdinalIgnoreCase)) return;
            var number = Number(row["LicNumber"], line);
            var name = string.Join(' ', new[] { row["FirstName"], row["MiddleName"], row["LastName"] }
                .Select(part => part.Trim()).Where(part => part.Length > 0));
            if (name.Length == 0) name = $"Name not published ({number})";
            if (name.Length > 160)
                throw new InvalidDataException($"ADRE individual name exceeds the directory limit at line {line}.");
            var employer = Optional(row["EmployerLicNumber"]);
            if (employer?.Length > 40)
                throw new InvalidDataException($"Overlength ADRE employer license number at line {line}.");
            records.Add(new(line, number, name, Required(row["LicType"], 40, "license type", line),
                Required(row["LicStatus"], 40, "license status", line), Optional(row["EmploymentType"]),
                Date(row["OriginalDate"]), Date(row["ExpireDate"]), employer, Truncate(Optional(row["EmployerLegalName"]), 160),
                Timestamp(row["LastModified"])));
        });
        return records;
    }

    public static List<ArizonaAdreEntity> ReadEntities(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        var records = new List<ArizonaAdreEntity>();
        ReadRows(path, EntityHeaders, "ADRE entities", ct, progress, (row, line) =>
        {
            if (!string.Equals(row["LicCategory"], RealEstateCategory, StringComparison.OrdinalIgnoreCase)) return;
            var number = Number(row["LicNumber"], line);
            var legal = Optional(row["LegalName"]) ?? $"Name not published ({number})";
            if (legal.Length > 200)
                throw new InvalidDataException($"ADRE entity name exceeds the directory limit at line {line}.");
            var cityLine = string.Join(' ', new[] { row["State"], row["Zip"] }.Select(part => part.Trim()).Where(part => part.Length > 0));
            var address = string.Join(", ", new[] { row["Address1"], row["Address2"], row["City"], cityLine }
                .Select(part => part.Trim()).Where(part => part.Length > 0));
            var designated = Optional(row["DesignatedBrokerLicNumber"]);
            if (designated?.Length > 40)
                throw new InvalidDataException($"Overlength ADRE designated broker license number at line {line}.");
            records.Add(new(line, number, legal, Truncate(Optional(row["DBAName"]), 200),
                Required(row["LicType"], 120, "license type", line), Required(row["LicStatus"], 40, "license status", line),
                Date(row["OriginalDate"]), Date(row["ExpireDate"]), Truncate(address.Length == 0 ? null : address, 500),
                designated, Timestamp(row["LastModified"])));
        });
        return records;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Arizona ADRE acquisition manifest.");
        if (entries.Count != Definitions.Count)
            throw new InvalidDataException("Arizona ADRE archive must contain the individuals and entities lists.");
        var ordered = new List<FloridaBulkManifestEntry>();
        foreach (var definition in Definitions)
        {
            var matches = entries.Where(entry => entry.File == definition.RelativePath).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Arizona ADRE archive requires one {definition.Name}.");
            var entry = matches[0];
            var columns = definition.Columns.ToString(CultureInfo.InvariantCulture);
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(columns, out var declared) || declared != entry.Rows)
                throw new InvalidDataException($"Invalid Arizona ADRE acquisition metadata for {definition.Name}.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using (var stream = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Arizona ADRE archive size/hash mismatch for {definition.Name}.");
            }
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, entry.Bytes)) != entry.Rows)
                throw new InvalidDataException($"Arizona ADRE archive row count mismatch for {definition.Name}.");
            progress?.Invoke(definition.Name, entry.Rows, entry.Bytes);
            ordered.Add(entry);
        }
        return ordered;
    }

    /// <summary>
    /// ADRE occasionally lists one license twice (an extra employer row, or a reissued number).
    /// Keep the most recently modified row, preferring an active one, so each credential appears once.
    /// </summary>
    public static List<ArizonaAdreIndividual> SelectLicensees(IEnumerable<ArizonaAdreIndividual> records) =>
        records.GroupBy(record => record.LicenseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.LastModified ?? DateTime.MinValue)
                .ThenByDescending(record => string.Equals(record.LicenseStatus, "Active", StringComparison.OrdinalIgnoreCase))
                .ThenBy(record => record.SourceRowNumber).First())
            .OrderBy(record => record.SourceRowNumber)
            .ToList();

    public static List<ArizonaAdreEntity> SelectFirms(IEnumerable<ArizonaAdreEntity> records) =>
        records.GroupBy(record => record.LicenseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.LastModified ?? DateTime.MinValue)
                .ThenBy(record => record.SourceRowNumber).First())
            .OrderBy(record => record.SourceRowNumber)
            .ToList();

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var individuals = ReadIndividuals(FloridaBulkImportService.ArchiveFile(root, entries[0].File), ct);
        var entities = ReadEntities(FloridaBulkImportService.ArchiveFile(root, entries[1].File), ct);
        var people = SelectLicensees(individuals);
        var firms = SelectFirms(entities);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion
            + string.Concat(entries.Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved)
                throw new InvalidDataException("Arizona ADRE archive conflicts with an existing snapshot.");
            if (await db.RegulatoryLicensees.CountAsync(record => record.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(record => record.SnapshotId == id, ct) != firms.Count)
                throw new InvalidDataException("Existing Arizona ADRE snapshot coverage does not match the validated archive.");
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
            [[id, State, "Arizona Department of Real Estate public database lists (real estate individuals and entities, all statuses)",
                SourcePage, ParserVersion, entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        var personIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var personNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var person in people)
        {
            personIds.Add(person.LicenseNumber, Guid.NewGuid());
            personNames.Add(person.LicenseNumber, person.Name);
        }
        var firmIds = firms.ToDictionary(firm => firm.LicenseNumber, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);

        long total = 0;
        foreach (var group in firms.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(firm => new object?[]
            {
                firmIds[firm.LicenseNumber], id, State, firm.LicenseNumber, firm.LegalName, firm.TradeName, firm.LicenseStatus,
                firm.OriginalDate, firm.ExpirationDate, firm.Address,
                firm.DesignatedBrokerLicenseNumber is { } broker && personNames.TryGetValue(broker, out var brokerName) ? brokerName : null
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "TradeName", "LicenseStatus",
                    "LicenseIssuedDate", "LicenseRenewalDate", "Address", "QualifyingBrokerName"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        foreach (var group in people.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(person => new object?[]
            {
                personIds[person.LicenseNumber], id, State, person.LicenseNumber, person.Name, person.LicenseType,
                null, Truncate(person.EmploymentType is null ? person.LicenseType : person.LicenseType + " - " + person.EmploymentType, 120),
                person.LicenseStatus, person.OriginalDate, person.OriginalDate, person.ExpirationDate,
                person.EmployerLicenseNumber, person.EmployerLicenseNumber is null ? null : "Employing Entity", person.EmployerName,
                false, false, false
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel", "SourceLicenseType",
                    "LicenseStatus", "OriginalLicenseDate", "LicenseIssuedDate", "LicenseRenewalDate",
                    "RelatedLicenseNumber", "RelatedLicenseType", "RelatedLicenseName",
                    "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        // One edge per firm and licensee; the designated broker edge is marked broker-in-charge.
        var edges = new Dictionary<(Guid Firm, Guid Person), bool>();
        foreach (var record in individuals)
        {
            if (record.EmployerLicenseNumber is not { } employer
                || !firmIds.TryGetValue(employer, out var firmId)
                || !personIds.TryGetValue(record.LicenseNumber, out var personId)
                || !string.Equals(personNames[record.LicenseNumber], record.Name, StringComparison.Ordinal))
                continue;
            edges.TryAdd((firmId, personId), false);
        }
        foreach (var firm in firms)
        {
            if (firm.DesignatedBrokerLicenseNumber is { } broker && personIds.TryGetValue(broker, out var personId))
                edges[(firmIds[firm.LicenseNumber], personId)] = true;
        }
        foreach (var group in edges.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(edge => new object?[]
                { Guid.NewGuid(), id, edge.Key.Firm, edge.Key.Person, null, edge.Value }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLicenseeAffiliations",
                ["Id", "SnapshotId", "FirmId", "LicenseeId", "RelationshipEffectiveDate", "IsBrokerInCharge"], rows, ct);
        }

        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        staged?.Invoke(total);
        return id;
    }

    private static long ReadRows(string path, IReadOnlyList<string> headers, string label, CancellationToken ct,
        Action<long>? progress, Action<IReadOnlyDictionary<string, string>, long> handle)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var actual = parser.ReadFields() ?? throw new InvalidDataException($"{label} list has no header.");
        if (!actual.Select(value => value.TrimStart('\uFEFF').Trim()).SequenceEqual(headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} list layout changed; expected the published {headers.Count}-column schema.");

        long rows = 0;
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var line = parser.LineNumber;
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 1 && fields[0].Length == 0) continue;
            if (fields.Length != headers.Count)
                throw new InvalidDataException($"Invalid {label} row shape at line {line}.");
            var row = new Dictionary<string, string>(headers.Count, StringComparer.Ordinal);
            for (var index = 0; index < headers.Count; index++)
                row.Add(headers[index], fields[index]);
            rows++;
            handle(row, line);
            if (rows % 10_000 == 0) progress?.Invoke(rows);
            if (rows > 2_000_000)
                throw new InvalidDataException($"{label} list exceeded the two-million-row safety limit.");
        }
        if (rows == 0)
            throw new InvalidDataException($"{label} list contains no records.");
        progress?.Invoke(rows);
        return rows;
    }

    private static string Number(string value, long line)
    {
        var number = Optional(value);
        if (number is null || number.Length > 40)
            throw new InvalidDataException($"Missing or overlength ADRE license number at line {line}.");
        return number;
    }

    private static string Required(string value, int maxLength, string field, long line)
    {
        var normalized = Optional(value);
        if (normalized is null || normalized.Length > maxLength)
            throw new InvalidDataException($"Missing or overlength ADRE {field} at line {line}.");
        return normalized;
    }

    private static string? Optional(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static readonly string[] TimestampFormats = ["M/d/yyyy h:mm:ss tt", "M/d/yyyy H:mm:ss", "M/d/yyyy"];

    private static DateTime? Timestamp(string value)
    {
        var normalized = Optional(value);
        if (normalized is null) return null;
        return DateTime.TryParseExact(normalized, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new InvalidDataException($"Invalid ADRE date value: {normalized}.");
    }

    private static DateOnly? Date(string value) => Timestamp(value) is { } parsed ? DateOnly.FromDateTime(parsed) : null;
}
