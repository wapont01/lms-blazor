using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public class StateRosterCaptureSeedTests
{
    [Fact]
    public async Task SeedAsync_ImportsNewYorkBeaconCapture_AndIsIdempotent()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "New York");

        Assert.Equal(RegulatorySnapshotStatuses.Approved, snapshot.Status);
        Assert.Equal(25, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.InRange(await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id), 90, 100);

        var agosta = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "10401229289");
        Assert.Equal("Salesperson", licenseeLevel(agosta));
        Assert.Equal(new DateOnly(2027, 4, 5), agosta.LicenseRenewalDate);

        var agostaAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id && affiliation.LicenseeId == agosta.Id);
        Assert.Contains("Berkshire Hathaway", agostaAffiliation.Firm!.LegalName, StringComparison.OrdinalIgnoreCase);

        var corporateBroker = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "31MI0887079");
        Assert.True(corporateBroker.IsBrokerInCharge);
        Assert.Equal("JONCAR REALTY INC", corporateBroker.Firm!.LegalName);

        static string? licenseeLevel(RegulatoryLicensee licensee) => licensee.LicenseLevel;
    }

    [Fact]
    public async Task SeedAsync_ImportsTexasTrecCapture_WithDesignatedBrokersAndSponsoredAgents()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "Texas");

        Assert.Equal(9, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));

        var elysian = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "9013710-BB");
        Assert.Contains("Del Roy", elysian.Address, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Aarant, Dave", elysian.QualifyingBrokerName);

        var designatedBroker = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "535139-B");
        Assert.True(designatedBroker.IsBrokerInCharge);

        var sponsoredAgent = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "736057-SA");
        Assert.Equal(new DateOnly(2028, 3, 31), sponsoredAgent.LicenseRenewalDate);

        var bobadillaAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "620392-SA");
        Assert.Equal("9014663-BB", bobadillaAffiliation.Firm!.LicenseNumber);
    }

    [Fact]
    public async Task SeedAsync_ImportsConnecticutFirmProfiles()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "Connecticut");

        Assert.Equal(9, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));

        var a1 = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "REB.0794798");
        Assert.Equal("ACTIVE", a1.LicenseStatus);
        Assert.Equal(new DateOnly(2023, 5, 22), a1.LicenseIssuedDate);
        Assert.Equal("JACQUELINE O JOHNSON", a1.QualifyingBrokerName);
    }

    [Fact]
    public async Task SeedAsync_ImportsIllinoisActiveSample_WithManagingBrokerContext()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "Illinois");

        Assert.Equal(2, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(3, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));

        var dawsonAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "475219323");
        Assert.Equal("481012055", dawsonAffiliation.Firm!.LicenseNumber);
        Assert.Equal("PATRICK J LOFTUS", dawsonAffiliation.BrokerInChargeName);
        Assert.Equal(new DateOnly(2028, 4, 30), dawsonAffiliation.Licensee!.LicenseRenewalDate);
    }

    [Fact]
    public async Task SeedAsync_ImportsArkansasAgentSample_AsLicenseesOnly()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "Arkansas");

        Assert.Equal(0, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(20, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));

        var grillo = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "PB00036246");
        Assert.Equal("Principal Broker", grillo.LicenseLevel);
        Assert.Equal(new DateOnly(2026, 12, 31), grillo.LicenseRenewalDate);

        var burton = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "EB00082129");
        Assert.Equal("Executive Broker", burton.LicenseLevel);
    }

    [Fact]
    public async Task SeedAsync_ImportsVirginiaDporSample_WithSupervisoryBrokerAffiliations()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "Virginia");

        Assert.Equal(4, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(8, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));
        Assert.Equal(8, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id));

        var stewartFine = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "0226023723");
        Assert.Equal("LINDSEY, VICKI ANN", stewartFine.QualifyingBrokerName);
        Assert.Equal(new DateOnly(2012, 1, 30), stewartFine.LicenseIssuedDate);

        var carderAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "0225186990");
        Assert.True(carderAffiliation.IsBrokerInCharge);
        Assert.Equal("0226023757", carderAffiliation.Firm!.LicenseNumber);
        Assert.Equal(new DateOnly(2008, 2, 25), carderAffiliation.Licensee!.LicenseIssuedDate);
    }

    [Fact]
    public async Task SeedAsync_ImportsHawaiiMyPvlSample_WithBranchOfficesAndEmployees()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "Hawaii");

        Assert.Equal(8, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(20, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));
        Assert.Equal(18, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id));

        var afff = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "RB-22161");
        Assert.Equal("FIRST HAWAIIAN REALTY", afff.TradeName);
        Assert.Equal(new DateOnly(2016, 11, 28), afff.LicenseIssuedDate);

        var branch = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "RBO-21851-1");
        Assert.Equal("HALE ELUA INVESTMENTS LLC", branch.LegalName);
        Assert.Equal("Branch Office", branch.TradeName);

        var paganoAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "RS-77373");
        Assert.Equal("RB-22161", paganoAffiliation.Firm!.LicenseNumber);

        var williams = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "RB-12660");
        Assert.Equal(new DateOnly(1982, 1, 6), williams.LicenseIssuedDate);
        Assert.False(await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.AnyAsync(affiliation =>
            affiliation.SnapshotId == snapshot.Id && affiliation.LicenseeId == williams.Id));
    }

    [Fact]
    public async Task SeedAsync_ImportsGeorgiaGrecSample_WithQualifyingBrokers()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "Georgia");

        Assert.Equal(20, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(12, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));
        Assert.Equal(12, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id));

        var chickDeep = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "79303");
        Assert.Equal("NEW FREEDOM REALTY PARTNERS", chickDeep.TradeName);
        Assert.Equal("DIONNA INGRAM", chickDeep.QualifyingBrokerName);

        var crimson = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "80264");
        Assert.Equal(new DateOnly(2022, 11, 7), crimson.LicenseIssuedDate);
        Assert.Equal("4330 STONEWALL TELL RD ATLANTA, GA 30349", crimson.Address);

        var ingramAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "296643");
        Assert.True(ingramAffiliation.IsBrokerInCharge);
        Assert.Equal(new DateOnly(2030, 1, 31), ingramAffiliation.Licensee!.LicenseRenewalDate);

        var sanders = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "343954");
        Assert.Equal(new DateOnly(2010, 12, 29), sanders.LicenseIssuedDate);

        var lapsedFirm = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "76840");
        Assert.Equal("LAPSED FEES", lapsedFirm.LicenseStatus);
    }

    [Fact]
    public async Task SeedAsync_ImportsSouthCarolinaLlrSample_WithClassifiedAffiliations()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "South Carolina");

        Assert.Equal(11, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(103, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));
        Assert.Equal(103, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id));

        var amRealty = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "2236");
        Assert.Equal("A & M REALTY & CONST CO INC", amRealty.LegalName);
        Assert.Equal("303 AIKEN HUNT CIRCLE COLUMBIA, SC 29223", amRealty.Address);
        Assert.Equal("LINDA P MARSHALL", amRealty.QualifyingBrokerName);

        var marshallAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "18599");
        Assert.True(marshallAffiliation.IsBrokerInCharge);
        Assert.Equal("Broker in Charge", marshallAffiliation.Licensee!.LicenseLevel);

        var boatwright = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "127018");
        Assert.Equal("AMY MARIA BOATWRIGHT", boatwright.Name);
        Assert.Equal("Associate", boatwright.LicenseLevel);
        Assert.Equal(new DateOnly(2027, 6, 30), boatwright.LicenseRenewalDate);

        var abdalla = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "5135");
        Assert.Equal(new DateOnly(1980, 7, 8), abdalla.LicenseIssuedDate);
        Assert.Equal("RAYMOND G ABDALLA", abdalla.QualifyingBrokerName);
    }

    [Fact]
    public async Task SeedAsync_ImportsWestVirginiaArelloSamples_AsLicenseesOnly()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "West Virginia");

        Assert.Equal(0, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(2, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));

        var dorsey = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "WV0006422");
        Assert.Equal("Salesperson", dorsey.LicenseLevel);
        Assert.Equal(new DateOnly(1974, 9, 20), dorsey.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2027, 6, 30), dorsey.LicenseRenewalDate);
    }

    private sealed class SeedFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }

        private SeedFixture(SqliteConnection connection, ApplicationDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public static async Task<SeedFixture> CreateAsync()
        {
            var workspaceRoot = FindWorkspaceRoot();
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            await StateRosterCaptureSeed.SeedAsync(dbContext, workspaceRoot);
            await StateRosterCaptureSeed.SeedAsync(dbContext, workspaceRoot);

            return new SeedFixture(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs", "NEW YORK")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Workspace root with docs folder not found.");
    }
}
