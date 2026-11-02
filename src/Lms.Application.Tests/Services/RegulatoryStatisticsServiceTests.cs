using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class RegulatoryStatisticsServiceTests
{
    [Fact]
    public async Task NevadaInitialSynchronizationDoesNotCompareAgainstPartialLegacyCapture()
    {
        await using var fixture = await Fixture.CreateAsync();
        var legacy = fixture.Snapshot("Nevada", RegulatorySnapshotStatuses.Approved, 1);
        fixture.License(legacy, "legacy-only");
        var current = fixture.Snapshot("Nevada", RegulatorySnapshotStatuses.Approved, 2);
        current.ParserVersion = NevadaNredBulkImportService.ParserVersion;
        fixture.License(current, "current");
        await fixture.Db.SaveChangesAsync();
        var stats = await fixture.Service.GetAsync("Nevada", bulkOnly: true);
        Assert.Null(stats.Baseline);
        Assert.Equal(current.Id, stats.Incoming!.Id);
        Assert.Equal("New license", Assert.Single(stats.Records).Classification);
        Assert.True(RegulatoryRoster.HasBulkProvider("Nevada"));
    }

    [Fact]
    public async Task NevadaComparisonReportsUpdatesNewUnchangedAndNotObservedWithoutInferringRevocation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var previous = fixture.Snapshot("Nevada", RegulatorySnapshotStatuses.Approved, 1);
        var current = fixture.Snapshot("Nevada", RegulatorySnapshotStatuses.Approved, 2);
        previous.ParserVersion = current.ParserVersion = NevadaNredBulkImportService.ParserVersion;
        fixture.License(previous, "updated", name: "Previous name");
        fixture.License(current, "updated", name: "Updated name");
        fixture.License(previous, "unchanged");
        fixture.License(current, "unchanged");
        fixture.License(previous, "missing");
        fixture.License(current, "new");
        await fixture.Db.SaveChangesAsync();
        var stats = await fixture.Service.GetAsync("Nevada", bulkOnly: true);
        Assert.Equal(previous.Id, stats.Baseline!.Id);
        Assert.Equal(current.Id, stats.Incoming!.Id);
        Assert.Equal(new[] { "New license", "Not observed", "Other updates", "Unchanged" },
            stats.Records.Select(row => row.Classification).OrderBy(value => value).ToArray());
    }

    [Fact]
    public async Task BulkStatisticsCompareCurrentSynchronizedRosterWithPreviousVersion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var previous = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 1);
        var current = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 2);
        current.ParserVersion = FloridaBulkImportService.ParserVersion;
        var legacy = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 3);
        fixture.License(previous, "one", status: "Active");
        fixture.License(previous, "missing");
        fixture.License(current, "one", status: "Inactive");
        fixture.License(current, "new");
        fixture.License(legacy, "legacy");
        await fixture.Db.SaveChangesAsync();
        var stats = await fixture.Service.GetAsync("Florida", current.Id, bulkOnly: true);
        Assert.Equal(current.Id, stats.Incoming!.Id);
        Assert.Equal(previous.Id, stats.Baseline!.Id);
        Assert.Empty(stats.PendingSnapshots);
        Assert.Single(stats.Records, row => row.Classification == "Status changed");
        Assert.Single(stats.Records, row => row.Classification == "Not observed");
        Assert.Single(stats.Records, row => row.Classification == "New license");
        Assert.DoesNotContain(stats.Records, row => row.LicenseNumber == "LEGACY");
    }

    [Fact]
    public async Task RevealsOffsettingStatusChanges_WhenSnapshotCountsAreEqual()
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 1);
        var incoming = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 2);
        fixture.License(baseline, "one", status: "Active");
        fixture.License(incoming, "one", status: "Inactive");
        fixture.License(baseline, "two", status: "Inactive");
        fixture.License(incoming, "two", status: "Active");
        await fixture.Db.SaveChangesAsync();
        var stats = await fixture.Service.GetAsync("Florida");
        Assert.All(stats.Statuses, row =>
        {
            Assert.Equal(row.Current, row.Collected);
            Assert.Equal(1, row.Entered);
            Assert.Equal(1, row.Left);
            Assert.Equal(0, row.NewLicenses);
        });
        Assert.Equal(2, stats.Records.Count(row => row.Classification == "Status changed"));
    }

    [Fact]
    public async Task ComparesLatestApprovedWithPending_WithoutTreatingUnobservedRecordsAsRemoved()
    {
        await using var fixture = await Fixture.CreateAsync();
        var old = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 1);
        fixture.License(old, "old-only");
        var baseline = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 2);
        fixture.License(baseline, "unchanged", name: " Person ");
        fixture.License(baseline, "changed", status: "Active");
        fixture.License(baseline, "not-yet-collected");
        var pending = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 3);
        fixture.License(pending, "UNCHANGED", name: "PERSON");
        fixture.License(pending, "changed", status: "Inactive");
        fixture.License(pending, "new");
        var rejected = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Rejected, 4);
        fixture.License(rejected, "rejected");
        var other = fixture.Snapshot("Idaho", RegulatorySnapshotStatuses.PendingReview, 5);
        fixture.License(other, "other-state");
        await fixture.Db.SaveChangesAsync();

        var stats = await fixture.Service.GetAsync("Florida");

        Assert.Equal(baseline.Id, stats.Baseline!.Id);
        Assert.Equal(pending.Id, stats.Incoming!.Id);
        Assert.Single(stats.PendingSnapshots);
        var row = Assert.Single(stats.Professions);
        Assert.Equal((3, 3, 1, 1, 1), (row.Current, row.Collected, row.New, row.Changed, row.Unchanged));
        Assert.Equal(3, stats.Statuses.Sum(row => row.Current));
        Assert.Equal(3, stats.Statuses.Sum(row => row.Collected));
        Assert.Equal(1, Assert.Single(stats.Statuses, row => row.Status == "Inactive").Collected);
        Assert.Equal(1, Assert.Single(stats.Statuses, row => row.Status == "Active").Left);
        Assert.Equal(1, Assert.Single(stats.Statuses, row => row.Status == "Active").NewLicenses);
        Assert.Equal(1, Assert.Single(stats.Statuses, row => row.Status == "Inactive").Entered);
        Assert.Equal(1, stats.Statuses.Sum(row => row.Entered));
        Assert.Equal(1, stats.Statuses.Sum(row => row.Left));
        Assert.Equal("Not observed", Assert.Single(stats.Records, row => row.LicenseNumber == "NOT-YET-COLLECTED").Classification);
        var transition = Assert.Single(stats.Records, row => row.Classification == "Status changed");
        Assert.Contains(transition.Changes, change => change.Field == "Status" && change.Previous == "ACTIVE" && change.Incoming == "INACTIVE");
        Assert.Single(stats.Records, row => row.Classification == "New license");
        Assert.Single(stats.Records, row => row.Classification == "Unchanged");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, pending.Status);
    }

    [Fact]
    public async Task InitialCollectionHasEmptyBaseline_AndRefreshReadsNewlyStagedRecords()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Snapshot("Idaho", RegulatorySnapshotStatuses.PendingReview, 1);
        fixture.License(pending, "1");
        await fixture.Db.SaveChangesAsync();
        var first = await fixture.Service.GetAsync("Idaho");
        Assert.Null(first.Baseline);
        Assert.Equal(1, Assert.Single(first.Professions).New);

        fixture.License(pending, "2", type: "Instructor");
        await fixture.Db.SaveChangesAsync();
        var next = await fixture.Service.GetAsync("Idaho");
        Assert.Equal(2, next.Professions.Sum(row => row.New));
        Assert.Equal(2, next.Professions.Sum(row => row.Collected));
        Assert.Equal(0, next.Professions.Sum(row => row.Current));
    }

    [Fact]
    public async Task KeepsTypesAndFirmsDistinct_DeduplicatesIdenticalRecords_AndReportsBlankNumbers()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Snapshot("California", RegulatorySnapshotStatuses.PendingReview, 1);
        fixture.License(pending, "00001234", type: "Salesperson");
        fixture.License(pending, "00001234", type: "Officer");
        fixture.License(pending, " 00001234 ", type: " salesperson ");
        fixture.License(pending, "");
        fixture.Db.RegulatoryFirms.Add(new RegulatoryFirm
        {
            Snapshot = pending, State = pending.State, LicenseNumber = "00001234", LegalName = "Office"
        });
        await fixture.Db.SaveChangesAsync();

        var stats = await fixture.Service.GetAsync("California");

        Assert.Equal(3, stats.Professions.Count);
        Assert.Equal(3, stats.Professions.Sum(row => row.New));
        Assert.Equal(1, stats.ExcludedCollected);
        Assert.Contains(stats.Professions, row => row.Kind == "Firms / offices");
    }

    [Fact]
    public async Task ConflictingDuplicateIdentityIsAnExplicitError()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 1);
        fixture.License(pending, "1", status: "Active");
        fixture.License(pending, "1", status: "Inactive");
        await fixture.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetAsync("Florida"));
        Assert.Contains("conflicting", error.Message);
    }

    [Fact]
    public async Task DetectsDateRestrictionEducationAndFirmChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 1);
        var pending = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 2);
        foreach (var number in new[] { "date", "restriction", "education" })
        {
            fixture.License(baseline, number);
            var incoming = fixture.License(pending, number);
            if (number == "date") incoming.LicenseRenewalDate = new DateOnly(2028, 1, 1);
            if (number == "restriction") incoming.IsRestricted = true;
            if (number == "education") incoming.ElectiveHours = 4;
        }
        fixture.Db.RegulatoryFirms.AddRange(
            new RegulatoryFirm { Snapshot = baseline, State = baseline.State, LicenseNumber = "firm", LegalName = "Office", Address = "Old address" },
            new RegulatoryFirm { Snapshot = pending, State = pending.State, LicenseNumber = "firm", LegalName = "Office", Address = "New address" });
        await fixture.Db.SaveChangesAsync();

        var stats = await fixture.Service.GetAsync("Florida");
        Assert.Equal(4, stats.Professions.Sum(row => row.Changed));
        Assert.Equal(0, stats.Professions.Sum(row => row.New));
        Assert.Equal(0, stats.Professions.Sum(row => row.Unchanged));
        Assert.All(stats.Records, row => Assert.Equal("Other updates", row.Classification));
        Assert.Contains(stats.Records.SelectMany(row => row.Changes), change => change.Field == "Address");
    }

    [Fact]
    public async Task SupportsExplicitPendingSelection_ButRejectsWrongStateApprovedAndRejectedIds()
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 1);
        var latest = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 2);
        var approved = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 3);
        var rejected = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Rejected, 4);
        var other = fixture.Snapshot("Idaho", RegulatorySnapshotStatuses.PendingReview, 5);
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(latest.Id, (await fixture.Service.GetAsync("Florida")).Incoming!.Id);
        Assert.Equal(older.Id, (await fixture.Service.GetAsync("Florida", older.Id)).Incoming!.Id);
        foreach (var id in new[] { approved.Id, rejected.Id, other.Id, Guid.NewGuid() })
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetAsync("Florida", id));
    }

    [Fact]
    public async Task MatchesKnownFloridaCredentialCodes_WithoutLosingBrokerAssociateBreakdown()
    {
        await using var fixture = await Fixture.CreateAsync();
        var baseline = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 1);
        var pending = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 2);
        fixture.License(baseline, "SL123", type: "SL");
        fixture.License(pending, "SL123", type: "Sales Associate");
        fixture.License(baseline, "ZH123", type: "ZH");
        fixture.License(pending, "ZH123", type: "Real Estate Instructor");
        fixture.License(baseline, "BK123", type: "BK");
        fixture.License(pending, "BK123", type: "Broker Sales");
        await fixture.Db.SaveChangesAsync();

        var stats = await fixture.Service.GetAsync("Florida");

        Assert.Equal(0, stats.Professions.Sum(row => row.New));
        Assert.Equal(2, stats.Professions.Sum(row => row.Unchanged));
        Assert.Equal(1, Assert.Single(stats.Professions, row => row.Profession == "Broker Sales").Changed);
        Assert.Equal(1, Assert.Single(stats.Professions, row => row.Profession == "Broker").Current);
        Assert.Equal(3, stats.Professions.Sum(row => row.Collected));
    }

    [Fact]
    public async Task OffsetSnapshotTimestampsAreUtc_AndSortByInstantNotSqliteStringFormat()
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 1);
        var newer = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.PendingReview, 2);
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE RegulatoryDataSnapshots SET RetrievedAtUtc = {"2026-10-30T13:54:00.620099+00:00"} WHERE Id = {older.Id}");
        await fixture.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE RegulatoryDataSnapshots SET RetrievedAtUtc = {"2026-10-30 15:00:00"} WHERE Id = {newer.Id}");

        var stats = await fixture.Service.GetAsync("Florida", older.Id);

        Assert.Equal(newer.Id, stats.PendingSnapshots[0].Id);
        Assert.Equal(DateTimeKind.Utc, stats.Incoming!.RetrievedAtUtc.Kind);
        Assert.Equal(13, stats.Incoming.RetrievedAtUtc.Hour);
        Assert.Equal(54, stats.Incoming.RetrievedAtUtc.Minute);
    }

    [Fact]
    public async Task StatesIncludeAllStatesAndPendingAdditionalJurisdictions_EmptyStateHasNoIncoming()
    {
        await using var fixture = await Fixture.CreateAsync();
        var approved = fixture.Snapshot("Florida", RegulatorySnapshotStatuses.Approved, 1);
        fixture.License(approved, "1");
        fixture.Snapshot("Puerto Rico", RegulatorySnapshotStatuses.PendingReview, 2);
        await fixture.Db.SaveChangesAsync();

        var states = await fixture.Service.GetStatesAsync();
        Assert.Contains("Alabama", states);
        Assert.Contains("Wyoming", states);
        Assert.Contains("Puerto Rico", states);
        Assert.Equal(1, states.Count(state => state == "Florida"));
        var empty = await fixture.Service.GetAsync("Idaho");
        Assert.Null(empty.Baseline);
        Assert.Null(empty.Incoming);
        Assert.Empty(empty.Professions);
        var current = await fixture.Service.GetAsync("Florida");
        Assert.Null(current.Incoming);
        Assert.Equal(1, Assert.Single(current.Professions).Current);
        Assert.Equal(0, Assert.Single(current.Professions).New);
    }

    private sealed class Fixture(SqliteConnection connection, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public RegulatoryStatisticsService Service { get; } = new(db);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new(connection, db);
        }

        public RegulatoryDataSnapshot Snapshot(string state, string status, int day)
        {
            var snapshot = new RegulatoryDataSnapshot
            {
                State = state, Status = status, SourceName = "Synthetic statistics test", SourceUrl = "https://example.invalid",
                RetrievedAtUtc = new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Utc)
            };
            Db.RegulatoryDataSnapshots.Add(snapshot);
            return snapshot;
        }

        public RegulatoryLicensee License(RegulatoryDataSnapshot snapshot, string number,
            string type = "Salesperson", string name = "Person", string status = "Active")
        {
            var record = new RegulatoryLicensee
            {
                Snapshot = snapshot, State = snapshot.State, LicenseNumber = number,
                LicenseType = type, Name = name, LicenseStatus = status
            };
            Db.RegulatoryLicensees.Add(record);
            return record;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
