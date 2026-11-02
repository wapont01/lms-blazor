using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class RegulatoryDirectoryServiceTests
{
    [Fact]
    public async Task RestorePreservesSourceHistoryIsAuditedAndNextSyncBecomesCurrent()
    {
        await using var f = await TestFixture.CreateAsync();
        using var archive = await BulkTestArchive.CreateAsync();
        var importer = new FloridaBulkImportService(f.DbContext);
        var originalId = await importer.ImportArchiveAsync(archive.Path);
        var acquired = await f.DbContext.RegulatoryDataSnapshots.Where(s => s.Id == originalId).Select(s => s.RetrievedAtUtc).SingleAsync();
        var newer = new RegulatoryDataSnapshot
        {
            State = "Florida", SourceName = "Newer synthetic bulk", Status = RegulatorySnapshotStatuses.Approved,
            ParserVersion = FloridaBulkImportService.ParserVersion, ActivatedAtUtc = DateTime.UtcNow,
            RetrievedAtUtc = DateTime.UtcNow
        };
        f.DbContext.RegulatoryLicensees.Add(new RegulatoryLicensee
        {
            Snapshot = newer, State = "Florida", Name = "Newer person", LicenseNumber = "SL2", LicenseType = "Sales Associate"
        });
        await f.DbContext.SaveChangesAsync();
        var admin = await f.DbContext.UserAccounts.FirstAsync(u => u.Role == "Admin");
        var learner = await f.DbContext.UserAccounts.FirstAsync(u => u.Role == "Learner");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RestoreFloridaRosterAsync(originalId, newer.Id, learner.Id, "Unauthorized"));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.RestoreFloridaRosterAsync(originalId, newer.Id, admin.Id, " "));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RestoreFloridaRosterAsync(originalId, Guid.NewGuid(), admin.Id, "Stale"));
        await f.Service.RestoreFloridaRosterAsync(originalId, newer.Id, admin.Id, "Recover previous roster");
        Assert.Equal(originalId, (await f.Service.GetLatestApprovedSnapshotAsync("Florida"))!.Id);
        Assert.Equal(acquired, await f.DbContext.RegulatoryDataSnapshots.Where(s => s.Id == originalId).Select(s => s.RetrievedAtUtc).SingleAsync());
        Assert.Equal(2, (await f.Service.GetFloridaRosterHistoryAsync()).Count);
        Assert.Contains(await f.DbContext.AuditLogs.ToListAsync(), log => log.Action == "regulatory-roster.restored" && log.Details!.Contains(newer.Id.ToString()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RestoreFloridaRosterAsync(originalId, originalId, admin.Id, "Already current"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RestoreFloridaRosterAsync(Guid.NewGuid(), originalId, admin.Id, "Missing"));
        await importer.ImportArchiveAsync(archive.Path);
        Assert.Equal(2, await f.DbContext.RegulatoryDataSnapshots.CountAsync());
        await f.Service.RestoreFloridaRosterAsync(newer.Id, originalId, admin.Id, "Select alternate version");
        await importer.ImportArchiveAsync(archive.Path);
        Assert.Equal(originalId, (await f.Service.GetLatestApprovedSnapshotAsync("Florida"))!.Id);
        Assert.Equal(2, await f.DbContext.RegulatoryDataSnapshots.CountAsync());
        var stats = await new RegulatoryStatisticsService(f.DbContext).GetAsync("Florida", bulkOnly: true);
        Assert.Equal(originalId, stats.Incoming!.Id);
        Assert.Equal(newer.Id, stats.Baseline!.Id);
    }

    [Fact]
    public async Task FloridaDirectoryUsesOnlyLatestActivatedRosterWhilePreservingHistory()
    {
        await using var fixture = await TestFixture.CreateAsync();
        foreach (var day in new[] { 1, 2 })
        {
            var snapshot = new RegulatoryDataSnapshot
            {
                State = "Florida", SourceName = $"Roster {day}", Status = RegulatorySnapshotStatuses.Approved,
                RetrievedAtUtc = new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Utc),
                ParserVersion = FloridaBulkImportService.ParserVersion
            };
            fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
            fixture.DbContext.RegulatoryLicensees.Add(new RegulatoryLicensee
            {
                Snapshot = snapshot, State = "Florida", LicenseNumber = "SL1", Name = $"Person {day}"
            });
            fixture.DbContext.RegulatoryFirms.Add(new RegulatoryFirm
            {
                Snapshot = snapshot, State = "Florida", LicenseNumber = "CQ1", LegalName = $"Firm {day}"
            });
        }
        await fixture.DbContext.SaveChangesAsync();
        var licenses = await fixture.Service.GetApprovedLicenseesAsync("Florida");
        Assert.Equal("Person 2", Assert.Single(licenses).Name);
        var firms = await fixture.Service.GetCampaignFirmTargetsAsync("Florida");
        Assert.Equal("Firm 2", Assert.Single(firms).FirmName);
        Assert.Equal(2, await fixture.DbContext.RegulatoryLicensees.CountAsync(l => l.State == "Florida"));
    }

    [Fact]
    public async Task GetCampaignStateOptionsAsync_ReturnsAllUsStatesAndApprovedAdditionalJurisdictions()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.RegulatoryDataSnapshots.AddRange(
            new RegulatoryDataSnapshot { State = "Puerto Rico", SourceName = "Puerto Rico source", SourceUrl = "https://example.test/pr", Status = RegulatorySnapshotStatuses.Approved },
            new RegulatoryDataSnapshot { State = "American Samoa", SourceName = "American Samoa pending", SourceUrl = "https://example.test/as", Status = RegulatorySnapshotStatuses.PendingReview });
        await fixture.DbContext.SaveChangesAsync();

        var states = await fixture.Service.GetCampaignStateOptionsAsync();

        Assert.Equal(1, states.Count(state => state == "Texas"));
        Assert.Contains("Alabama", states);
        Assert.Contains("Wyoming", states);
        Assert.Contains("Puerto Rico", states);
        Assert.DoesNotContain("American Samoa", states);
    }

    [Fact]
    public async Task TryAutoLinkLicenseAsync_LeavesAmbiguousLicenseNumberUnlinked_ThenUsesMatchingLicenseType()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var user = await fixture.DbContext.UserAccounts.FirstAsync();
        user.LicenseType = null;
        user.LicenseStatus = null;
        user.InitialLicensureDate = null;
        user.LicenseRenewalDueDate = null;

        var snapshot = new RegulatoryDataSnapshot
        {
            State = "California",
            SourceName = "California DRE education information only",
            SourceUrl = "https://dre.ca.gov/Licensees/ExamineeLicenseeListDataFiles.html",
            RetrievedAtUtc = new DateTime(2026, 10, 10, 0, 32, 13, DateTimeKind.Utc),
            Status = RegulatorySnapshotStatuses.Approved
        };
        var salesperson = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "California",
            LicenseNumber = "00001234",
            Name = "Jane Doe",
            LicenseType = "Salesperson"
        };
        var officer = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "California",
            LicenseNumber = "00001234",
            Name = "Jane Doe",
            LicenseType = "Officer"
        };
        fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
        fixture.DbContext.RegulatoryLicensees.AddRange(salesperson, officer);
        await fixture.DbContext.SaveChangesAsync();

        Assert.False(await fixture.Service.TryAutoLinkLicenseAsync(user.Id, "California", "00001234"));
        Assert.False(await fixture.DbContext.UserRegulatoryLicenseLinks.AnyAsync(link => link.UserAccountId == user.Id));

        user.LicenseType = "Officer";
        await fixture.DbContext.SaveChangesAsync();
        Assert.True(await fixture.Service.TryAutoLinkLicenseAsync(user.Id, "California", "00001234"));

        var link = await fixture.DbContext.UserRegulatoryLicenseLinks.SingleAsync(candidate => candidate.UserAccountId == user.Id);
        Assert.Equal(officer.Id, link.RegulatoryLicenseeId);
    }

    [Fact]
    public async Task RegulatoryComplianceService_UsesApprovedLinkAndEffectiveStatePolicies()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "North Carolina",
            SourceName = "NCREC",
            SourceUrl = "https://license.ncrec.gov/example",
            RetrievedAtUtc = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc)
        };
        var licensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "North Carolina",
            LicenseNumber = "363756",
            Name = "BREZEAULT, REMY YANICK ISAAC",
            LicenseType = "BROKER",
            LicenseLevel = "PB",
            LicenseStatus = "ACTIVE",
            LicenseRenewalDate = new DateOnly(2026, 5, 26)
        };
        fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
        fixture.DbContext.RegulatoryLicensees.Add(licensee);
        fixture.DbContext.CourseEligibilityPolicies.AddRange(
            new CourseEligibilityPolicy
            {
                State = "North Carolina",
                LicenseLevel = "PB",
                ComplianceType = CourseComplianceTypes.Postlicensing,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EligibilityMessage = "North Carolina postlicensing courses are available for provisional brokers."
            },
            new CourseEligibilityPolicy
            {
                State = "North Carolina",
                LicenseLevel = "B",
                ComplianceType = CourseComplianceTypes.ContinuingEducation,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EligibilityMessage = "Broker-only continuing education."
            });
        await fixture.DbContext.SaveChangesAsync();

        Assert.False(await fixture.ComplianceService.LinkUserToApprovedLicenseAsync(learner.Id, licensee.Id, admin.Id, admin.Email));

        snapshot.Status = RegulatorySnapshotStatuses.Approved;
        await fixture.DbContext.SaveChangesAsync();
        Assert.True(await fixture.ComplianceService.LinkUserToApprovedLicenseAsync(learner.Id, licensee.Id, admin.Id, admin.Email));

        var status = await fixture.ComplianceService.GetUserComplianceStatusAsync(learner.Id, new DateOnly(2026, 8, 28));

        Assert.NotNull(status);
        Assert.Equal("363756", status!.LicenseNumber);
        Assert.Equal("PB", status.LicenseLevel);
        Assert.Single(status.RecommendedCourseMessages);
        Assert.Contains("postlicensing", status.RecommendedCourseMessages[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCampaignFirmTargetsAsync_RanksApprovedFirmsByUpcomingRenewals()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "Texas",
            SourceName = "TREC sample",
            SourceUrl = "https://www.trec.texas.gov/campaign-test",
            RetrievedAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            Status = RegulatorySnapshotStatuses.Approved
        };
        var soonFirm = new RegulatoryFirm
        {
            Snapshot = snapshot,
            State = "Texas",
            LicenseNumber = "9000001-BB",
            LegalName = "Soon Renewal Realty",
            QualifyingBrokerName = "Casey Broker",
            LicenseRenewalDate = today.AddMonths(1)
        };
        var laterFirm = new RegulatoryFirm
        {
            Snapshot = snapshot,
            State = "Texas",
            LicenseNumber = "9000002-BB",
            LegalName = "Later Renewal Realty"
        };
        var soonAgent = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "Texas",
            LicenseNumber = "100001-SA",
            Name = "Amy Agent",
            LicenseRenewalDate = today.AddMonths(2)
        };
        var laterBroker = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "Texas",
            LicenseNumber = "100002-B",
            Name = "Blake Broker",
            LicenseRenewalDate = today.AddMonths(20)
        };
        fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
        fixture.DbContext.RegulatoryFirms.AddRange(soonFirm, laterFirm);
        fixture.DbContext.RegulatoryLicensees.AddRange(soonAgent, laterBroker);
        fixture.DbContext.RegulatoryFirmLicenseeAffiliations.AddRange(
            new RegulatoryFirmLicenseeAffiliation { Snapshot = snapshot, Firm = soonFirm, Licensee = soonAgent },
            new RegulatoryFirmLicenseeAffiliation { Snapshot = snapshot, Firm = laterFirm, Licensee = laterBroker, IsBrokerInCharge = true });
        await fixture.DbContext.SaveChangesAsync();

        var targets = await fixture.Service.GetCampaignFirmTargetsAsync("Texas");

        Assert.Equal(2, targets.Count);
        Assert.Equal("Soon Renewal Realty", targets[0].FirmName);
        Assert.Equal("Casey Broker", targets[0].BrokerName);
        Assert.Equal(today.AddMonths(1), targets[0].NextRenewalDate);
        Assert.Equal(2, targets[0].RenewalsWithinTwelveMonths);
        Assert.Equal("Blake Broker", targets[1].BrokerName);
        Assert.Equal(0, targets[1].RenewalsWithinTwelveMonths);
    }

    [Fact]
    public async Task GetCampaignFirmTargetsAsync_DerivesWestVirginiaRenewalsOnlyWhenRosterDateIsMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.True(StateLicenseRenewalPolicyCatalog.TryCalculateNextRenewalDate(
            "West Virginia", "Salesperson", today, out var derivedDate));

        var snapshot = new RegulatoryDataSnapshot
        {
            State = "West Virginia",
            SourceName = "West Virginia licensing sample",
            SourceUrl = "https://rec.wv.gov/campaign-test",
            RetrievedAtUtc = DateTime.UtcNow,
            Status = RegulatorySnapshotStatuses.Approved
        };
        var derivedFirm = new RegulatoryFirm
        {
            Snapshot = snapshot,
            State = "West Virginia",
            LicenseNumber = "WV-FIRM-DERIVED",
            LegalName = "Derived Renewal Realty"
        };
        var authoritativeFirm = new RegulatoryFirm
        {
            Snapshot = snapshot,
            State = "West Virginia",
            LicenseNumber = "WV-FIRM-AUTHORITATIVE",
            LegalName = "Roster Renewal Realty"
        };
        var unknownFirm = new RegulatoryFirm
        {
            Snapshot = snapshot,
            State = "West Virginia",
            LicenseNumber = "WV-FIRM-UNKNOWN",
            LegalName = "Unknown Renewal Realty"
        };
        var dualCategoryFirm = new RegulatoryFirm
        {
            Snapshot = snapshot,
            State = "West Virginia",
            LicenseNumber = "WV-FIRM-DUAL-CATEGORY",
            LegalName = "Dual Category Realty"
        };
        var derivedLicensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "West Virginia",
            LicenseNumber = "WV-SALESPERSON-1",
            Name = "Derived Licensee",
            LicenseType = "Salesperson"
        };
        var authoritativeLicensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "West Virginia",
            LicenseNumber = "WV-BROKER-1",
            Name = "Roster Licensee",
            LicenseType = "Broker",
            LicenseRenewalDate = today.AddDays(5)
        };
        var unknownLicensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "West Virginia",
            LicenseNumber = "WV-UNKNOWN-1",
            Name = "Unclassified Licensee",
            LicenseType = "Unclassified"
        };
        var dualCategoryLicensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "West Virginia",
            LicenseNumber = "WV-DUAL-CATEGORY-1",
            Name = "Dual Category Licensee",
            LicenseType = "Broker, Associate Broker",
            LicenseLevel = "Broker"
        };

        fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
        fixture.DbContext.RegulatoryFirms.AddRange(derivedFirm, authoritativeFirm, unknownFirm, dualCategoryFirm);
        fixture.DbContext.RegulatoryLicensees.AddRange(derivedLicensee, authoritativeLicensee, unknownLicensee, dualCategoryLicensee);
        fixture.DbContext.RegulatoryFirmLicenseeAffiliations.AddRange(
            new RegulatoryFirmLicenseeAffiliation { Snapshot = snapshot, Firm = derivedFirm, Licensee = derivedLicensee },
            new RegulatoryFirmLicenseeAffiliation { Snapshot = snapshot, Firm = authoritativeFirm, Licensee = authoritativeLicensee },
            new RegulatoryFirmLicenseeAffiliation { Snapshot = snapshot, Firm = unknownFirm, Licensee = unknownLicensee },
            new RegulatoryFirmLicenseeAffiliation { Snapshot = snapshot, Firm = dualCategoryFirm, Licensee = dualCategoryLicensee });
        await fixture.DbContext.SaveChangesAsync();

        var targets = await fixture.Service.GetCampaignFirmTargetsAsync("West Virginia");
        var derivedTarget = Assert.Single(targets, target => target.FirmName == derivedFirm.LegalName);
        var authoritativeTarget = Assert.Single(targets, target => target.FirmName == authoritativeFirm.LegalName);
        var unknownTarget = Assert.Single(targets, target => target.FirmName == unknownFirm.LegalName);
        var dualCategoryTarget = Assert.Single(targets, target => target.FirmName == dualCategoryFirm.LegalName);

        Assert.Equal(derivedDate, derivedTarget.NextRenewalDate);
        Assert.Equal(LicenseDateProvenance.Derived, derivedTarget.RenewalDateSource);
        Assert.Equal(derivedDate <= today.AddYears(1) ? 1 : 0, derivedTarget.RenewalsWithinTwelveMonths);
        Assert.Equal(authoritativeLicensee.LicenseRenewalDate, authoritativeTarget.NextRenewalDate);
        Assert.Equal(LicenseDateProvenance.Authoritative, authoritativeTarget.RenewalDateSource);
        Assert.Null(unknownTarget.NextRenewalDate);
        Assert.Null(unknownTarget.RenewalDateSource);
        Assert.Equal(0, unknownTarget.RenewalsWithinTwelveMonths);
        Assert.Equal(derivedDate, dualCategoryTarget.NextRenewalDate);
        Assert.Equal(LicenseDateProvenance.Derived, dualCategoryTarget.RenewalDateSource);
    }

    [Fact]
    public async Task GetCampaignFirmTargetsAsync_ReturnsAllWestVirginiaBranchesForCsvSizedRequests()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "West Virginia",
            SourceName = "West Virginia active roster",
            SourceUrl = "https://rec.wv.gov/campaign-export-test",
            RetrievedAtUtc = DateTime.UtcNow,
            Status = RegulatorySnapshotStatuses.Approved
        };
        var firms = Enumerable.Range(1, 2_006)
            .Select(index => new RegulatoryFirm
            {
                Snapshot = snapshot,
                State = "West Virginia",
                LicenseNumber = $"{index:000000}-00",
                LegalName = $"West Virginia Branch {index}"
            })
            .ToList();

        fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
        fixture.DbContext.RegulatoryFirms.AddRange(firms);
        await fixture.DbContext.SaveChangesAsync();

        var targets = await fixture.Service.GetCampaignFirmTargetsAsync("West Virginia", take: 10_000);

        Assert.Equal(2_006, targets.Count);
        Assert.All(targets, target => Assert.Equal("West Virginia", target.State));
    }

    [Fact]
    public async Task ImportAsync_PersistsPendingSnapshotAndApprovalMakesItCurrent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");

        var snapshot = await fixture.Service.ImportAsync(new RegulatorySnapshotImport(
            "North Carolina",
            "North Carolina Real Estate Commission",
            "https://license.ncrec.gov/ncrec/oecgi3.exe/O4W_BROKERS_BY_FIRM_NEW",
            new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            AcquisitionCost: 12.50m,
            ParserVersion: "ncrec-bic-v1",
            Firms:
            [
                new RegulatoryFirmImport(
                    "C20134",
                    "5 POINTS REALTY GROUP LLC",
                    "5 POINTS REALTY",
                    "ACTIVE",
                    "2200 THE PLAZA, CHARLOTTE, NC 28205-3036",
                    "DONEY, MICHAEL JASON",
                    [
                        new RegulatoryLicenseeImport(
                            "250791",
                            "WILSON JR, EDWIN PEEBLES",
                            "BROKER",
                            "B",
                            "ACTIVE",
                            new DateOnly(2026, 6, 15),
                            4,
                            4,
                            null,
                            true,
                            true,
                            null),
                        new RegulatoryLicenseeImport(
                            "363756",
                            "BREZEAULT, REMY YANICK ISAAC",
                            "BROKER",
                            "PB",
                            "ACTIVE",
                            new DateOnly(2026, 5, 26),
                            null,
                            null,
                            null,
                            false,
                            false,
                            "WILSON JR, EDWIN PEEBLES")
                    ])
            ]));

        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(1, await fixture.DbContext.RegulatoryFirms.CountAsync());
        Assert.Equal(2, await fixture.DbContext.RegulatoryLicensees.CountAsync());
        Assert.Equal(2, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync());

        var approved = await fixture.Service.ApproveAsync(snapshot.Id, admin.Id, admin.Email);
        var latest = await fixture.Service.GetLatestApprovedSnapshotAsync("North Carolina");

        Assert.True(approved);
        Assert.NotNull(latest);
        Assert.Equal(snapshot.Id, latest!.Id);
        Assert.Equal(RegulatorySnapshotStatuses.Approved, latest.Status);
        Assert.Equal(12.50m, latest.AcquisitionCost);
        var summary = Assert.Single(await fixture.Service.GetRecentSnapshotsAsync());
        Assert.Equal(snapshot.Id, summary.SnapshotId);
        Assert.Equal(1, summary.FirmCount);
        Assert.Equal(2, summary.LicenseeCount);
        Assert.Contains(await fixture.DbContext.AuditLogs.ToListAsync(), entry => entry.Action == "regulatory-snapshot.approved");
    }

    [Fact]
    public async Task ApproveAsync_AllowsLicenseeOnlySnapshotsButRejectsEmptySnapshots()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var licenseeOnlySnapshot = new RegulatoryDataSnapshot
        {
            State = "Nevada",
            SourceName = "NRED active licensees",
            SourceUrl = "https://red.nv.gov/",
            RetrievedAtUtc = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc),
            Status = RegulatorySnapshotStatuses.PendingReview
        };
        licenseeOnlySnapshot.Licensees.Add(new RegulatoryLicensee
        {
            Snapshot = licenseeOnlySnapshot,
            State = "Nevada",
            LicenseNumber = "S.000001",
            Name = "TEST LICENSEE",
            LicenseType = "Salesperson",
            LicenseStatus = "Active"
        });
        var emptySnapshot = new RegulatoryDataSnapshot
        {
            State = "Empty",
            SourceName = "Empty capture",
            SourceUrl = "https://example.test/empty",
            RetrievedAtUtc = DateTime.UtcNow,
            Status = RegulatorySnapshotStatuses.PendingReview
        };
        fixture.DbContext.RegulatoryDataSnapshots.AddRange(licenseeOnlySnapshot, emptySnapshot);
        await fixture.DbContext.SaveChangesAsync();

        Assert.True(await fixture.Service.ApproveAsync(licenseeOnlySnapshot.Id, admin.Id, admin.Email));
        Assert.False(await fixture.Service.ApproveAsync(emptySnapshot.Id, admin.Id, admin.Email));
        Assert.Equal(RegulatorySnapshotStatuses.Approved, licenseeOnlySnapshot.Status);
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, emptySnapshot.Status);
    }

    [Fact]
    public async Task LinkLicenseeToUserAsync_CopiesVerifiedLicenseFactsAndAuditsLink()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "sofia.martinez.fl@wlr.com");
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "Florida",
            SourceName = "DBPR",
            SourceUrl = "https://example.test/dbpr",
            RetrievedAtUtc = DateTime.UtcNow,
            Status = RegulatorySnapshotStatuses.Approved
        };
        var licensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "Florida",
            LicenseNumber = "SL999999",
            Name = "SOFIA MARTINEZ",
            LicenseType = "SL",
            LicenseStatus = "Current",
            LicenseIssuedDate = new DateOnly(2025, 4, 1),
            LicenseRenewalDate = new DateOnly(2027, 3, 31)
        };
        fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
        fixture.DbContext.RegulatoryLicensees.Add(licensee);
        await fixture.DbContext.SaveChangesAsync();

        Assert.True(await fixture.Service.LinkLicenseeToUserAsync(learner.Id, licensee.Id, admin.Id, admin.Email));

        var linkedUser = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Id == learner.Id);
        Assert.Equal("SL999999", linkedUser.LicenseNumber);
        Assert.Equal("Florida", linkedUser.State);
        Assert.Equal(new DateTime(2025, 4, 1), linkedUser.InitialLicensureDate);
        Assert.Equal(new DateTime(2027, 3, 31), linkedUser.LicenseRenewalDueDate);
        Assert.Contains(await fixture.DbContext.AuditLogs.ToListAsync(), entry => entry.Action == "regulatory-license.linked");
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IRegulatoryDirectoryService Service { get; }
        public IRegulatoryComplianceService ComplianceService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IRegulatoryDirectoryService service, IRegulatoryComplianceService complianceService)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = service;
            ComplianceService = complianceService;
        }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var dbContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options);
            await dbContext.Database.EnsureCreatedAsync();

            var auditLogService = new AuditLogService(dbContext);
            var userAccountService = new UserAccountService(dbContext, auditLogService);
            await userAccountService.EnsureSeedUsersAsync();
            return new TestFixture(
                connection,
                dbContext,
                new RegulatoryDirectoryService(dbContext, auditLogService),
                new RegulatoryComplianceService(dbContext, auditLogService));
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}