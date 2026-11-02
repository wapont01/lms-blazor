using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public sealed record RosterImportEvidence(string File, string Sha256, string Coverage, int SourceRows,
    int ImportedRows, int DuplicateRows, IReadOnlyDictionary<string, int> Exclusions, IReadOnlyList<int> RejectedDataRows,
    IReadOnlyDictionary<string, int> Warnings, IReadOnlyList<int> WarningDataRows);
public sealed record RosterImportResult(string State, Guid SnapshotId, bool Imported, int Individuals, int Firms,
    int Affiliations, string Status, IReadOnlyList<RosterImportEvidence> Evidence);
public sealed record RosterImportRecord(string State, string Number, string Name, string Type, string Status,
    bool Business, DateOnly? Issued, DateOnly? Expires, string RelatedNumber, string RelatedName);
public sealed record RosterImportBatch(string State, string SourceUrl, DateTime CapturedAt, string Fingerprint,
    IReadOnlyList<RosterImportRecord> Records, IReadOnlyList<RosterImportEvidence> Evidence,
    IReadOnlyList<RosterImportRelationship>? Relationships = null);
public sealed record RosterImportRelationship(string FirmNumber, string PersonNumber, string PersonType, DateOnly? EffectiveDate);

public static class RosterSourceImport
{
    private const string Version = "canonical-roster-v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<RosterImportBatch> Prepare(string rosterRoot, IReadOnlyList<RosterImportSource>? sources = null)
    {
        sources ??= RosterImportSources.Remaining;
        var catalog = RosterSourceTable.Read(Path.Combine(rosterRoot, "source-catalog.csv")).ToList();
        var records = new Dictionary<string, List<RosterImportRecord>>(StringComparer.Ordinal);
        var evidence = new Dictionary<string, List<RosterImportEvidence>>(StringComparer.Ordinal);
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        var dates = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var relationships = new Dictionary<string, List<RosterImportRelationship>>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var entry = catalog.Single(row => row["Jurisdiction"] == source.Jurisdiction
                && Path.GetFileName(row["LegacyRelativePath"]) == source.FileName);
            var slug = source.Jurisdiction.ToLowerInvariant().Replace(' ', '-');
            var relative = entry["LegacyRelativePath"].Split('\\').Skip(1).ToArray();
            var captureRoot = Path.Combine(rosterRoot, slug, entry["CaptureDate"]);
            var path = Path.Combine(captureRoot, entry["Role"], Path.Combine(relative));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(captureRoot, "manifest.json")));
            var expected = manifest.RootElement.GetProperty("Files").EnumerateArray().Single(file =>
                file.GetProperty("LegacyRelativePath").GetString() == entry["LegacyRelativePath"]);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (hash != expected.GetProperty("Sha256").GetString())
                throw new InvalidDataException($"Canonical source hash mismatch: {source.FileName}");
            var date = DateTime.ParseExact(entry["CaptureDate"], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var counters = new Dictionary<string, SourceCounter>(StringComparer.Ordinal);
            var ambiguousBusinessNumbers = source.Kind == "BusinessQuery"
                ? RosterSourceTable.Read(path).GroupBy(row => Required(row, "LicenseNumber"))
                    .Where(group => group.Select(row => Required(row, "LicenseeName")).Distinct(StringComparer.Ordinal).Count() > 1)
                    .Select(group => group.Key).ToHashSet(StringComparer.Ordinal)
                : [];
            var endpointIndexes = new Dictionary<string, (HashSet<string> Firms, HashSet<string> People, HashSet<(string Number, string Type)> TypedPeople)>();
            var rowNumber = 1;
            foreach (var row in RosterSourceTable.Read(path, source.Jurisdiction == "Colorado"))
            {
                rowNumber++;
                var state = source.Jurisdiction == "multi-state" ? Required(row, "State") : source.Jurisdiction;
                if (!DevelopmentCourseCatalog.States.Contains(state) && state != "Puerto Rico")
                    throw new InvalidDataException($"Unsupported source jurisdiction: {state}");
                if (!records.ContainsKey(state))
                {
                    records.Add(state, []);
                    evidence.Add(state, []);
                    seen.Add(state, new(StringComparer.Ordinal));
                    relationships.Add(state, []);
                    urls.Add(state, source.Jurisdiction == "multi-state" ? Required(row, "OfficialSourceUrl") : entry["OfficialSourceUrl"]);
                    dates.Add(state, date);
                }
                if (date > dates[state]) dates[state] = date;
                if (!counters.TryGetValue(state, out var counter))
                    counters.Add(state, counter = new());
                counter.SourceRows++;
                if (source.Kind is "UnclassifiedHolder" or "BusinessQuery")
                {
                    var number = Required(row, "LicenseNumber");
                    if (records[state].Any(record => record.Business && record.Number == number))
                    {
                        counter.Duplicates++;
                        continue;
                    }
                    if (source.Kind == "UnclassifiedHolder" || ambiguousBusinessNumbers.Contains(number))
                    {
                        counter.Exclude(source.Kind == "UnclassifiedHolder"
                            ? "Holder kind not established by captured profile; not classified from name"
                            : "Multiple published business aliases without verified legal-name profile");
                        counter.RejectedDataRows.Add(rowNumber - 1);
                        continue;
                    }
                }
                if (source.Kind is "FirmToPerson" or "PersonToFirm" or "WyomingRelationship")
                {
                    var reversed = source.Kind == "PersonToFirm";
                    var firmNumber = source.Kind == "WyomingRelationship" ? Required(row, "FirmLicenseNumber")
                        : Required(row, reversed ? "ToLicenseNumber" : "FromLicenseNumber");
                    var personNumber = source.Kind == "WyomingRelationship" ? Required(row, "IndividualLicenseNumber")
                        : Required(row, reversed ? "FromLicenseNumber" : "ToLicenseNumber");
                    var personType = source.Kind == "WyomingRelationship" ? "" :
                        Value(row, reversed ? "FromType" : "ToType", reversed ? "FromLicenseType" : "ToLicenseType");
                    if (!endpointIndexes.TryGetValue(state, out var endpoints))
                    {
                        endpoints = (records[state].Where(record => record.Business).Select(record => record.Number).ToHashSet(),
                            records[state].Where(record => !record.Business).Select(record => record.Number).ToHashSet(),
                            records[state].Where(record => !record.Business).Select(record => (record.Number, record.Type)).ToHashSet());
                        endpointIndexes.Add(state, endpoints);
                    }
                    if (!endpoints.Firms.Contains(firmNumber)
                        || !(personType.Length == 0 ? endpoints.People.Contains(personNumber) : endpoints.TypedPeople.Contains((personNumber, personType))))
                    {
                        counter.Exclude("Relationship endpoint not an imported firm/individual; no inferred link");
                        continue;
                    }
                    relationships[state].Add(new(firmNumber, personNumber, personType,
                        Date(Value(row, "PublishedStartDate", "PublishedSupervisionEffectiveDate"), false)));
                    counter.Imported++;
                    continue;
                }
                RosterImportRecord? mapped;
                string? excluded;
                string? warning;
                try { mapped = Map(source, row, state, out excluded, out warning); }
                catch (Exception ex) when (ex is InvalidDataException or ValidationException or FormatException)
                {
                    throw new InvalidDataException($"{source.FileName}, data row {rowNumber - 1}: {ex.Message}", ex);
                }
                if (warning is not null)
                {
                    counter.Warnings[warning] = counter.Warnings.GetValueOrDefault(warning) + 1;
                    counter.WarningDataRows.Add(rowNumber - 1);
                }
                if (excluded is not null)
                {
                    counter.Exclude(excluded);
                    if (excluded.StartsWith("Missing", StringComparison.Ordinal) || excluded.StartsWith("Published", StringComparison.Ordinal))
                        counter.RejectedDataRows.Add(rowNumber - 1);
                    continue;
                }
                if (mapped is null) throw new InvalidDataException("Mapping returned no record or exclusion.");
                // Multiple affiliation rows do not create duplicate credentials; explicit source IDs remain significant.
                var identity = JsonSerializer.Serialize(new { Record = mapped, LicenseId = Value(row, "LicenseId"), PersonId = Value(row, "PersonId") });
                if (!seen[state].Add(identity))
                {
                    counter.Duplicates++;
                    continue;
                }
                records[state].Add(mapped);
                counter.Imported++;
            }
            foreach (var pair in counters)
                evidence[pair.Key].Add(new(entry["LegacyRelativePath"], hash, source.Coverage, pair.Value.SourceRows,
                    pair.Value.Imported, pair.Value.Duplicates, pair.Value.Exclusions, pair.Value.RejectedDataRows,
                    pair.Value.Warnings, pair.Value.WarningDataRows));
        }
        return records.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            var state = pair.Key;
            var files = evidence[state];
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                Version + (state == "Ohio" ? "-ohio-v2" : "") + state + JsonSerializer.Serialize(files))));
            if (!Uri.TryCreate(urls[state], UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                throw new InvalidDataException($"Missing official source URL for {state}.");
            if (pair.Value.Where(record => record.Business).GroupBy(record => record.Number).Any(group => group.Count() > 1))
                throw new InvalidDataException($"Conflicting business credentials share a license number in {state}; resolve before import.");
            return new RosterImportBatch(state, urls[state], dates[state], fingerprint, pair.Value, files, relationships[state]);
        }).ToList();
    }

    public static async Task<RosterImportResult> ApplyAsync(ApplicationDbContext db, RosterImportBatch batch,
        bool approveInterim = false, CancellationToken ct = default)
    {
        var parser = Version + "-" + batch.Fingerprint[..40];
        var existing = await db.RegulatoryDataSnapshots.AsNoTracking().SingleOrDefaultAsync(snapshot =>
            snapshot.State == batch.State && snapshot.ParserVersion == parser, ct);
        var people = batch.Records.Count(record => !record.Business);
        var firms = batch.Records.Count(record => record.Business);
        if (existing is not null)
        {
            if (await db.RegulatoryLicensees.CountAsync(record => record.SnapshotId == existing.Id, ct) != people
                || await db.RegulatoryFirms.CountAsync(record => record.SnapshotId == existing.Id, ct) != firms)
                throw new InvalidDataException($"Previously imported snapshot counts differ for {batch.State}.");
            return new(batch.State, existing.Id, false, people, firms,
                await db.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == existing.Id, ct),
                existing.Status, batch.Evidence);
        }
        var duplicateFirms = batch.Records.Where(record => record.Business).GroupBy(record => record.Number)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateFirms is not null)
            throw new InvalidDataException($"Distinct business records share a license number in {batch.State}; resolve before import.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var snapshot = new RegulatoryDataSnapshot
        {
            State = batch.State, SourceName = $"{batch.State} commission capture; see canonical roster manifest",
            SourceUrl = batch.SourceUrl, RetrievedAtUtc = batch.CapturedAt, ParserVersion = parser,
            Status = approveInterim ? RegulatorySnapshotStatuses.Approved : RegulatorySnapshotStatuses.PendingReview,
            ReviewedAtUtc = approveInterim ? DateTime.UtcNow : null
        };
        db.RegulatoryDataSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var firmIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var peopleIds = new List<(RosterImportRecord Record, Guid Id)>();
        foreach (var chunk in batch.Records.Chunk(1000))
        {
            foreach (var record in chunk)
            {
                if (record.Business)
                {
                    var firm = new RegulatoryFirm
                    {
                        SnapshotId = snapshot.Id, State = record.State, LicenseNumber = record.Number, LegalName = record.Name,
                        LicenseStatus = Null(record.Status), LicenseIssuedDate = record.Issued, LicenseRenewalDate = record.Expires
                    };
                    Validator.ValidateObject(firm, new ValidationContext(firm), true);
                    firmIds.Add(record.Number, firm.Id);
                    db.RegulatoryFirms.Add(firm);
                }
                else
                {
                    var licensee = new RegulatoryLicensee
                    {
                        SnapshotId = snapshot.Id, State = record.State, LicenseNumber = record.Number, Name = record.Name,
                        LicenseType = record.Type, LicenseStatus = Null(record.Status), LicenseIssuedDate = record.Issued,
                        OriginalLicenseDate = record.Issued, LicenseRenewalDate = record.Expires,
                        RelatedLicenseNumber = Null(record.RelatedNumber), RelatedLicenseName = Null(record.RelatedName)
                    };
                    Validator.ValidateObject(licensee, new ValidationContext(licensee), true);
                    peopleIds.Add((record, licensee.Id));
                    db.RegulatoryLicensees.Add(licensee);
                }
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        var edgeKeys = new HashSet<(Guid Firm, Guid Person)>();
        var edgesByPerson = (batch.Relationships ?? []).ToLookup(edge => edge.PersonNumber);
        var pendingEdges = 0;
        foreach (var person in peopleIds)
        {
            var candidates = edgesByPerson[person.Record.Number].Where(edge =>
                edge.PersonType.Length == 0 || edge.PersonType == person.Record.Type).ToList();
            if (person.Record.RelatedNumber.Length > 0 && firmIds.ContainsKey(person.Record.RelatedNumber))
                candidates.Add(new(person.Record.RelatedNumber, person.Record.Number, person.Record.Type, null));
            foreach (var edge in candidates)
            {
                if (!firmIds.TryGetValue(edge.FirmNumber, out var firmId))
                    throw new InvalidDataException("Relationship references an absent imported firm.");
                if (!edgeKeys.Add((firmId, person.Id))) continue;
                db.RegulatoryFirmLicenseeAffiliations.Add(new RegulatoryFirmLicenseeAffiliation
                {
                    SnapshotId = snapshot.Id, FirmId = firmId, LicenseeId = person.Id,
                    RelationshipEffectiveDate = edge.EffectiveDate
                });
                pendingEdges++;
            }
            if (pendingEdges >= 1000)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                pendingEdges = 0;
            }
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        db.AuditLogs.Add(new AuditLog
        {
            Action = approveInterim ? "regulatory-snapshot.approved" : "regulatory-snapshot.imported",
            TargetType = nameof(RegulatoryDataSnapshot), TargetId = snapshot.Id,
            Details = JsonSerializer.Serialize(new
            {
                Authorization = approveInterim
                    ? "Workspace owner authorized interim roster acceptance and instructed continuing all available imports on 2026-10-24; formal approval criteria remain undecided. Not license ownership verification."
                    : "Imported for review; no approval or license ownership established.",
                batch.State, batch.Fingerprint, Individuals = people, Firms = firms, Affiliations = edgeKeys.Count, Sources = batch.Evidence
            }, JsonOptions)
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return new(batch.State, snapshot.Id, true, people, firms, edgeKeys.Count, snapshot.Status, batch.Evidence);
    }

    private static RosterImportRecord? Map(RosterImportSource source, Dictionary<string, string> row, string state,
        out string? exclusion, out string? warning)
    {
        exclusion = null;
        warning = null;
        var business = source.Kind is "Business license" or "BusinessQuery";
        var kind = Value(row, "RecordKind");
        if (source.Kind == "Mixed")
        {
            if (kind is not ("Individual" or "Business license"))
                throw new InvalidDataException($"Unsupported normalized record kind: {kind}");
            business = kind == "Business license";
        }
        var number = Value(row, "LicenseNumber", "LicNumber", "RegistrationNumber", "Credential Number", "License Number");
        var type = source.FixedType ?? Value(row, "SourceLicenseType", "LicenseType", "LicType", "CredentialType",
            "Category", "Credential Type Prefix", "License Type");
        var name = Value(row, "PublishedName", "LicenseeName", "FullName", "EntityName", "BusinessName", "LegalName");
        var status = Value(row, "SourceStatus", "LicenseStatus", "Status", "LicStatus");
        var issued = Value(row, "OriginalIssueDate", "OriginalLicenseDate", "LicenseOriginalIssueDate", "IssueDate",
            "OriginalDate", "License First Issue Date", "License First Issuance Date");
        var expires = Value(row, "Expiration", "ExpirationDate", "LicenseExpirationDate", "ExpireDate",
            "License Expiration Date", "License Expiration");
        if (source.Kind == "Mixed")
        {
            using var payload = JsonDocument.Parse(Required(row, "SourceRecordJson"));
            var fields = payload.RootElement;
            issued = JsonValue(fields, "Issue Date", "OriginalIssueDate");
            expires = JsonValue(fields, "Expiration Date", "Expiration");
        }
        if (source.Jurisdiction == "Arizona" && Required(row, "LicCategory") != "Real Estate")
            exclusion = "Outside real-estate license category";
        if (source.Kind == "Ohio")
        {
            type = Required(row, "Regulatory Authorization Type: Name");
            business = type is "Brokerage" or "Branch Office" or "Sole Proprietor";
            if (!business && type is not ("Broker" or "Salesperson"))
                exclusion = "Outside broker/salesperson/brokerage scope";
            number = Required(row, "Name");
            name = business ? Value(row, "Organization: Account Name") :
                Join(row, "Legacy First Name", "Legacy Last Name");
            if (!business && name.Length == 0) name = Value(row, "Organization: Account Name");
            issued = "";
            expires = Value(row, "Expiration Date");
        }
        else if (source.Jurisdiction == "Colorado")
        {
            if (business) name = Value(row, "Entity Name", "EntityName");
            else name = Join(row, row.ContainsKey("FirstName") ? "FirstName" : "First Name",
                row.ContainsKey("MiddleName") ? "MiddleName" : "Middle Name",
                row.ContainsKey("LastName") ? "LastName" : "Last Name", "Suffix");
        }
        else if (source.Jurisdiction == "Maryland") name = Required(row, "Name");
        else if (source.Jurisdiction == "Mississippi") name = Required(row, "Name");
        else if (source.Jurisdiction == "Vermont")
            name = business ? Value(row, "Business Name", "BusinessName", "Organization Name", "Name") :
                Join(row, "First Name", "Middle Name", "Last Name");
        else if (source.Jurisdiction == "Arizona")
            name = business ? Value(row, "LegalName", "LegalBusinessName", "EmployerLegalName") :
                Join(row, "FirstName", "MiddleName", "LastName");
        if (source.Jurisdiction == "Louisiana" && business && name.Contains('\n'))
        {
            var names = name.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.StartsWith("AKA:", StringComparison.Ordinal) ? line[4..].Trim() : line)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length == 1)
            {
                name = names[0];
                warning = "Repeated identical published name/AKA lines collapsed; original source retained";
            }
        }
        if (source.Jurisdiction == "Wyoming" && type is not ("Associate Broker License" or "Responsible Broker License"
            or "Real Estate Salesperson License" or "Real Estate License"))
            exclusion = "Appraiser/AMC credential; not real-estate broker/salesperson/firm";
        if (source.Jurisdiction == "Wyoming" && name == "Test Record - Please Delete")
            exclusion = "Published source explicitly labels test record for deletion";
        if (exclusion is not null) return null;
        if (name.Length == 0)
        {
            exclusion = "Missing published personal/business name; no identity fabricated";
            return null;
        }
        if (number.Length == 0 || number == "N/A")
        {
            exclusion = "Missing published license number; no credential identity fabricated";
            return null;
        }
        if (type.Length == 0)
            throw new InvalidDataException("Missing exact published credential type.");
        if (source.Jurisdiction == "Rhode Island" && expires.Length > 0 && expires.All(char.IsAsciiDigit)
            && expires.TrimStart('0') == Value(row, "InternalId").TrimStart('C', '0'))
        {
            warning = "Source expiration repeats internal record ID, not a date; expiry left unknown";
            expires = "";
        }
        var record = new RosterImportRecord(state, number, name, type, status, business,
            Date(issued, source.Kind == "Ohio"), Date(expires, source.Kind == "Ohio"),
            source.Kind == "Ohio" ? Value(row, "Primary License: Name") :
                Value(row, "EmployerLicNumber", "EmployerLicenseNumber", "ParentFirmLicenseNumber"),
            source.Kind == "Ohio" ? "" : Value(row, "EmployerLegalName", "EmployerName"));
        if (record.RelatedNumber.Length > 40 || record.RelatedName.Length > 160)
        {
            warning = "Published affiliation reference exceeds schema; reference retained in source, not truncated or linked";
            record = record with { RelatedNumber = "", RelatedName = "" };
        }
        if (number.Length > 40 || (!business && type.Length > 40) || status.Length > 40 || name.Length > (business ? 200 : 160))
        {
            exclusion = "Published field exceeds application schema length; source retained without truncation";
            return null;
        }
        return record;
    }

    private static string Join(Dictionary<string, string> row, params string[] keys) =>
        string.Join(" ", keys.Select(key => Value(row, key)).Where(value => value.Length > 0));
    private static string Required(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) && value.Length > 0 ? value :
            throw new InvalidDataException($"Missing required source field: {key}");
    private static string Value(Dictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
        return "";
    }
    private static string? Null(string value) => value.Length == 0 ? null : value;
    private static string JsonValue(JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
            if (payload.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!.Trim();
        return "";
    }
    private static DateOnly? Date(string value, bool excel)
    {
        if (value.Length == 0) return null;
        if (excel && double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var serial))
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return DateOnly.FromDateTime(parsed);
        throw new InvalidDataException("Unrecognized published date format.");
    }
    private sealed class SourceCounter
    {
        public int SourceRows { get; set; }
        public int Imported { get; set; }
        public int Duplicates { get; set; }
        public Dictionary<string, int> Exclusions { get; } = new(StringComparer.Ordinal);
        public List<int> RejectedDataRows { get; } = [];
        public Dictionary<string, int> Warnings { get; } = new(StringComparer.Ordinal);
        public List<int> WarningDataRows { get; } = [];
        public void Exclude(string reason) => Exclusions[reason] = Exclusions.GetValueOrDefault(reason) + 1;
    }
}
