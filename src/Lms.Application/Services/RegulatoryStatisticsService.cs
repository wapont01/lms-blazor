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

/// <summary>
/// Holds computed bulk-roster comparisons keyed by snapshot IDs. Bulk snapshots are written once in a single
/// transaction and every collection creates a new snapshot, so an entry stays valid until it is evicted.
/// </summary>
public sealed class RegulatoryStatisticsCache
{
    private const int Capacity = 8;
    private readonly object _gate = new();
    private readonly Dictionary<(Guid? Baseline, Guid? Incoming), Lazy<Task<RegulatoryComparisonResult>>> _entries = [];
    private readonly LinkedList<(Guid? Baseline, Guid? Incoming)> _order = new();

    internal Task<RegulatoryComparisonResult> GetOrAdd((Guid? Baseline, Guid? Incoming) key,
        Func<Task<RegulatoryComparisonResult>> compute)
    {
        Lazy<Task<RegulatoryComparisonResult>> entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!) || (entry.IsValueCreated
                && (entry.Value.IsFaulted || entry.Value.IsCanceled)))
            {
                entry = new(compute);
                _entries[key] = entry;
            }
            _order.Remove(key);
            _order.AddFirst(key);
            while (_order.Count > Capacity)
            {
                _entries.Remove(_order.Last!.Value);
                _order.RemoveLast();
            }
        }
        return entry.Value;
    }
}

internal sealed record RegulatoryComparisonResult(
    IReadOnlyList<RegulatoryStatisticsRow> Professions,
    IReadOnlyList<RegulatoryStatusCount> Statuses,
    int ExcludedCurrent,
    int ExcludedCollected,
    IReadOnlyList<RegulatoryRecordComparison> Records);

/// <summary>The roster that was current before the last completed bulk collection run, and the roster it produced.</summary>
public sealed record RegulatoryLastRun(Guid? PreviousSnapshotId, Guid SnapshotId);

public interface IRegulatoryLastRunSource
{
    Task<RegulatoryLastRun?> GetAsync(string state, CancellationToken cancellationToken = default);
}

