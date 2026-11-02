using System.Globalization;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public sealed record RegulatoryStatisticsSnapshot(
    Guid Id, string SourceName, DateTime RetrievedAtUtc, DateTime ImportedAtUtc);

public sealed record RegulatoryStatisticsRow(
    string Kind, string Profession, int Current, int Collected, int New, int Changed, int Unchanged);

public sealed record RegulatoryStatusCount(string Kind, string Status, int Current, int Collected,
    int NewLicenses = 0, int Entered = 0, int Left = 0);

public sealed record RegulatoryFieldChange(string Field, string Previous, string Incoming);
public sealed record RegulatoryRecordComparison(
    string Kind, string LicenseNumber, string Profession, string Classification,
    IReadOnlyList<RegulatoryFieldChange> Changes);

public sealed record RegulatoryStateStatistics(
    string State,
    RegulatoryStatisticsSnapshot? Baseline,
    RegulatoryStatisticsSnapshot? Incoming,
    IReadOnlyList<RegulatoryStatisticsSnapshot> PendingSnapshots,
    IReadOnlyList<RegulatoryStatisticsRow> Professions,
    IReadOnlyList<RegulatoryStatusCount> Statuses,
    int ExcludedCurrent,
    int ExcludedCollected,
    DateTime CheckedAtUtc,
    IReadOnlyList<RegulatoryRecordComparison> Records);

