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

public sealed record ConnecticutDcpCredential(
    long SourceRowNumber,
    string LicenseNumber,
    string Name,
    string HolderType,
    string Credential,
    string LicenseStatus,
    string? Dba,
    DateOnly? IssueDate,
    DateOnly? EffectiveDate,
    DateOnly? ExpirationDate,
    string? Address,
    DateOnly? RefreshedOn)
{
    public bool IsCompany => ConnecticutDcpBulkImportService.IsCompanyType(HolderType);
}

public sealed record ConnecticutDcpAssociation(
    long SourceRowNumber,
    string SalespersonName,
    string SalespersonLicenseNumber,
    string BrokerName,
    string BrokerLicenseNumber);

public sealed record ConnecticutDcpFirm(
    string LicenseNumber,
    string LegalName,
    string? TradeName,
    string? LicenseStatus,
    DateOnly? IssueDate,
    DateOnly? ExpirationDate,
    string? Address,
    string? QualifyingBrokerName,
    string? QualifyingBrokerLicenseNumber);

public sealed class ConnecticutDcpBulkImportService(ApplicationDbContext db)
{
    public const string State = "Connecticut";
    public const string ParserVersion = "connecticut-dcp-socrata-v1";
    public const string SourcePage = "https://data.ct.gov/Business/Real-Estate-Salesperson-Licenses/7y8e-cawe";
    public const string SalespersonsFile = "real-estate-salesperson-licenses.csv";
    public const string BrokersFile = "real-estate-broker-licenses.csv";
    public const string AssociationsFile = "real-estate-salespersons-with-associated-broker.csv";
    public const string SalespersonsUrl = "https://data.ct.gov/resource/7y8e-cawe.csv";
    public const string BrokersUrl = "https://data.ct.gov/resource/fwpc-pgqj.csv";
    public const string AssociationsUrl = "https://data.ct.gov/resource/6tja-6vdt.csv";
    public const string SalespersonsRowCountUrl = "https://data.ct.gov/resource/7y8e-cawe.json?$select=count(*)";
    public const string BrokersRowCountUrl = "https://data.ct.gov/resource/fwpc-pgqj.json?$select=count(*)";
    public const string AssociationsRowCountUrl = "https://data.ct.gov/resource/6tja-6vdt.json?$select=count(*)";
    public const string TeamLicenseType = "Real Estate Team";

    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(SalespersonsFile, SalespersonsFile, 20) { SourceUrl = SalespersonsUrl },
        new(BrokersFile, BrokersFile, 20) { SourceUrl = BrokersUrl },
        new(AssociationsFile, AssociationsFile, 4) { SourceUrl = AssociationsUrl }
    ];

    public static IReadOnlyList<string> CredentialHeaders { get; } = Array.AsReadOnly(new[]
    {
        "credentialid", "name", "type", "businessname", "dba", "fullcredentialcode", "credentialtype", "credentialnumber",
        "credentialsubcategory", "credential", "status", "statusreason", "active", "issuedate", "effectivedate",
        "expirationdate", "city", "state", "zip", "recordrefreshedon"
    });

    public static IReadOnlyList<string> AssociationHeaders { get; } = Array.AsReadOnly(new[]
    {
        "real_estate_salesperson", "salespersoncredential", "real_estate_broker", "broker_credential"
    });

    // Stable total ordering for paged Socrata exports.
    public const string CredentialOrderBy = "credentialid,fullcredentialcode";
    public const string AssociationOrderBy = "salespersoncredential,broker_credential,real_estate_salesperson,real_estate_broker";

    public static bool IsCompanyType(string holderType)
    {
        var type = holderType.Trim().ToUpperInvariant();
        return type is "BUSINESS" or "LLC" or "LLP" || type.Contains("COMPANY", StringComparison.Ordinal)
            || type.Contains("CORPORATION", StringComparison.Ordinal) || type.Contains("PARTNERSHIP", StringComparison.Ordinal)
            || type.Contains("LIMITED", StringComparison.Ordinal);
    }

    /// <summary>Counts every source row after validating the published layout.</summary>
    public static long CheckFile(string path, string fileName, CancellationToken ct = default, Action<long>? progress = null) =>
        fileName switch
        {
            SalespersonsFile => ReadRows(path, CredentialHeaders, "CT salesperson", ct, progress, (_, _) => { }),
            BrokersFile => ReadRows(path, CredentialHeaders, "CT broker", ct, progress, (_, _) => { }),
            AssociationsFile => ReadRows(path, AssociationHeaders, "CT salesperson-broker", ct, progress, (_, _) => { }),
            _ => throw new InvalidDataException("Unknown Connecticut DCP source file.")
        };

    /// <summary>Reads credentials that carry a license number; numberless application rows stay in the source file only.</summary>
    public static List<ConnecticutDcpCredential> ReadCredentials(string path, string expectedPrefix, CancellationToken ct = default,
        Action<long>? progress = null)
    {
        var records = new List<ConnecticutDcpCredential>();
        ReadRows(path, CredentialHeaders, $"CT {expectedPrefix}", ct, progress, (row, line) =>
        {
            var number = Optional(row["fullcredentialcode"]);
            if (number is null || !number.Contains('.', StringComparison.Ordinal) || Optional(row["credentialnumber"]) is null)
                return;
            if (number.Length > 40 || !number.StartsWith(expectedPrefix + ".", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected Connecticut credential code '{number}' at line {line}.");
            var name = Optional(row["name"]) ?? $"Name not published ({number})";
            if (name.Length > 160)
                throw new InvalidDataException($"Connecticut licensee name exceeds the directory limit at line {line}.");
            var zip = Optional(row["zip"]);
            if (zip is { Length: 9 } && zip.All(char.IsAsciiDigit)) zip = zip[..5] + "-" + zip[5..];
            var region = string.Join(' ', new[] { Optional(row["state"]), zip }.Where(part => part is not null));
            var address = string.Join(", ", new[] { Optional(row["city"]), region.Length == 0 ? null : region }
                .Where(part => part is not null));
            records.Add(new(line, number.ToUpperInvariant(), name, Optional(row["type"]) ?? "NOT REPORTED",
                Required(row["credential"], 120, "credential", line), Required(row["status"], 40, "status", line),
                Truncate(Optional(row["dba"]), 200), Date(row["issuedate"]), Date(row["effectivedate"]),
                Date(row["expirationdate"]), address.Length == 0 ? null : address, Date(row["recordrefreshedon"])));
        });
        return records;
    }

    public static List<ConnecticutDcpAssociation> ReadAssociations(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        var records = new List<ConnecticutDcpAssociation>();
        ReadRows(path, AssociationHeaders, "CT salesperson-broker", ct, progress, (row, line) =>
        {
            var salesperson = Optional(row["salespersoncredential"]);
            var broker = Optional(row["broker_credential"]);
            if (salesperson is null || broker is null) return;
            if (salesperson.Length > 40 || broker.Length > 40)
                throw new InvalidDataException($"Overlength Connecticut association credential at line {line}.");
            records.Add(new(line, Truncate(Optional(row["real_estate_salesperson"]), 160) ?? salesperson,
                salesperson.ToUpperInvariant(), Truncate(Optional(row["real_estate_broker"]), 200) ?? broker, broker.ToUpperInvariant()));
        });
        return records;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Connecticut DCP acquisition manifest.");
        if (entries.Count != Definitions.Count)
            throw new InvalidDataException("Connecticut DCP archive must contain the salesperson, broker and association datasets.");
        var ordered = new List<FloridaBulkManifestEntry>();
        foreach (var definition in Definitions)
        {
            var matches = entries.Where(entry => entry.File == definition.RelativePath).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Connecticut DCP archive requires one {definition.Name}.");
            var entry = matches[0];
            var columns = definition.Columns.ToString(CultureInfo.InvariantCulture);
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(columns, out var declared) || declared != entry.Rows)
                throw new InvalidDataException($"Invalid Connecticut DCP acquisition metadata for {definition.Name}.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using (var stream = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Connecticut DCP archive size/hash mismatch for {definition.Name}.");
            }
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, entry.Bytes)) != entry.Rows)
                throw new InvalidDataException($"Connecticut DCP archive row count mismatch for {definition.Name}.");
            progress?.Invoke(definition.Name, entry.Rows, entry.Bytes);
            ordered.Add(entry);
        }
        return ordered;
    }

    /// <summary>Keeps one row per credential: the most recently refreshed, then the first published.</summary>
    public static List<ConnecticutDcpCredential> Distinct(IEnumerable<ConnecticutDcpCredential> records) =>
        records.GroupBy(record => record.LicenseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.RefreshedOn ?? DateOnly.MinValue)
                .ThenBy(record => record.SourceRowNumber).First())
            .OrderBy(record => record.SourceRowNumber)
            .ToList();

    /// <summary>
    /// Firms are broker credentials held by companies, plus any other broker or team credential that salespersons
    /// are associated with (sole-proprietor brokers and real estate teams), so every published association has a firm.
    /// </summary>
    public static List<ConnecticutDcpFirm> SelectFirms(IReadOnlyList<ConnecticutDcpCredential> brokers,
        IReadOnlyList<ConnecticutDcpAssociation> associations)
    {
        var byNumber = brokers.ToDictionary(broker => broker.LicenseNumber, StringComparer.OrdinalIgnoreCase);
        var firms = new Dictionary<string, ConnecticutDcpFirm>(StringComparer.OrdinalIgnoreCase);
        foreach (var broker in brokers.Where(broker => broker.IsCompany))
            firms.Add(broker.LicenseNumber, new(broker.LicenseNumber, broker.Name, broker.Dba, Title(broker.LicenseStatus),
                broker.IssueDate, broker.ExpirationDate, broker.Address, null, null));
        foreach (var association in associations)
        {
            if (firms.ContainsKey(association.BrokerLicenseNumber)) continue;
            firms.Add(association.BrokerLicenseNumber, byNumber.TryGetValue(association.BrokerLicenseNumber, out var broker)
                ? new(broker.LicenseNumber, broker.Name, broker.Dba, Title(broker.LicenseStatus), broker.IssueDate,
                    broker.ExpirationDate, broker.Address, broker.Name, broker.LicenseNumber)
                : new(association.BrokerLicenseNumber, association.BrokerName, null, null, null, null, null, null, null));
        }
        return firms.Values.ToList();
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var salespersons = Distinct(ReadCredentials(FloridaBulkImportService.ArchiveFile(root, entries[0].File), "RES", ct));
        var brokers = Distinct(ReadCredentials(FloridaBulkImportService.ArchiveFile(root, entries[1].File), "REB", ct));
        var associations = ReadAssociations(FloridaBulkImportService.ArchiveFile(root, entries[2].File), ct);
        var firms = SelectFirms(brokers, associations);
        var people = salespersons.Concat(brokers.Where(broker => !broker.IsCompany)).ToList();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion
            + string.Concat(entries.Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved)
                throw new InvalidDataException("Connecticut DCP archive conflicts with an existing snapshot.");
            if (await db.RegulatoryLicensees.CountAsync(record => record.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(record => record.SnapshotId == id, ct) != firms.Count)
                throw new InvalidDataException("Existing Connecticut DCP snapshot coverage does not match the validated archive.");
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
            [[id, State, "Connecticut DCP open data: real estate salesperson and broker licenses with associated brokers (all statuses)",
                SourcePage, ParserVersion, entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        var personIds = people.ToDictionary(person => person.LicenseNumber, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        var firmIds = firms.ToDictionary(firm => firm.LicenseNumber, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        var employers = new Dictionary<string, ConnecticutDcpAssociation>(StringComparer.OrdinalIgnoreCase);
        foreach (var association in associations)
            employers.TryAdd(association.SalespersonLicenseNumber, association);

        long total = 0;
        foreach (var group in firms.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(firm => new object?[]
            {
                firmIds[firm.LicenseNumber], id, State, firm.LicenseNumber, firm.LegalName, firm.TradeName, firm.LicenseStatus,
                firm.IssueDate, firm.ExpirationDate, firm.Address, firm.QualifyingBrokerName
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
            var rows = group.Select(person =>
            {
                employers.TryGetValue(person.LicenseNumber, out var employer);
                var licenseType = Title(person.Credential);
                return new object?[]
                {
                    personIds[person.LicenseNumber], id, State, person.LicenseNumber, person.Name, Truncate(licenseType, 40),
                    null, Truncate(licenseType + " - " + Title(person.HolderType), 120), Title(person.LicenseStatus),
                    person.IssueDate, person.EffectiveDate ?? person.IssueDate, person.ExpirationDate,
                    employer?.BrokerLicenseNumber, employer is null ? null : BrokerType(employer.BrokerLicenseNumber),
                    Truncate(employer?.BrokerName, 160), false, false, false
                };
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel", "SourceLicenseType",
                    "LicenseStatus", "OriginalLicenseDate", "LicenseIssuedDate", "LicenseRenewalDate",
                    "RelatedLicenseNumber", "RelatedLicenseType", "RelatedLicenseName",
                    "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        // One edge per firm and licensee; a sole-proprietor broker is broker-in-charge of their own office.
        var edges = new Dictionary<(Guid Firm, Guid Person), bool>();
        foreach (var association in associations)
        {
            if (firmIds.TryGetValue(association.BrokerLicenseNumber, out var firmId)
                && personIds.TryGetValue(association.SalespersonLicenseNumber, out var personId))
                edges.TryAdd((firmId, personId), false);
        }
        foreach (var firm in firms)
        {
            if (firm.QualifyingBrokerLicenseNumber is { } broker && personIds.TryGetValue(broker, out var personId))
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

    private static string BrokerType(string licenseNumber) =>
        licenseNumber.StartsWith("REB.", StringComparison.OrdinalIgnoreCase) ? "Real Estate Broker" : TeamLicenseType;

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
        var actual = parser.ReadFields() ?? throw new InvalidDataException($"{label} dataset has no header.");
        if (!actual.Select(value => value.TrimStart('\uFEFF').Trim()).SequenceEqual(headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} dataset layout changed; expected the published {headers.Count}-column schema.");

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
            if (rows > 1_000_000)
                throw new InvalidDataException($"{label} dataset exceeded the one-million-row safety limit.");
        }
        if (rows == 0)
            throw new InvalidDataException($"{label} dataset contains no records.");
        progress?.Invoke(rows);
        return rows;
    }

    // "ACTIVE IN RENEWAL" -> "Active In Renewal".
    private static string Title(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());

    private static string Required(string value, int maxLength, string field, long line)
    {
        var normalized = Optional(value);
        if (normalized is null || normalized.Length > maxLength)
            throw new InvalidDataException($"Missing or overlength Connecticut {field} at line {line}.");
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
        return DateTime.TryParseExact(normalized, ["yyyy-MM-dd'T'HH:mm:ss.fff", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd", "MM/dd/yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateOnly.FromDateTime(date)
            : throw new InvalidDataException($"Invalid Connecticut date value: {normalized}.");
    }
}
