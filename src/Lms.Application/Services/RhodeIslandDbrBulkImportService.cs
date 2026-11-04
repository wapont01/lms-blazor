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

public sealed record RhodeIslandDbrRecord(string InternalId, string LicenseNumber, string Name,
    string LicenseType, string Status, DateOnly? IssueDate, DateOnly? ExpirationDate,
    string? Address, string? QualifierName, string SourceRecordJson)
{
    public bool CanPublish { get; init; } = true;
    public IReadOnlyList<RhodeIslandDbrRelationship> Relationships { get; init; } = [];
}
public sealed record RhodeIslandDbrRelationship(string LicenseNumber, DateOnly? StartDate, DateOnly? EndDate);

public sealed class RhodeIslandDbrBulkImportService(ApplicationDbContext db)
{
    public const string State = "Rhode Island";
    public const string ParserVersion = "rhode-island-dbr-public-v1";
    public const string SourcePage = "https://dbr.ri.gov/about-dbr/lookup-license";
    public const string PortalUrl = "https://ridbrprod-search.state-reg-eastern.tylerapp.com/search";
    public const string TokenUrl = "https://ridbrprod-search.state-reg-eastern.tylerapp.com/api/auth/token";
    public const string SearchUrl = "https://ridbrprod.state-reg-eastern.tylerapp.com/licensing/api/endpoints/v1/portal/search";
    public const string Broker = "Real Estate Broker";
    public const string Salesperson = "Real Estate Salesperson";
    public const string Corporation = "Real Estate Corporation";
    public const string BrokerFile = "real-estate-brokers.json";
    public const string SalespersonFile = "real-estate-salespersons.json";
    public const string CorporationFile = "real-estate-corporations.json";
    public const string BaselinePersonCsv = "Rhode_Island_Full_Captured_Licensees_20261012.csv";
    public static IReadOnlyList<string> BaselinePersonHeaders { get; } =
    [
        "InternalId", "RecordNumber", "LicenseNumber", "CredentialType", "Board", "FullName", "FirmAffiliation",
        "Status", "ExpirationDate", "AddressLine", "City", "State", "ZipCode", "CityStateZipRaw"
    ];
    public const int MaximumRows = 100_000;
    public const long MaximumArchiveBytes = 200L * 1024 * 1024;
    public static IReadOnlySet<string> PublishedStatuses { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "ACTIVE", "INACTIVE", "Inactive - Able to Renew"
    };
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(BrokerFile, BrokerFile, 5) { SourceUrl = SearchUrl },
        new(SalespersonFile, SalespersonFile, 5) { SourceUrl = SearchUrl },
        new(CorporationFile, CorporationFile, 5) { SourceUrl = SearchUrl }
    ];

    public static string LicenseTypeForFile(string name) => name switch
    {
        BrokerFile => Broker,
        SalespersonFile => Salesperson,
        CorporationFile => Corporation,
        _ => throw new InvalidDataException("Unknown Rhode Island roster file.")
    };
    public static bool IsFirm(RhodeIslandDbrRecord row) => row.LicenseType == Corporation;
    public static string NormalizeLicenseType(string type) => type switch
    {
        Broker => "Broker",
        Salesperson => "Salesperson",
        Corporation => "Corporation",
        _ => throw new InvalidDataException("Unknown Rhode Island credential type.")
    };

    public static List<RhodeIslandDbrRecord> ReadRecords(string path, string name,
        CancellationToken ct = default, Action<long>? progress = null)
    {
        var type = LicenseTypeForFile(name);
        if (new FileInfo(path).Length is <= 0 or > MaximumArchiveBytes)
            throw new InvalidDataException("Rhode Island roster is empty or exceeds the archive limit.");
        var records = new List<RhodeIslandDbrRecord>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        void Add(JsonElement source, JsonElement? detail = null)
        {
            ct.ThrowIfCancellationRequested();
            var row = ParseRecord(source, type, detail);
            if (!identities.Add(row.InternalId))
                throw new InvalidDataException("Duplicate Rhode Island source identity.");
            records.Add(row);
            if (records.Count > MaximumRows) throw new InvalidDataException("Rhode Island roster exceeds the row limit.");
            if (records.Count % 1000 == 0) progress?.Invoke(records.Count);
        }
        using var stream = File.OpenRead(path);
        // Baseline CSV must carry the actual source JSON, not reconstructed normalized development fields.
        int first;
        using (var sniff = new StreamReader(stream, new UTF8Encoding(false, true), true, 1024, leaveOpen: true))
        {
            do { first = sniff.Read(); } while (first >= 0 && char.IsWhiteSpace((char)first));
        }
        stream.Position = 0;
        if (first != '{' && first != '[')
        {
            using var csv = new TextFieldParser(stream, new UTF8Encoding(false, true), true)
            {
                TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
            };
            csv.SetDelimiters(",");
            var headers = csv.ReadFields() ?? throw new InvalidDataException("Missing Rhode Island CSV header.");
            var column = Array.IndexOf(headers.Select(value => value.TrimStart('\uFEFF')).ToArray(), "SourceRecordJson");
            if (column < 0 || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
                throw new InvalidDataException("Rhode Island baseline CSV requires an untouched SourceRecordJson column.");
            while (!csv.EndOfData)
            {
                ct.ThrowIfCancellationRequested();
                var fields = csv.ReadFields();
                if (fields is null || fields.Length != headers.Length)
                    throw new InvalidDataException("Malformed Rhode Island baseline CSV row.");
                using var json = ParseJson(fields[column]);
                Add(json.RootElement);
            }
        }
        else
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            using var json = ParseJson(reader.ReadToEnd());
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                foreach (var row in root.EnumerateArray()) Add(row);
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("pages", out var pages))
            {
                if (Text(root, "licenseType") != type || !root.TryGetProperty("pageSize", out var size)
                    || size.ValueKind != JsonValueKind.Number || !size.TryGetInt32(out var pageSize)
                    || pageSize is < 1 or > 1000 || pages.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Invalid Rhode Island page archive schema.");
                var details = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                if (root.TryGetProperty("details", out var detailArray))
                {
                    if (type != Corporation || detailArray.ValueKind != JsonValueKind.Array)
                        throw new InvalidDataException("Unexpected Rhode Island detail archive.");
                    foreach (var detail in detailArray.EnumerateArray())
                        if (!details.TryAdd(Required(Text(detail, "id"), 40, "detail identity"), detail))
                            throw new InvalidDataException("Duplicate Rhode Island detail identity.");
                }
                int? total = null;
                foreach (var page in pages.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("start", out var start)
                        || start.ValueKind != JsonValueKind.Number || !start.TryGetInt32(out var offset)
                        || offset != records.Count || total is { } known && offset >= known
                        || !page.TryGetProperty("response", out var response))
                        throw new InvalidDataException("Rhode Island pages are missing, repeated or out of order.");
                    var count = ValidatePage(response, type, offset, pageSize, total);
                    total ??= count;
                    foreach (var row in response.GetProperty("results").EnumerateArray())
                        Add(row, details.TryGetValue(Text(row, "id"), out var detail) ? detail : null);
                }
                if (total is null || records.Count != total || details.Count != 0 && details.Count != records.Count
                    || details.Keys.Any(key => !identities.Contains(key)))
                    throw new InvalidDataException("Rhode Island archive does not cover the complete published count.");
                if (type == Corporation && details.Count != records.Count)
                    throw new InvalidDataException("Live corporation archive omitted official business details.");
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out _))
            {
                var total = ValidatePage(root, type, 0, MaximumRows, null);
                foreach (var row in root.GetProperty("results").EnumerateArray()) Add(row);
                if (records.Count != total) throw new InvalidDataException("Partial Rhode Island search response.");
            }
            else throw new InvalidDataException("Unknown Rhode Island roster schema.");
        }
        if (records.Count == 0) throw new InvalidDataException("Rhode Island roster contains no credentials.");
        progress?.Invoke(records.Count);
        return records;
    }

    public static long CheckFile(string path, string name, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, name, ct, progress).Count;

    public static int ValidatePage(JsonElement response, string type, int start, int pageSize, int? expectedTotal)
    {
        NormalizeLicenseType(type);
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("resultCount", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var total)
            || total is <= 0 or > MaximumRows || expectedTotal is { } expected && expected != total
            || start < 0 || start >= total
            || !response.TryGetProperty("results", out var rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() != Math.Min(pageSize, total - start)
            || !response.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Incomplete, changed or malformed Rhode Island search page.");
        foreach (var (key, label) in new[] { ("board", "REAL ESTATE"), ("licenseType", type) })
        {
            var matches = filters.EnumerateArray().Where(filter => Text(filter, "key") == key).ToArray();
            if (matches.Length != 1 || !matches[0].TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != 1
                || Text(items[0], "label") != label || !items[0].TryGetProperty("count", out var itemCount)
                || itemCount.ValueKind != JsonValueKind.Number || !itemCount.TryGetInt32(out var value) || value != total)
                throw new InvalidDataException("Rhode Island source filters do not verify the exact board and credential type.");
        }
        foreach (var row in rows.EnumerateArray()) ParseRecord(row, type);
        return total;
    }

    public static RhodeIslandDbrRecord ParseRecord(JsonElement source, string type, JsonElement? detail = null)
    {
        NormalizeLicenseType(type);
        if (source.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid Rhode Island source record.");
        if (source.TryGetProperty("fields", out var csvFields))
            return ParseBaselineCsv(source, csvFields, type);
        if (source.TryGetProperty("baselineDetail", out var baselineDetail))
        {
            if (type != Corporation || !source.TryGetProperty("search", out var rawSearch)
                || baselineDetail.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid Rhode Island baseline corporation evidence.");
            var row = ParseRecord(rawSearch, type);
            if (Text(baselineDetail, "InternalId") != row.InternalId || Text(baselineDetail, "LicenseNumber") != row.LicenseNumber
                || Text(baselineDetail, "CredentialType") != type || Text(baselineDetail, "Board") != "REAL ESTATE"
                || Text(baselineDetail, "Status") != row.Status)
                throw new InvalidDataException("Baseline corporation detail and raw search identities disagree.");
            var rawExpiration = Text(baselineDetail, "ExpirationDate");
            if (!IsMisplacedRecordNumber(row.InternalId, rawExpiration) && Date(rawExpiration) != row.ExpirationDate)
                throw new InvalidDataException("Baseline corporation detail and raw search dates disagree.");
            return row with
            {
                Name = Optional(Text(baselineDetail, "BusinessName").Trim(), 200) ?? "",
                QualifierName = Optional(Text(baselineDetail, "BrokerQualifierName").Trim(), 160),
                IssueDate = Date(Text(baselineDetail, "InitialCredentialDate")),
                Address = Optional(string.Join(", ", new[] { "AddressLine1", "AddressLine2", "CityStateZipRaw", "CountyCountryRaw" }
                    .Select(field => Text(baselineDetail, field)).Where(value => value.Length > 0)).Trim(), 500),
                SourceRecordJson = source.GetRawText()
            };
        }
        if (source.TryGetProperty("search", out var search))
        {
            if (!source.TryGetProperty("detail", out var embedded)) throw new InvalidDataException("Missing embedded Rhode Island detail.");
            return ParseRecord(search, type, embedded);
        }
        if (!source.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Rhode Island record omitted credential details.");
        var credentials = details.EnumerateArray().Where(row => Text(row, "label") == "Credential Details").ToArray();
        if (credentials.Length != 1 || !credentials[0].TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is not (4 or 5)
            || values.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
            throw new InvalidDataException("Rhode Island credential details layout changed.");
        var fields = values.EnumerateArray().Select(value => value.GetString()!).ToArray();
        var internalId = Required(Text(source, "id"), 40, "source identity");
        var prefix = type switch { Broker => "REB.", Salesperson => "RES.", _ => "REC." };
        var number = Required(fields[1], 40, "credential number");
        if (fields[0] != type || !ValidNumber(number, type, prefix)
            || fields[^1].Length != 8 || fields[^1].Any(c => c is < '0' or > '9') || fields[^1] == "00000000"
            || internalId != "C" + fields[^1].TrimStart('0'))
            throw new InvalidDataException("Rhode Island record identity or case-sensitive credential type changed.");
        var name = type == Corporation ? "" : Optional(Text(source, "title").Trim(), 160) ?? "";
        var status = Required(fields[2], 40, "published status");
        if (!PublishedStatuses.Contains(status))
            throw new InvalidDataException("Unknown Rhode Island published status; no status can be inferred.");
        var expiration = fields.Length == 5 ? Date(fields[3]) : null;
        DateOnly? issue = null;
        string? address = null, qualifier = null;
        var relationships = new List<RhodeIslandDbrRelationship>();
        if (detail is { } full)
        {
            if (Text(full, "id") != internalId || !full.TryGetProperty("tiles", out var tiles) || tiles.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Rhode Island detail identity or layout changed.");
            var data = new List<JsonElement>();
            foreach (var tile in tiles.EnumerateArray())
            {
                if (!tile.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Invalid Rhode Island detail steps.");
                foreach (var step in steps.EnumerateArray())
                {
                    if (!step.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
                        throw new InvalidDataException("Invalid Rhode Island detail contents.");
                    foreach (var content in contents.EnumerateArray())
                    {
                        if (!content.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                            throw new InvalidDataException("Invalid Rhode Island detail fields.");
                        data.AddRange(rows.EnumerateArray());
                    }
                }
            }
            string[] Field(string label, bool optional = false)
            {
                var found = data.Where(row => Text(row, "label") == label).ToArray();
                if (optional && found.Length == 0) return [];
                if (found.Length != 1 || !found[0].TryGetProperty("value", out var value)
                    || value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                    throw new InvalidDataException("Missing or ambiguous Rhode Island detail field: " + label + ".");
                return found[0].GetProperty("value").EnumerateArray().Select(item => item.GetString()!).ToArray();
            }
            var credential = Field("Credential");
            var fullStatus = Field("Status");
            var fullExpiration = Field("Expiration Date", optional: true);
            var initial = Field("Initial Credential Date", optional: true);
            if (!credential.SequenceEqual(new[] { number, type, "REAL ESTATE" }) || fullStatus.Length != 1
                || fullStatus[0] != status || fullExpiration.Length > 1
                || (fullExpiration.Length == 0 ? null : Date(fullExpiration[0])) != expiration || initial.Length > 1)
                throw new InvalidDataException("Rhode Island search and detail credentials disagree.");
            issue = initial.Length == 0 ? null : Date(initial[0]);
            if (type == Corporation)
            {
                // DBR publishes zero, one, or several historical Business Address entries with no current marker.
                // Only a single entry identifies the firm; missing or multiple entries stay source-only (no name guess).
                var businessRows = data.Where(row => Text(row, "label") == "Business Address").ToArray();
                foreach (var row in businessRows)
                    if (!row.TryGetProperty("value", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() != 5
                        || lines.EnumerateArray().Any(item => item.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)))
                        throw new InvalidDataException("Rhode Island business address layout changed.");
                if (businessRows.Length == 1)
                {
                    var business = businessRows[0].GetProperty("value").EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : "").ToArray();
                    name = Optional(business[0], 200) ?? "";
                    address = Optional(string.Join(", ", business.Skip(1).Where(value => value.Length > 0)), 500);
                }
                // A search title is not a published licensed qualifier identity or an exact affiliation.
                if (data.Any(row => Text(row, "label") == "Broker Qualifier"))
                {
                    var names = Field("Broker Qualifier");
                    if (names.Length != 1) throw new InvalidDataException("Invalid Rhode Island qualifier field.");
                    qualifier = Optional(names[0], 160);
                }
                foreach (var relation in data.Where(row => Text(row, "relationshipName") == "Supervision of RES"))
                {
                    const string referencePrefix = "Real Estate Salesperson - ";
                    var reference = Text(relation, "credentialReference");
                    if (!reference.StartsWith(referencePrefix, StringComparison.Ordinal)) continue;
                    var license = Required(reference[referencePrefix.Length..], 40, "published supervised credential");
                    if (!ValidNumber(license, Salesperson, "RES."))
                        throw new InvalidDataException("Invalid Rhode Island published supervision credential.");
                    string RelationDate(string field)
                    {
                        if (!relation.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return "";
                        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid Rhode Island relationship date schema.");
                        return value.GetString()!;
                    }
                    relationships.Add(new(license, Date(RelationDate("startDate")), Date(RelationDate("endDate"))));
                }
            }
        }
        var raw = detail is { } detailJson
            ? "{\"search\":" + source.GetRawText() + ",\"detail\":" + detailJson.GetRawText() + "}"
            : source.GetRawText();
        return new(internalId, number, name, type, status, issue, expiration, address, qualifier, raw) { Relationships = relationships };
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Rhode Island acquisition manifest.");
        if (entries.Count != Definitions.Count || entries.Select(entry => entry.File).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidDataException("Rhode Island archive requires all three exact credential types.");
        foreach (var definition in Definitions)
        {
            var entry = entries.SingleOrDefault(entry => entry.File == definition.RelativePath)
                ?? throw new InvalidDataException("Rhode Island archive omitted a credential type.");
            if (entry.SourceUrl != definition.Url || entry.Rows is <= 0 or > MaximumRows
                || entry.Bytes is <= 0 or > MaximumArchiveBytes || entry.VerifiedAtUtc == default
                || string.IsNullOrWhiteSpace(entry.Acquisition) || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue("5", out var count) || count != entry.Rows
                || entry.SourceRowCount is { } published && published != entry.Rows)
                throw new InvalidDataException("Invalid Rhode Island acquisition manifest.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using var file = File.OpenRead(path);
            if (file.Length != entry.Bytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
                entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Rhode Island roster integrity verification failed.");
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, file.Length)) != entry.Rows)
                throw new InvalidDataException("Rhode Island roster count differs from its manifest.");
        }
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var source = Definitions.SelectMany(file => ReadRecords(FloridaBulkImportService.ArchiveFile(root, file.RelativePath), file.Name, ct)).ToList();
        if (source.Select(row => row.InternalId).Distinct(StringComparer.Ordinal).Count() != source.Count)
            throw new InvalidDataException("Rhode Island archive repeats identities across credential types.");
        // Conflicting historical identities remain source evidence; never pick an active/latest/name-matched winner.
        var people = PublishableRecords(source.Where(row => !IsFirm(row)));
        var firms = PublishableRecords(source.Where(IsFirm));
        var firmIds = firms.ToDictionary(row => row.LicenseNumber, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var personIds = people.ToDictionary(row => row.LicenseNumber, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var affiliations = firms.SelectMany(firm => firm.Relationships.Select(relation => (Firm: firm.LicenseNumber, Relation: relation)))
            .Where(row => row.Relation.EndDate is null && personIds.ContainsKey(row.Relation.LicenseNumber))
            .GroupBy(row => (row.Firm, row.Relation.LicenseNumber)).Select(group =>
            {
                var rows = group.Distinct().ToArray();
                return rows.Length == 1 ? rows[0] : ((string Firm, RhodeIslandDbrRelationship Relation)?)null;
            }).Where(row => row.HasValue).Select(row => row!.Value).ToList();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion
            + string.Join("", entries.OrderBy(entry => entry.File, StringComparer.Ordinal).Select(entry => entry.Sha256.ToUpperInvariant()))));
        var id = new Guid(digest.AsSpan(0, 16));
        await RegulatoryLicenseeNameIndex.EnsureAsync(db, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved
                || await db.RegulatorySourceRecords.CountAsync(row => row.SnapshotId == id, ct) != source.Count
                || await db.RegulatoryLicensees.CountAsync(row => row.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(row => row.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.AnyAsync(row => row.SnapshotId == id, ct)
                || await db.RegulatoryFirmLicenseeAffiliations.CountAsync(row => row.SnapshotId == id, ct) != affiliations.Count)
                throw new InvalidDataException("Existing Rhode Island snapshot conflicts with validated evidence.");
            var current = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == State)).Select(row => row.Id).FirstAsync(ct);
            if (current != id) { existing.ActivatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
            return id;
        }
        var connection = db.Database.GetDbConnection();
        var sqlTransaction = transaction.GetDbTransaction();
        var now = DateTime.UtcNow;
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [[id, State, "Rhode Island DBR official real estate credential roster (all published statuses)", SourcePage, ParserVersion,
                entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, now, RegulatorySnapshotStatuses.Approved, now, now]], ct);
        long total = 0;
        foreach (var group in source.Select((record, index) => (Record: record, Row: index + 1)).Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatorySourceRecords",
                ["Id", "SnapshotId", "SourceRowNumber", "LicenseNumber", "LicenseType", "LicenseStatus", "SourceRecordJson"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, row.Row, row.Record.LicenseNumber,
                    row.Record.LicenseType, row.Record.Status, row.Record.SourceRecordJson }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in firms.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "LicenseStatus", "LicenseIssuedDate",
                    "LicenseRenewalDate", "Address", "QualifyingBrokerName", "Industry"],
                group.Select(row => new object?[] { firmIds[row.LicenseNumber], id, State, row.LicenseNumber, row.Name,
                    row.Status, row.IssueDate, row.ExpirationDate, row.Address, row.QualifierName, RegulatoryIndustries.RealEstate }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in people.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "SourceLicenseType", "LicenseStatus",
                    "LicenseIssuedDate", "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"],
                group.Select(row => new object?[] { personIds[row.LicenseNumber], id, State, row.LicenseNumber, row.Name,
                    NormalizeLicenseType(row.LicenseType), row.LicenseType, row.Status, row.IssueDate, row.ExpirationDate, false, false, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in affiliations.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLicenseeAffiliations",
                ["Id", "SnapshotId", "FirmId", "LicenseeId", "RelationshipEffectiveDate", "IsBrokerInCharge"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, firmIds[row.Firm],
                    personIds[row.Relation.LicenseNumber], row.Relation.StartDate, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;
    }

    private static JsonDocument ParseJson(string value)
    {
        try { return JsonDocument.Parse(value); }
        catch (JsonException ex) { throw new InvalidDataException("Malformed Rhode Island source JSON.", ex); }
    }
    private static List<RhodeIslandDbrRecord> PublishableRecords(IEnumerable<RhodeIslandDbrRecord> source) =>
        source.GroupBy(row => row.LicenseNumber, StringComparer.Ordinal).Select(group =>
        {
            var rows = group.ToArray();
            return rows.All(row => row.CanPublish && row.Name.Length > 0) && rows.Select(row =>
                (row.Name, row.LicenseType, row.Status, row.IssueDate, row.ExpirationDate, row.Address, row.QualifierName))
                .Distinct().Count() == 1 ? rows[0] : null;
        }).OfType<RhodeIslandDbrRecord>().ToList();
    private static RhodeIslandDbrRecord ParseBaselineCsv(JsonElement source, JsonElement fields, string type)
    {
        if (type == Corporation || Text(source, "source_file") != BaselinePersonCsv
            || fields.ValueKind != JsonValueKind.Object
            || !fields.EnumerateObject().Select(field => field.Name).SequenceEqual(BaselinePersonHeaders)
            || fields.EnumerateObject().Any(field => field.Value.ValueKind != JsonValueKind.String)
            || !source.TryGetProperty("source_row", out var sourceRow) || sourceRow.ValueKind != JsonValueKind.Number
            || !sourceRow.TryGetInt32(out var rowNumber) || rowNumber is < 1 or > MaximumRows)
            throw new InvalidDataException("Unknown Rhode Island baseline CSV provenance or field schema.");
        var id = Required(Text(fields, "InternalId"), 40, "baseline source identity");
        if (id.Length is < 2 or > 9 || id[0] != 'C' || id[1..].Any(c => c is < '0' or > '9') || id[1] == '0')
            throw new InvalidDataException("Invalid Rhode Island baseline source identity.");
        var number = Required(Text(fields, "LicenseNumber"), 40, "baseline credential number");
        if (Text(fields, "CredentialType") != type || Text(fields, "Board") != "REAL ESTATE"
            || !ValidNumber(number, type, type == Broker ? "REB." : "RES."))
            throw new InvalidDataException("Invalid Rhode Island baseline board, credential type or number.");
        var status = Required(Text(fields, "Status"), 40, "baseline status");
        if (!PublishedStatuses.Contains(status)) throw new InvalidDataException("Unknown Rhode Island baseline status.");
        var expirationText = Text(fields, "ExpirationDate");
        var recordNumber = Text(fields, "RecordNumber");
        var misplaced = recordNumber.Length == 0 && IsMisplacedRecordNumber(id, expirationText);
        if (!misplaced && (recordNumber.Length != 8 || recordNumber.Any(c => c is < '0' or > '9')
            || id != "C" + recordNumber.TrimStart('0')))
            throw new InvalidDataException("Invalid Rhode Island baseline record number.");
        // The old exporter put the record number in the date column on 190 rows. Keep them as evidence, not guessed dates.
        return new(id, number, Optional(Text(fields, "FullName").Trim(), 160) ?? "", type, status, null,
            misplaced ? null : Date(expirationText), null, null, source.GetRawText()) { CanPublish = !misplaced };
    }
    private static bool IsMisplacedRecordNumber(string id, string value) =>
        value.Length == 8 && value.All(c => c is >= '0' and <= '9') && id == "C" + value.TrimStart('0');
    private static bool ValidNumber(string number, string type, string prefix)
    {
        if (!number.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var parts = number.Split('.');
        var standard = parts.Length >= 2 && parts[1].Length == 7
            && parts.Skip(1).All(part => part.Length > 0 && part.All(c => c is >= '0' and <= '9'));
        var legacy = type == Broker && parts.Length == 2 && parts[1].Length == 7
            && (parts[1][..5].All(c => c is >= '0' and <= '9') && parts[1][5] == '-' && parts[1][6] is >= '0' and <= '9'
                || parts[1][..6].All(c => c is >= '0' and <= '9') && parts[1][6] == 'X');
        return standard || legacy;
    }
    private static string Text(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()! : "";
    private static string Required(string value, int maximum, string field) =>
        value.Length is > 0 && value.Length <= maximum && value == value.Trim()
            ? value : throw new InvalidDataException("Invalid Rhode Island " + field + ".");
    private static string? Optional(string value, int maximum) => value.Length == 0 ? null : Required(value, maximum, "published field");
    private static DateOnly? Date(string value) => value.Length == 0 ? null
        : DateOnly.TryParseExact(value, ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new InvalidDataException("Invalid Rhode Island published date.");
}