public sealed class RegulatoryStatisticsService(ApplicationDbContext db, RegulatoryStatisticsCache? cache = null,
    IRegulatoryLastRunSource? lastRuns = null)
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
            // These states compare only against earlier bulk rosters, not manual or canonical captures with other fields.
            var sameParserOnly = state is "Nevada" or "New York" or "Arizona" or "Connecticut" or "Oregon" or "Delaware" or "Virginia" or "Illinois";
            if (sameParserOnly)
                baseline = incoming is null ? null : published.FirstOrDefault(snapshot =>
                    snapshot.Id != incoming.Id && snapshot.ParserVersion == RegulatoryRoster.BulkParser(state));
            // Compare the last collection run with the roster current before it; an identical download reuses
            // the same snapshot, so previous and current are then the same roster.
            if (incoming is not null && lastRuns is not null
                && await lastRuns.GetAsync(state) is { PreviousSnapshotId: { } previousId } lastRun
                && lastRun.SnapshotId == incoming.Id
                && published.FirstOrDefault(snapshot => snapshot.Id == previousId) is { } previous
                && (!sameParserOnly || previous.ParserVersion == incoming.ParserVersion))
                baseline = previous;
        }

        // Only bulk rosters are written once in a single transaction; manual collections keep appending
        // records to the same pending snapshot, so their comparisons must always be recomputed.
        var comparison = cache is null || !bulkOnly
            ? await CompareAsync(baseline?.Id, incoming?.Id)
            : await cache.GetOrAdd((baseline?.Id, incoming?.Id), () => CompareAsync(baseline?.Id, incoming?.Id));

        return new(state, Describe(baseline), Describe(incoming), pending.Select(snapshot => Describe(snapshot)!).ToList(),
            comparison.Professions, comparison.Statuses, comparison.ExcludedCurrent, comparison.ExcludedCollected,
            DateTime.UtcNow, comparison.Records);
    }

    private async Task<RegulatoryComparisonResult> CompareAsync(Guid? baselineId, Guid? incomingId)
    {
        // Cached results hold one entry per license, so repeated profession names share one string instance.
        var professionNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var (current, excludedCurrent) = await ReadAsync(baselineId, professionNames);
        var (collected, excludedCollected) = await ReadAsync(incomingId, professionNames);
        var records = new List<RegulatoryRecordComparison>(collected.Count + (incomingId.HasValue ? current.Count : 0));
        foreach (var (identity, observed) in collected)
        {
            if (!current.TryGetValue(identity, out var previous))
            {
                records.Add(new(identity.Kind, identity.Number, observed.Profession, "New license", []));
                continue;
            }
            var labels = identity.Kind == "Licensees" ? LicenseFields : FirmFields;
            List<RegulatoryFieldChange>? changes = null;
            for (var index = 0; index < labels.Length; index++)
                if (previous.Values[index] != observed.Values[index])
                    (changes ??= []).Add(new(labels[index], previous.Values[index], observed.Values[index]));
            var classification = Normalize(previous.Status) != Normalize(observed.Status) ? "Status changed"
                : changes is not null ? "Other updates" : "Unchanged";
            records.Add(new(identity.Kind, identity.Number, observed.Profession, classification,
                changes is null ? [] : changes.ToArray()));
        }
        if (incomingId.HasValue)
            records.AddRange(current.Where(pair => !collected.ContainsKey(pair.Key)).Select(pair =>
                new RegulatoryRecordComparison(pair.Key.Kind, pair.Key.Number, pair.Value.Profession, "Not observed", [])));
        // Single pass per snapshot; groups are created current-first so display names match the first observation.
        var professionTallies = new Dictionary<(string Kind, string Type), Tally>();
        var statusTallies = new Dictionary<(string Kind, string Status), Tally>();
        foreach (var (identity, previous) in current)
        {
            var status = Normalize(previous.Status);
            GetTally(professionTallies, (previous.Kind, Normalize(previous.Profession)), previous.Profession).Current++;
            var statusTally = GetTally(statusTallies, (previous.Kind, status), previous.Status);
            statusTally.Current++;
            if (collected.TryGetValue(identity, out var next) && Normalize(next.Status) != status)
                statusTally.Left++;
        }
        foreach (var (identity, observed) in collected)
        {
            var status = Normalize(observed.Status);
            var professionTally = GetTally(professionTallies, (observed.Kind, Normalize(observed.Profession)), observed.Profession);
            var statusTally = GetTally(statusTallies, (observed.Kind, status), observed.Status);
            professionTally.Collected++;
            statusTally.Collected++;
            if (!current.TryGetValue(identity, out var previous))
            {
                professionTally.New++;
                statusTally.New++;
                continue;
            }
            if (!previous.Values.SequenceEqual(observed.Values))
                professionTally.Changed++;
            if (Normalize(previous.Status) != status)
                statusTally.Entered++;
        }
        var professions = professionTallies
            .OrderBy(pair => pair.Key.Kind).ThenBy(pair => pair.Key.Type)
            .Select(pair => new RegulatoryStatisticsRow(pair.Key.Kind, pair.Value.Label, pair.Value.Current,
                pair.Value.Collected, pair.Value.New, pair.Value.Changed,
                pair.Value.Collected - pair.Value.New - pair.Value.Changed))
            .ToList();
        var statuses = statusTallies
            .OrderBy(pair => pair.Key.Kind).ThenBy(pair => pair.Key.Status)
            .Select(pair => new RegulatoryStatusCount(pair.Key.Kind, pair.Value.Label, pair.Value.Current,
                pair.Value.Collected, pair.Value.New, pair.Value.Entered, pair.Value.Left))
            .ToList();

        return new(professions, statuses, excludedCurrent, excludedCollected, records);
    }

    private static RegulatoryStatisticsSnapshot? Describe(RegulatoryDataSnapshot? snapshot) => snapshot is null
        ? null : new(snapshot.Id, snapshot.SourceName, ToUtc(snapshot.RetrievedAtUtc), ToUtc(snapshot.ImportedAtUtc));

    private async Task<(Dictionary<Identity, Observation> Records, int Excluded)> ReadAsync(Guid? snapshotId,
        Dictionary<string, string> professionNames)
    {
        var records = new Dictionary<Identity, Observation>();
        if (!snapshotId.HasValue)
            return (records, 0);

        var excluded = 0;
        // Streamed so a large roster is not held twice (as entities and as observations).
        await foreach (var record in db.RegulatoryLicensees.AsNoTracking()
            .Where(record => record.SnapshotId == snapshotId.Value).AsAsyncEnumerable())
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
        await foreach (var record in db.RegulatoryFirms.AsNoTracking()
            .Where(record => record.SnapshotId == snapshotId.Value).AsAsyncEnumerable())
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
            if (!professionNames.TryGetValue(profession, out var sharedProfession))
                professionNames[profession] = sharedProfession = profession;
            profession = sharedProfession;
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
    private static Tally GetTally<TKey>(Dictionary<TKey, Tally> tallies, TKey key, string label) where TKey : notnull
    {
        if (!tallies.TryGetValue(key, out var tally))
            tallies[key] = tally = new Tally(label);
        return tally;
    }

    private sealed class Tally(string label)
    {
        public string Label { get; } = label;
        public int Current;
        public int Collected;
        public int New;
        public int Changed;
        public int Entered;
        public int Left;
    }
    private sealed record Identity(string Kind, string Number, string Type);
    private sealed record Observation(string Kind, string Profession, string Status, string[] Values);
}