public sealed class RegulatoryStatisticsService(ApplicationDbContext db)
{
    public async Task<List<string>> GetStatesAsync()
    {
        var storedStates = await db.RegulatoryDataSnapshots.AsNoTracking()
            .Select(snapshot => snapshot.State).Distinct().ToListAsync();
        return StateLicensingReferenceService.GetStates().Select(state => state.Name)
            .Concat(storedStates).Where(state => !string.IsNullOrWhiteSpace(state))
            .Select(state => state.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(state => state, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<RegulatoryStateStatistics> GetAsync(string state, Guid? incomingId = null, bool bulkOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        state = state.Trim();
        var snapshots = await db.RegulatoryDataSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.State == state
                && (snapshot.Status == RegulatorySnapshotStatuses.Approved
                    || snapshot.Status == RegulatorySnapshotStatuses.PendingReview))
            .ToListAsync();
        snapshots = snapshots.OrderByDescending(snapshot => ToUtc(snapshot.RetrievedAtUtc))
            .ThenByDescending(snapshot => ToUtc(snapshot.ImportedAtUtc))
            .ThenByDescending(snapshot => snapshot.Id).ToList();
        var baseline = snapshots.FirstOrDefault(snapshot => snapshot.Status == RegulatorySnapshotStatuses.Approved);
        var pending = snapshots.Where(snapshot => snapshot.Status == RegulatorySnapshotStatuses.PendingReview
            && (!bulkOnly || snapshot.ParserVersion == RegulatoryRoster.BulkParser(state))).ToList();
        var incoming = !bulkOnly && incomingId.HasValue
            ? pending.SingleOrDefault(snapshot => snapshot.Id == incomingId.Value)
                ?? throw new InvalidOperationException("The selected incoming snapshot is no longer pending review for this state. Refresh the page.")
            : pending.FirstOrDefault();
        if (bulkOnly)
        {
            var published = snapshots.Where(snapshot => snapshot.Status == RegulatorySnapshotStatuses.Approved)
                .OrderByDescending(snapshot => ToUtc(snapshot.ActivatedAtUtc ?? snapshot.RetrievedAtUtc))
                .ThenByDescending(snapshot => ToUtc(snapshot.ImportedAtUtc)).ThenBy(snapshot => snapshot.Id).ToList();
            incoming = published.FirstOrDefault(snapshot => snapshot.ParserVersion == RegulatoryRoster.BulkParser(state));
            baseline = incoming is null ? baseline : published.FirstOrDefault(snapshot => snapshot.Id != incoming.Id);
            if (state == "Nevada")
                baseline = incoming is null ? null : published.FirstOrDefault(snapshot =>
                    snapshot.Id != incoming.Id && snapshot.ParserVersion == NevadaNredBulkImportService.ParserVersion);
        }

        var (current, excludedCurrent) = await ReadAsync(baseline?.Id);
        var (collected, excludedCollected) = await ReadAsync(incoming?.Id);
        var records = new List<RegulatoryRecordComparison>();
        foreach (var (identity, observed) in collected)
        {
            if (!current.TryGetValue(identity, out var previous))
            {
                records.Add(new(identity.Kind, identity.Number, observed.Profession, "New license", []));
                continue;
            }
            var labels = identity.Kind == "Licensees" ? LicenseFields : FirmFields;
            var changes = labels.Select((label, index) => new RegulatoryFieldChange(label,
                    previous.Values[index], observed.Values[index]))
                .Where(change => change.Previous != change.Incoming).ToList();
            var classification = Normalize(previous.Status) != Normalize(observed.Status) ? "Status changed"
                : changes.Count > 0 ? "Other updates" : "Unchanged";
            records.Add(new(identity.Kind, identity.Number, observed.Profession, classification, changes));
        }
        if (incoming is not null)
            records.AddRange(current.Where(pair => !collected.ContainsKey(pair.Key)).Select(pair =>
                new RegulatoryRecordComparison(pair.Key.Kind, pair.Key.Number, pair.Value.Profession, "Not observed", [])));
        var professions = current.Values.Concat(collected.Values)
            .GroupBy(record => (record.Kind, Type: Normalize(record.Profession)))
            .OrderBy(group => group.Key.Kind).ThenBy(group => group.Key.Type)
            .Select(group =>
            {
                var existing = current.Where(pair => pair.Value.Kind == group.Key.Kind && Normalize(pair.Value.Profession) == group.Key.Type).ToList();
                var observed = collected.Where(pair => pair.Value.Kind == group.Key.Kind && Normalize(pair.Value.Profession) == group.Key.Type).ToList();
                var added = observed.Count(pair => !current.ContainsKey(pair.Key));
                var changed = observed.Count(pair => current.TryGetValue(pair.Key, out var previous)
                    && !previous.Values.SequenceEqual(pair.Value.Values));
                return new RegulatoryStatisticsRow(group.Key.Kind, group.First().Profession,
                    existing.Count, observed.Count, added, changed, observed.Count - added - changed);
            }).ToList();
        var statuses = current.Values.Concat(collected.Values)
            .GroupBy(record => (record.Kind, Status: Normalize(record.Status)))
            .OrderBy(group => group.Key.Kind).ThenBy(group => group.Key.Status)
            .Select(group => new RegulatoryStatusCount(group.Key.Kind, group.First().Status,
                current.Values.Count(record => record.Kind == group.Key.Kind && Normalize(record.Status) == group.Key.Status),
                collected.Values.Count(record => record.Kind == group.Key.Kind && Normalize(record.Status) == group.Key.Status),
                collected.Count(pair => pair.Value.Kind == group.Key.Kind && Normalize(pair.Value.Status) == group.Key.Status
                    && !current.ContainsKey(pair.Key)),
                collected.Count(pair => pair.Value.Kind == group.Key.Kind && Normalize(pair.Value.Status) == group.Key.Status
                    && current.TryGetValue(pair.Key, out var previous) && Normalize(previous.Status) != group.Key.Status),
                current.Count(pair => pair.Value.Kind == group.Key.Kind && Normalize(pair.Value.Status) == group.Key.Status
                    && collected.TryGetValue(pair.Key, out var next) && Normalize(next.Status) != group.Key.Status)))
            .ToList();

        return new(state, Describe(baseline), Describe(incoming), pending.Select(snapshot => Describe(snapshot)!).ToList(),
            professions, statuses, excludedCurrent, excludedCollected, DateTime.UtcNow, records);
    }

    private static RegulatoryStatisticsSnapshot? Describe(RegulatoryDataSnapshot? snapshot) => snapshot is null
        ? null : new(snapshot.Id, snapshot.SourceName, ToUtc(snapshot.RetrievedAtUtc), ToUtc(snapshot.ImportedAtUtc));

    private async Task<(Dictionary<Identity, Observation> Records, int Excluded)> ReadAsync(Guid? snapshotId)
    {
        var records = new Dictionary<Identity, Observation>();
        if (!snapshotId.HasValue)
            return (records, 0);

        var licensees = await db.RegulatoryLicensees.AsNoTracking()
            .Where(record => record.SnapshotId == snapshotId.Value).ToListAsync();
        var firms = await db.RegulatoryFirms.AsNoTracking()
            .Where(record => record.SnapshotId == snapshotId.Value).ToListAsync();
        var excluded = 0;
        foreach (var record in licensees)
        {
            var profession = Profession(record.State, record.LicenseType);
            Add("Licensees", record.State == "Texas" ? TexasBulkImportService.CredentialKey(record.LicenseNumber) : record.LicenseNumber, profession, record.LicenseStatus,
                Normalize(record.State) == "FLORIDA" && profession == "Broker Sales" ? "Broker"
                    : record.State == "California" ? profession + "|" + Normalize(record.RelatedLicenseType) + "|" + Normalize(record.RelatedLicenseNumber)
                    : profession,
            [
                Normalize(profession), Normalize(record.Name), Normalize(record.LicenseLevel), Normalize(record.LicenseStatus),
                Date(record.LicenseIssuedDate), Date(record.OriginalLicenseDate), Date(record.LicenseRenewalDate),
                Normalize(record.RelatedLicenseNumber), Normalize(record.RelatedLicenseType), Normalize(record.RelatedLicenseName),
                record.IsRestricted.ToString(), record.EthicsAndAgencyOnly.ToString(), record.IsAdditionalLicenseRecord?.ToString() ?? "",
                record.UpdateHours?.ToString(CultureInfo.InvariantCulture) ?? "",
                record.ElectiveHours?.ToString(CultureInfo.InvariantCulture) ?? "",
                record.PostlicensingHours?.ToString(CultureInfo.InvariantCulture) ?? "", record.IsBicEligible.ToString()
            ]);
        }
        foreach (var record in firms)
        {
            Add("Firms / offices", record.State == "Texas" ? TexasBulkImportService.CredentialKey(record.LicenseNumber) : record.LicenseNumber, "Firm / office", record.LicenseStatus, "Firm / office",
            [
                Normalize(record.LegalName), Normalize(record.TradeName), Normalize(record.LicenseStatus),
                Date(record.LicenseIssuedDate), Date(record.LicenseRenewalDate),
                Normalize(record.Address), Normalize(record.QualifyingBrokerName)
            ]);
        }
        return (records, excluded);

        void Add(string kind, string number, string profession, string? status, string identityType, string[] values)
        {
            if (string.IsNullOrWhiteSpace(number))
            {
                excluded++;
                return;
            }
            profession = string.IsNullOrWhiteSpace(profession) ? "Type not reported" : profession.Trim();
            var key = new Identity(kind, Normalize(number), Normalize(identityType));
            var observation = new Observation(kind, profession,
                string.IsNullOrWhiteSpace(status) ? "Not reported" : status.Trim(), values);
            if (!records.TryAdd(key, observation) && !records[key].Values.SequenceEqual(values))
                throw new InvalidOperationException($"Snapshot {snapshotId} has conflicting {kind.ToLowerInvariant()} records for license '{number}' and type '{profession}'. Resolve the duplicate before comparing.");
        }
    }

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? "";
    private static readonly string[] LicenseFields =
    [
        "Profession / type", "Name", "License level", "Status", "Issued date", "Original license date",
        "Renewal date", "Related license number", "Related license type", "Related license name",
        "Restricted", "Ethics and agency only", "Additional license record", "Update hours", "Elective hours",
        "Postlicensing hours", "BIC eligible"
    ];
    private static readonly string[] FirmFields =
    [
        "Legal name", "Trade name", "Status", "Issued date", "Renewal date", "Address", "Qualifying broker"
    ];
    private static string Profession(string state, string? type)
    {
        if (Normalize(state) == "FLORIDA")
        {
            return Normalize(type) switch
            {
                "SL" or "SALES ASSOCIATE" => "Sales Associate",
                "BK" or "BROKER" => "Broker",
                "BROKER SALES" => "Broker Sales",
                "ZH" or "RE INSTRUCTOR" or "REAL ESTATE INSTRUCTOR" => "Real Estate Instructor",
                _ => string.IsNullOrWhiteSpace(type) ? "Type not reported" : type.Trim()
            };
        }
        return string.IsNullOrWhiteSpace(type) ? "Type not reported" : type.Trim();
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
    private static string Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    private sealed record Identity(string Kind, string Number, string Type);
    private sealed record Observation(string Kind, string Profession, string Status, string[] Values);
}
