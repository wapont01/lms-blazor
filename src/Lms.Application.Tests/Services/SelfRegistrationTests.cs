using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class SelfRegistrationTests
{
    [Fact]
    public async Task RegisterLearnerAsync_CreatesActiveLearner_WithoutAdminIntervention()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var user = await fixture.UserAccountService.RegisterLearnerAsync(
            "New.Agent@Example.com", "New Agent", "NewAgent123!", "North Carolina", "999999");

        Assert.Equal("new.agent@example.com", user.Email);
        Assert.Equal("Learner", user.Role);
        Assert.True(user.IsActive);
        Assert.Equal("North Carolina", user.State);
        Assert.Equal("999999", user.LicenseNumber);

        var login = await fixture.UserAccountService.AuthenticateAsync("new.agent@example.com", "NewAgent123!");
        Assert.Equal(LoginStatus.Succeeded, login.Status);

        Assert.True(await fixture.DbContext.AuditLogs.AnyAsync(entry =>
            entry.Action == "user.self-registered" && entry.TargetId == user.Id));
    }

    [Fact]
    public async Task RegisterLearnerAsync_RecordsMarketingOptOut_AndPreferenceCanBeToggled()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var user = await fixture.UserAccountService.RegisterLearnerAsync(
            "quiet.agent@example.com", "Quiet Agent", "QuietAgent12!", marketingOptIn: false);
        Assert.NotNull(user.MarketingEmailOptOutAtUtc);

        await fixture.UserAccountService.SetMarketingEmailPreferenceAsync(user.Id, optIn: true);
        var refreshed = await fixture.DbContext.UserAccounts.SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Null(refreshed.MarketingEmailOptOutAtUtc);

        await fixture.UserAccountService.SetMarketingEmailPreferenceAsync(user.Id, optIn: false);
        refreshed = await fixture.DbContext.UserAccounts.SingleAsync(candidate => candidate.Id == user.Id);
        Assert.NotNull(refreshed.MarketingEmailOptOutAtUtc);

        Assert.True(await fixture.DbContext.AuditLogs.AnyAsync(entry =>
            entry.Action == "user.marketing-preference.updated" && entry.TargetId == user.Id));
    }

    [Fact]
    public async Task TryAutoLinkLicenseAsync_LinksVerifiedRecord_WhenLicenseMatchesApprovedSnapshot()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "North Carolina",
            SourceName = "NCREC",
            SourceUrl = "https://license.ncrec.gov/example",
            RetrievedAtUtc = new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc),
            Status = RegulatorySnapshotStatuses.Approved
        };
        var licensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "North Carolina",
            LicenseNumber = "368498",
            Name = "MARTINEZ SORIANO, JAZMIN",
            LicenseType = "BROKER",
            LicenseLevel = "PB",
            LicenseStatus = "ACTIVE",
            LicenseIssuedDate = new DateOnly(2026, 9, 16)
        };
        fixture.DbContext.RegulatoryDataSnapshots.Add(snapshot);
        fixture.DbContext.RegulatoryLicensees.Add(licensee);
        await fixture.DbContext.SaveChangesAsync();

        var user = await fixture.UserAccountService.RegisterLearnerAsync(
            "jazmin.martinez@example.com", "Jazmin Martinez", "JazminPb123!", "North Carolina", "368498");

        Assert.True(await fixture.DirectoryService.TryAutoLinkLicenseAsync(user.Id, "North Carolina", "368498"));

        var refreshed = await fixture.DbContext.UserAccounts.SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("BROKER", refreshed.LicenseType);
        Assert.Equal("ACTIVE", refreshed.LicenseStatus);
        Assert.Equal(new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc), refreshed.InitialLicensureDate);

        var link = await fixture.DbContext.UserRegulatoryLicenseLinks.SingleAsync(candidate => candidate.UserAccountId == user.Id);
        Assert.Equal(licensee.Id, link.RegulatoryLicenseeId);
        Assert.Null(link.VerifiedByUserId);

        Assert.True(await fixture.DbContext.AuditLogs.AnyAsync(entry =>
            entry.Action == "regulatory-license.auto-linked" && entry.TargetId == user.Id));
    }

    [Fact]
    public async Task TryAutoLinkLicenseAsync_LeavesAccountUnlinkedAndUsable_WhenNoMatch()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var user = await fixture.UserAccountService.RegisterLearnerAsync(
            "no.match@example.com", "No Match", "NoMatch1234!", "North Carolina", "000000");

        Assert.False(await fixture.DirectoryService.TryAutoLinkLicenseAsync(user.Id, "North Carolina", "000000"));
        Assert.False(await fixture.DirectoryService.TryAutoLinkLicenseAsync(user.Id, null, null));

        Assert.False(await fixture.DbContext.UserRegulatoryLicenseLinks.AnyAsync(candidate => candidate.UserAccountId == user.Id));

        // self-reported values stay, account remains fully usable
        var refreshed = await fixture.DbContext.UserAccounts.SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("000000", refreshed.LicenseNumber);
        Assert.True(refreshed.IsActive);
        var login = await fixture.UserAccountService.AuthenticateAsync("no.match@example.com", "NoMatch1234!");
        Assert.Equal(LoginStatus.Succeeded, login.Status);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IUserAccountService UserAccountService { get; }
        public IRegulatoryDirectoryService DirectoryService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IUserAccountService userAccountService, IRegulatoryDirectoryService directoryService)
        {
            _connection = connection;
            DbContext = dbContext;
            UserAccountService = userAccountService;
            DirectoryService = directoryService;
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
            return new TestFixture(
                connection,
                dbContext,
                new UserAccountService(dbContext, auditLogService),
                new RegulatoryDirectoryService(dbContext, auditLogService));
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
