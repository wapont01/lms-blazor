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
    public async Task RegulatoryComplianceService_UsesApprovedLinkAndEffectiveStatePolicies()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.learner@wlr.com");
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
    public async Task ImportAsync_PersistsPendingSnapshotAndApprovalMakesItCurrent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");

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
    public async Task LinkLicenseeToUserAsync_CopiesVerifiedLicenseFactsAndAuditsLink()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
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