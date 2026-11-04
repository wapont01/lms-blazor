using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lms.Application.Services;

public sealed record WyomingWrecRecord(string File, int Row, int? Page, DateTimeOffset? CapturedAtUtc,
    JsonElement? Query, JsonElement Raw, long ProviderId, long IndividualId, int TypeId,
    string LicenseNumber, string Name, string SourceLicenseType, string LicenseType, string Status,
    DateOnly? OriginalLicenseDate, DateOnly? IssueDate, DateOnly? ExpirationDate, string? Address)
{
    public string Identity => $"{ProviderId}:{IndividualId}:{TypeId}:{LicenseNumber}";
}

public sealed class WyomingWrecBulkImportService(ApplicationDbContext db)
{
    public const string State = "Wyoming";
    public const string ParserVersion = "wyoming-wrec-json-v1";
    public const string SourcePage = "https://online.wyrec.org/#/verifylicense";
    public const string CommissionPage = "https://realestate.wyo.gov/";
    public const string ApiUrl = "https://ws.wyrec.org/api/Provider/IndividualProviderVerifySearchWithPage";
    public const string IndividualFile = "individuals.json";
    public const string FirmFile = "firms.json";
    public const string AmcFile = "amc.json";
    public const string ArchiveFormat = "wyoming-wrec-paged-v1";
    public const int RecordColumns = 67;
    public const int MaxRecords = 10_000;
    public const int DefaultPageSize = 250;
    public const int MaxPageSize = 250;
    public const int MaxPages = 100;
    public const long MaxResponseBytes = 16L * 1024 * 1024;
    public const long MaxFileBytes = 256L * 1024 * 1024;
    public static IReadOnlyList<string> AppraiserLicenseTypes { get; } = Array.AsReadOnly(new[]
    {
        "Certified Residential Appraiser", "Certified General Appraiser", "Certified Appraiser Trainee"
    });
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(IndividualFile, IndividualFile, RecordColumns) { SourceUrl = ApiUrl },
        new(FirmFile, FirmFile, RecordColumns) { SourceUrl = ApiUrl },
        new(AmcFile, AmcFile, RecordColumns) { SourceUrl = ApiUrl }
    ];
    public static IReadOnlyList<string> Headers { get; } = Array.AsReadOnly(new[]
    {
        "IsIndividualSearch", "IsFirmSearch", "IsFuzzySearch", "RemoveRelationshipsForLicenseTypes",
        "FirstName", "LastName", "MiddleName", "LicenseNumber", "State", "Zip", "ProfessionalTitle",
        "CorporationName", "City", "StreetAddress", "ProviderTypeId", "ProviderLicenseStatusId",
        "Address1", "Address2", "Contact", "ContactEmail", "ProviderId", "IndividualId", "ExternalId",
        "ExternalId2", "IsActive", "StatusColorCode", "IndividualLicenseId", "LicenseStatusTypeId",
        "LicenseStatusTypeName", "LicenseTypeId", "LicenseTypeName", "LicenseKindTypeId", "LicenseKindTypeName",
        "BranchTypeName", "LicenseExpirationDate", "LicenseEffectiveDate", "OriginalLicenseDate", "RevokedDate",
        "CountryId", "CountryName", "CountyName", "TotalRecord", "LicenseDisciplineTypeCode",
        "LicenseDisciplineTypeId", "Source", "Discipline", "SortType", "SortOrder", "MiscDate1", "MiscDate2",
        "MiscInfo1", "MiscInfo2", "CompanyStreetLine1", "CompanyStreetLine2", "CompanyCity", "CompanyState",
        "CompanyZip", "CompanyCountryName", "CompanyAddresse", "TaxId", "SuffixTypeCode", "Phone",
        "ComplaintDisciplineType", "Violation", "ProviderName", "ClinicName", "EntityRelationList"
    });
    private static readonly IReadOnlyDictionary<int, (string Source, string Normalized)> PersonTypes =
        new Dictionary<int, (string, string)>
        {
            [1] = ("Real Estate Salesperson License", "Salesperson"),
            [2] = ("Associate Broker License", "Associate Broker"),
            [3] = ("Responsible Broker License", "Responsible Broker"),
            [4] = ("Certified Residential Appraiser Permit", "Certified Residential Appraiser"),
            [5] = ("Certified General Appraiser Permit", "Certified General Appraiser"),
            [6] = ("Certified Appraiser Trainee Permit", "Certified Appraiser Trainee")
        };

    public static JsonElement SearchPayload(string file)
    {
        RequireFile(file);
        return JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["LastName"] = "", ["FirstName"] = "", ["LicenseNumber"] = "",
            ["IsIndividualSearch"] = file == IndividualFile, ["IsFirmSearch"] = file != IndividualFile,
            ["State"] = "", ["Zip"] = "", ["LicenseTypeId"] = "", ["CorporationName"] = "",
            ["City"] = "", ["StreetAddress"] = "", ["ProviderTypeId"] = file == FirmFile ? "2" : file == AmcFile ? "1" : "",
            ["SortOrder"] = "asc", ["SortType"] = "LicenseNumber", ["RemoveRelationshipsForLicenseTypes"] = Array.Empty<string>()
        });
    }

    public static string PageUrl(int page, int pageSize)
    {
        if (page is < 1 or > MaxPages || pageSize is < 1 or > MaxPageSize)
            throw new InvalidDataException("Wyoming paging exceeds its bounded limits.");
        return $"{ApiUrl}?PageNumber={page}&NoOfRecords={pageSize}&ShowAllRecords=false&Source=WYREALESTATE";
    }

    // Sorting object properties permits formatting-only changes, but never discards published fields or array order.
    public static string CanonicalJson(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(value, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
        static void Write(JsonElement element, Utf8JsonWriter writer)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var properties = element.EnumerateObject().ToList();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Count)
                    throw new InvalidDataException("Duplicate Wyoming JSON property.");
                writer.WriteStartObject();
                foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name); Write(property.Value, writer);
                }
                writer.WriteEndObject();
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Write(item, writer);
                writer.WriteEndArray();
            }
            else element.WriteTo(writer);
        }
    }

    public static (int Total, List<WyomingWrecRecord> Rows) ReadPage(string json, string file, int page,
        int pageSize, DateTimeOffset capturedAtUtc, JsonElement query)
    {
        RequireFile(file);
        if (Encoding.UTF8.GetByteCount(json) > MaxResponseBytes)
            throw new InvalidDataException("Wyoming API response exceeds its byte limit.");
        if (capturedAtUtc == default || capturedAtUtc.Offset != TimeSpan.Zero
            || CanonicalJson(query) != CanonicalJson(SearchPayload(file)))
            throw new InvalidDataException("Wyoming API capture timestamp or published query changed.");
        try
        {
            using var document = JsonDocument.Parse(json);
            var envelope = document.RootElement;
            _ = CanonicalJson(envelope);
            if (envelope.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("Status", out var status)
                || status.ValueKind != JsonValueKind.True || Text(envelope, "StatusCode") != "00"
                || !envelope.TryGetProperty("TotalRecord", out var totalValue) || !totalValue.TryGetInt32(out var total)
                || total is < 1 or > MaxRecords || !envelope.TryGetProperty("IndividualProviderVerifySearchList", out var list)
                || list.ValueKind != JsonValueKind.Array || page is < 1 or > MaxPages || pageSize is < 1 or > MaxPageSize
                || (long)(page - 1) * pageSize >= total
                || list.GetArrayLength() != Math.Min(pageSize, total - (page - 1) * pageSize))
                throw new InvalidDataException("Wyoming API status, total, page size or response shape changed.");
            var rows = new List<WyomingWrecRecord>();
            foreach (var item in list.EnumerateArray())
            {
                if (Integer(item, "TotalRecord") != total)
                    throw new InvalidDataException("Wyoming row total disagrees with its envelope.");
                rows.Add(ParseRecord(item, file, (page - 1) * pageSize + rows.Count + 1, page, capturedAtUtc, query));
            }
            return (total, rows);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed Wyoming API response.", ex); }
    }

    public static List<WyomingWrecRecord> ReadRecords(string path, string file, CancellationToken ct = default,
        Action<long>? progress = null)
    {
        RequireFile(file);
        using var input = File.OpenRead(path);
        if (input.Length is <= 0 or > MaxFileBytes) throw new InvalidDataException("Wyoming archive exceeds its byte limit.");
        try
        {
            using var reader = new StreamReader(input, new UTF8Encoding(false, true), true);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            List<WyomingWrecRecord> rows;
            if (root.ValueKind == JsonValueKind.Array)
            {
                // Historical published arrays contain no acquisition envelope. Do not invent one.
                rows = [];
                foreach (var item in root.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    rows.Add(ParseRecord(item, file, rows.Count + 1, null, null, null));
                    if (rows.Count > MaxRecords) throw new InvalidDataException("Wyoming archive exceeds its row limit.");
                }
            }
            else
            {
                _ = CanonicalJson(root);
                if (Text(root, "format") != ArchiveFormat || Text(root, "file") != file
                    || !root.TryGetProperty("page_size", out var sizeValue) || !sizeValue.TryGetInt32(out var size)
                    || size is < 1 or > MaxPageSize || !root.TryGetProperty("query", out var query)
                    || CanonicalJson(query) != CanonicalJson(SearchPayload(file)))
                    throw new InvalidDataException("Wyoming archive query or format changed.");
                rows = ReadPass("pages");
                var verification = ReadPass("verification_pages");
                if (verification[0].CapturedAtUtc!.Value < rows[^1].CapturedAtUtc!.Value)
                    throw new InvalidDataException("Wyoming verification predates the original capture.");
                var expected = Fingerprints(rows);
                var actual = Fingerprints(verification);
                if (expected.Count != actual.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
                    throw new InvalidDataException("Wyoming repeated collection identities or published facts changed.");

                List<WyomingWrecRecord> ReadPass(string key)
                {
                    if (!root.TryGetProperty(key, out var pages) || pages.ValueKind != JsonValueKind.Array
                        || pages.GetArrayLength() is < 1 or > MaxPages)
                        throw new InvalidDataException("Wyoming archive omitted a complete verification pass.");
                    var result = new List<WyomingWrecRecord>();
                    int? published = null;
                    DateTimeOffset? previousTime = null;
                    foreach (var capture in pages.EnumerateArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        var page = result.Count / size + 1;
                        if (Integer(capture, "page_number") != page || Text(capture, "url") != PageUrl(page, size)
                            || !capture.TryGetProperty("captured_at_utc", out var timestamp)
                            || !timestamp.TryGetDateTimeOffset(out var at) || at == default || at.Offset != TimeSpan.Zero
                            || previousTime is { } previous && at < previous)
                            throw new InvalidDataException("Wyoming archive page URL, order or capture timestamp changed.");
                        previousTime = at;
                        var response = ReadPage(Text(capture, "response_json"), file, page, size, at, query);
                        published ??= response.Total;
                        if (published != response.Total) throw new InvalidDataException("Wyoming published count changed during paging.");
                        result.AddRange(response.Rows);
                        if (result.Count > published) throw new InvalidDataException("Wyoming archive has extra pages.");
                    }
                    if (result.Count != published) throw new InvalidDataException("Wyoming collection is incomplete.");
                    return result;
                }
            }
            if (rows.Count == 0) throw new InvalidDataException("Wyoming archive is empty.");
            _ = Fingerprints(rows);
            progress?.Invoke(rows.Count);
            return rows;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException or DecoderFallbackException)
        { throw new InvalidDataException("Malformed Wyoming archive.", ex); }
    }

    public static Dictionary<string, string> Fingerprints(IEnumerable<WyomingWrecRecord> rows)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
            if (!result.TryAdd(row.Identity, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson(row.Raw))))))
                throw new InvalidDataException("Duplicate or conflicting Wyoming published record identity.");
        return result;
    }

    public static long CheckFile(string path, string file, CancellationToken ct = default, Action<long>? progress = null) =>
        ReadRecords(path, file, ct, progress).Count;

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Wyoming acquisition manifest.");
        if (entries.Count != Definitions.Count || entries.Select(entry => entry.File).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidDataException("Wyoming archive must contain all three official queries.");
        foreach (var definition in Definitions)
        {
            var entry = entries.SingleOrDefault(entry => entry.File == definition.RelativePath)
                ?? throw new InvalidDataException("Wyoming archive omitted an official query.");
            if (entry.SourceUrl != definition.Url || entry.Rows is < 1 or > MaxRecords || entry.Bytes is <= 0 or > MaxFileBytes
                || entry.VerifiedAtUtc == default || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(RecordColumns.ToString(CultureInfo.InvariantCulture), out var count) || count != entry.Rows
                || entry.SourceRowCount is { } published && published != entry.Rows)
                throw new InvalidDataException("Invalid Wyoming acquisition manifest.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using var file = File.OpenRead(path);
            if (file.Length != entry.Bytes || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
                entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Wyoming archive SHA256 or byte count differs from its manifest.");
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, file.Length)) != entry.Rows)
                throw new InvalidDataException("Wyoming archive row count differs from its manifest.");
        }
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var source = Definitions.SelectMany(file => ReadRecords(FloridaBulkImportService.ArchiveFile(root, file.RelativePath),
            file.Name, ct)).ToList();
        var people = source.Where(row => row.File == IndividualFile && HasCredential(row)).ToList();
        // Conflicting provider IDs using the same firm credential cannot fit the directory's unique credential key.
        // Keep every row as evidence, but do not choose an owner or generate a replacement license number.
        var firms = source.Where(row => row.File != IndividualFile && HasCredential(row) && Text(row.Raw, "BranchTypeName").Length == 0)
            .GroupBy(row => row.LicenseNumber, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Single()).ToList();
        var personIds = people.ToDictionary(row => row.Identity, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var firmIds = firms.ToDictionary(row => row.Identity, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var affiliations = SelectAffiliations(source, people, firms);
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
                throw new InvalidDataException("Existing Wyoming snapshot conflicts with the validated archive.");
            var current = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == State))
                .Select(row => row.Id).FirstOrDefaultAsync(ct);
            if (current != id) { existing.ActivatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
            return id;
        }
        var connection = db.Database.GetDbConnection();
        var sqlTransaction = transaction.GetDbTransaction();
        var now = DateTime.UtcNow;
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [[id, State, "Wyoming WREC official individuals, real estate firms and AMC searches", SourcePage, ParserVersion,
                entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, now, RegulatorySnapshotStatuses.Approved, now, now]], ct);
        long total = 0;
        foreach (var group in source.Select((row, index) => (Record: row, Number: index + 1)).Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatorySourceRecords",
                ["Id", "SnapshotId", "SourceRowNumber", "LicenseNumber", "LicenseType", "LicenseStatus", "SourceRecordJson"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, row.Number, row.Record.LicenseNumber,
                    row.Record.SourceLicenseType, row.Record.Status, SourceJson(row.Record) }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in firms.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "LicenseStatus", "LicenseIssuedDate", "LicenseRenewalDate",
                    "Address", "QualifyingBrokerName", "Industry"],
                group.Select(row => new object?[] { firmIds[row.Identity], id, State, row.LicenseNumber, row.Name,
                    row.Status, row.IssueDate, row.ExpirationDate, row.Address, QualifyingBroker(row),
                    row.File == AmcFile ? RegulatoryIndustries.Appraisal : RegulatoryIndustries.RealEstate }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in people.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "SourceLicenseType", "LicenseStatus",
                    "OriginalLicenseDate", "LicenseIssuedDate", "LicenseRenewalDate", "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"],
                group.Select(row => new object?[] { personIds[row.Identity], id, State, row.LicenseNumber, row.Name,
                    row.LicenseType, row.SourceLicenseType, row.Status, row.OriginalLicenseDate, row.IssueDate,
                    row.ExpirationDate, false, false, false }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        foreach (var group in affiliations.Chunk(100))
        {
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLicenseeAffiliations",
                ["Id", "SnapshotId", "FirmId", "LicenseeId", "RelationshipEffectiveDate", "IsBrokerInCharge", "ContactEmail"],
                group.Select(row => new object?[] { Guid.NewGuid(), id, firmIds[row.Firm.Identity],
                    personIds[row.Person.Identity], row.StartDate, false, row.Email }).ToList(), ct);
            total += group.Length; staged?.Invoke(total);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return id;

        string? QualifyingBroker(WyomingWrecRecord firm)
        {
            var brokers = affiliations.Where(link => link.Firm.Identity == firm.Identity && link.Responsible
                && link.Person.LicenseType == "Responsible Broker").Select(link => link.Person.Name).Distinct(StringComparer.Ordinal).ToList();
            return brokers.Count == 1 ? brokers[0] : null;
        }
    }

    private sealed record Affiliation(WyomingWrecRecord Firm, WyomingWrecRecord Person, bool Responsible, DateOnly? StartDate, string? Email);
    private static List<Affiliation> SelectAffiliations(List<WyomingWrecRecord> source,
        List<WyomingWrecRecord> people, List<WyomingWrecRecord> firms)
    {
        var providers = firms.GroupBy(row => row.ProviderId).ToDictionary(group => group.Key, group => group.ToList());
        var individuals = people.GroupBy(row => (row.IndividualId, row.LicenseNumber))
            .ToDictionary(group => group.Key, group => group.ToList());
        var evidence = new List<(WyomingWrecRecord Firm, WyomingWrecRecord Person, JsonElement Relation)>();
        foreach (var owner in source)
        foreach (var relation in owner.Raw.GetProperty("EntityRelationList").EnumerateArray())
        {
            var provider = Integer(relation, "ProviderId");
            var individual = Integer(relation, "IndividualId");
            var number = Text(relation, "IndividualLicenseNumber").Trim();
            if (Flag(relation, "IsDeleted") == true || Text(relation, "ReleasedDate").Length > 0
                || Text(relation, "EndDate").Length > 0 || Integer(relation, "EntityRelationStatusId") != 1
                || !providers.TryGetValue(provider, out var matchedFirms) || matchedFirms.Count != 1
                || !individuals.TryGetValue((individual, number), out var matchedPeople))
                continue;
            var candidates = matchedPeople.Where(person =>
                Integer(relation, "IndividualLicenseTypeId") is not > 0 || Integer(relation, "IndividualLicenseTypeId") == person.TypeId).ToList();
            if (candidates.Count != 1) continue;
            var firm = matchedFirms[0];
            var person = candidates[0];
            if ((firm.File == AmcFile) != (person.TypeId is >= 4 and <= 6)) continue;
            if (owner.File == IndividualFile ? owner.Identity != person.Identity : owner.Identity != firm.Identity) continue;
            var publishedNumber = Text(relation, "ProviderLicenseNumber").Trim();
            if (publishedNumber.Length > 0 && publishedNumber != firm.LicenseNumber) continue;
            evidence.Add((firm, person, relation));
        }
        return evidence.GroupBy(row => (row.Firm.Identity, row.Person.Identity)).Select(group =>
        {
            var first = group.First();
            var flags = group.Select(row => Flag(row.Relation, "Responsible")).Where(value => value.HasValue).Distinct().ToList();
            var emails = group.Select(row => Optional(Text(row.Relation, "EmailAddress").Trim(), 254))
                .Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var dates = group.Select(row => Date(Text(row.Relation, "StartDate"))).Where(value => value.HasValue).Distinct().ToList();
            return new Affiliation(first.Firm, first.Person, flags.Count == 1 && flags[0] == true,
                dates.Count == 1 ? dates[0] : null, emails.Count == 1 ? emails[0] : null);
        }).ToList();
    }

    private static WyomingWrecRecord ParseRecord(JsonElement raw, string file, int row, int? page,
        DateTimeOffset? at, JsonElement? query)
    {
        if (raw.ValueKind != JsonValueKind.Object || !raw.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(Headers.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Wyoming published 67-property record schema changed.");
        _ = CanonicalJson(raw);
        var provider = Integer(raw, "ProviderId");
        var individual = Integer(raw, "IndividualId");
        var typeId = checked((int)Integer(raw, "LicenseTypeId"));
        var sourceType = Required(Text(raw, "LicenseTypeName").Trim(), 120);
        string type;
        if (file == IndividualFile)
        {
            if (individual <= 0 || provider != 0 || !PersonTypes.TryGetValue(typeId, out var mapping) || sourceType != mapping.Source)
                throw new InvalidDataException("Wyoming individual identifier or license type changed.");
            type = mapping.Normalized;
        }
        else
        {
            type = file == FirmFile ? "Real Estate License" : "Appraisal Management Company";
            if (provider <= 0 || individual != provider || typeId != (file == FirmFile ? 2 : 1) || sourceType != type)
                throw new InvalidDataException("Wyoming provider identifier or license type changed.");
        }
        if (!raw.TryGetProperty("EntityRelationList", out var relations) || relations.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Wyoming relationship list changed.");
        foreach (var relation in relations.EnumerateArray())
        {
            if (relation.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Malformed Wyoming relationship.");
            _ = Integer(relation, "ProviderId"); _ = Integer(relation, "IndividualId");
            _ = Text(relation, "IndividualLicenseNumber");
            _ = Flag(relation, "Responsible"); _ = Flag(relation, "IsDeleted");
            _ = Date(Text(relation, "StartDate")); _ = Date(Text(relation, "EndDate")); _ = Date(Text(relation, "ReleasedDate"));
        }
        var name = file == IndividualFile
            ? string.Join(' ', new[] { Text(raw, "FirstName"), Text(raw, "MiddleName"), Text(raw, "LastName") }
                .Select(value => value.Trim()).Where(value => value.Length > 0))
            : Text(raw, "CorporationName").Trim();
        var address = string.Join(", ", new[] { "Address1", "Address2", "City", "State", "Zip", "CountryName" }
            .Select(key => Text(raw, key).Trim()).Where(value => value.Length > 0));
        return new(file, row, page, at, query?.Clone(), raw.Clone(), provider, individual, typeId,
            Optional(Text(raw, "LicenseNumber").Trim(), 40) ?? "", Required(name, file == IndividualFile ? 160 : 200),
            sourceType, type, Required(Text(raw, "LicenseStatusTypeName").Trim(), 40),
            Date(Text(raw, "OriginalLicenseDate")), Date(Text(raw, "LicenseEffectiveDate")),
            Date(Text(raw, "LicenseExpirationDate")), Optional(address, 500));
    }

    private static string SourceJson(WyomingWrecRecord row) => row.Page is null
        ? JsonSerializer.Serialize(new { source_file = row.File, source_row = row.Row, record = row.Raw })
        : JsonSerializer.Serialize(new { source_file = row.File, source_row = row.Row, source_page = row.Page,
            captured_at_utc = row.CapturedAtUtc, query = row.Query, record = row.Raw });
    private static bool HasCredential(WyomingWrecRecord row) =>
        row.LicenseNumber.Length > 0 && !row.LicenseNumber.Equals("N/A", StringComparison.OrdinalIgnoreCase);
    private static void RequireFile(string file)
    {
        if (file is not (IndividualFile or FirmFile or AmcFile)) throw new InvalidDataException("Unknown Wyoming official query.");
    }
    private static string Text(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException("Invalid Wyoming text field " + key + ".");
    }
    private static long Integer(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number : throw new InvalidDataException("Invalid Wyoming identifier " + key + ".");
    }
    private static bool? Flag(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true, JsonValueKind.False => false,
            _ => throw new InvalidDataException("Invalid Wyoming relationship flag.")
        };
    }
    private static string Required(string value, int maximum) => value.Length is > 0 && value.Length <= maximum
        ? value : throw new InvalidDataException("Invalid Wyoming required field length.");
    private static string? Optional(string value, int maximum) => value.Length == 0 ? null : Required(value, maximum);
    private static DateOnly? Date(string value)
    {
        if (value.Length == 0) return null;
        if (!DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
                "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            throw new InvalidDataException("Invalid Wyoming published date.");
        return date.Year == 1 ? null : DateOnly.FromDateTime(date.DateTime);
    }
}
