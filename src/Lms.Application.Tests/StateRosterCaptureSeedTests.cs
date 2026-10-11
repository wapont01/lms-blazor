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
    public async Task TexasTrecCaptureSeed_ImportsCurrentBulkRosterAndIsIdempotent()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await TexasTrecCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Texas"
            && candidate.SourceUrl == "https://data.texas.gov/api/views/s7ft-44qi/rows.csv?accessType=DOWNLOAD");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(new DateTime(2026, 10, 10, 12, 43, 24, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Equal(174_839, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));

        var agent = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "858798-SA");
        Assert.Equal("KARSON LEE KITE", agent.Name);
        Assert.Equal("Active", agent.LicenseStatus);
        Assert.Equal(new DateOnly(2028, 10, 31), agent.LicenseRenewalDate);
        Assert.Equal("9007722-BB", agent.RelatedLicenseNumber);

        var company = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "9017330-BB");
        Assert.Equal("COTTAGE ROSE PROPERTY MANAGEMENT LLC", company.LegalName);
        Assert.Equal(new DateOnly(2028, 10, 31), company.LicenseRenewalDate);
        Assert.Equal("ROCHELLE M FITZGERALD", company.QualifyingBrokerName);

        var agentAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id && affiliation.LicenseeId == agent.Id);
        Assert.Equal("9007722-BB", agentAffiliation.Firm!.LicenseNumber);

        var designatedBrokerAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.FirmId == company.Id);
        Assert.True(designatedBrokerAffiliation.IsBrokerInCharge);
        Assert.Equal("521506-B", designatedBrokerAffiliation.Licensee!.LicenseNumber);

        Assert.False(await fixture.DbContext.RegulatoryLicensees.AnyAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "858871-SA"));

        await TexasTrecCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Texas"
            && candidate.SourceUrl == "https://data.texas.gov/api/views/s7ft-44qi/rows.csv?accessType=DOWNLOAD"));
    }

    [Fact]
    public async Task AlaskaCbplCaptureSeed_ImportsCurrentRealEstateRecordsAndOpenOfficesIdempotently()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await AlaskaCbplCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Alaska"
            && candidate.SourceUrl == "https://www.commerce.alaska.gov/cbp/main/");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Equal(2_756, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(615, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Empty(await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Where(record => record.SnapshotId == snapshot.Id)
            .ToListAsync());

        var associateBroker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "137404");
        Assert.Equal("CHRISTINE CASWELL", associateBroker.Name);
        Assert.Equal("Associate Broker", associateBroker.LicenseType);
        Assert.Equal("Active", associateBroker.LicenseStatus);
        Assert.Equal(new DateOnly(2018, 8, 20), associateBroker.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2028, 1, 31), associateBroker.LicenseRenewalDate);

        var openOffice = await fixture.DbContext.RegulatoryFirms.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "OFFICE16");
        Assert.Equal("DANIEL L. MAXWELL", openOffice.LegalName);
        Assert.Equal("MAXWELL REAL ESTATE", openOffice.TradeName);
        Assert.Equal("Office Open", openOffice.LicenseStatus);
        Assert.Contains("ANCHORAGE, AK, 99518", openOffice.Address);

        Assert.Equal(0, await fixture.DbContext.RegulatoryLicensees.CountAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseStatus == "Suspended"));

        await AlaskaCbplCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Alaska"
            && candidate.SourceUrl == "https://www.commerce.alaska.gov/cbp/main/"));
    }

    [Fact]
    public async Task NevadaNredCaptureSeed_ImportsAllActiveLicenseeListsAndIsIdempotent()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await NevadaNredCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Nevada" && candidate.SourceUrl == "https://red.nv.gov/");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Equal(25_596, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Empty(await fixture.DbContext.RegulatoryFirms.Where(record => record.SnapshotId == snapshot.Id).ToListAsync());
        Assert.Empty(await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Where(record => record.SnapshotId == snapshot.Id)
            .ToListAsync());

        var broker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "B.1002236.LLC");
        Assert.Equal("PHILLIP ALEXANDER", broker.Name);
        Assert.Equal("Broker", broker.LicenseType);
        Assert.Equal("Active", broker.LicenseStatus);
        Assert.Equal(new DateOnly(2018, 11, 29), broker.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2027, 11, 30), broker.LicenseRenewalDate);
        Assert.Equal("RED ROCK REALTY LLC", broker.RelatedLicenseName);
        Assert.Equal("Company", broker.RelatedLicenseType);

        var brokerSalesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "BS.0144746");
        Assert.Equal("AARON ALRED", brokerSalesperson.Name);
        Assert.Equal("Broker-Salesperson", brokerSalesperson.LicenseType);
        Assert.Equal(new DateOnly(2015, 8, 28), brokerSalesperson.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2028, 8, 31), brokerSalesperson.LicenseRenewalDate);

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "S.0036002");
        Assert.Equal("A CADRY GENENA", salesperson.Name);
        Assert.Equal("Salesperson", salesperson.LicenseType);

        await NevadaNredCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Nevada" && candidate.SourceUrl == "https://red.nv.gov/"));
    }

    [Fact]
    public async Task KansasKrecCaptureSeed_ImportsActiveLicenseesAndExplicitCompanyRelationshipsIdempotently()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await KansasKrecCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Kansas" && candidate.SourceUrl == "https://krec.ks.gov/");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Contains("active company links lack a name", snapshot.SourceName);
        Assert.Equal(16_837, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(2_337, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(17_708, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(0, await fixture.DbContext.RegulatoryLicensees.CountAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseStatus != "Active"));

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "00253297");
        Assert.Equal("Billy Aaron", salesperson.Name);
        Assert.Equal("Salesperson", salesperson.LicenseType);
        Assert.Equal(new DateOnly(2026, 2, 9), salesperson.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2028, 2, 1), salesperson.LicenseRenewalDate);
        Assert.Equal("00228947", salesperson.RelatedLicenseNumber);
        Assert.Equal("Supervising Broker", salesperson.RelatedLicenseType);

        var companyAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.LicenseeId == salesperson.Id);
        Assert.Equal("BO90022843", companyAffiliation.Firm!.LicenseNumber);
        Assert.Equal("REECE & NICHOLS REALTORS, INC.", companyAffiliation.Firm.LegalName);

        var broker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "00053783");
        var brokerAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.SingleAsync(affiliation =>
            affiliation.SnapshotId == snapshot.Id && affiliation.LicenseeId == broker.Id);
        Assert.True(brokerAffiliation.IsBrokerInCharge);

        await KansasKrecCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Kansas" && candidate.SourceUrl == "https://krec.ks.gov/"));
    }

    [Fact]
    public async Task UtahDreCaptureSeed_ImportsCurrentRealEstateLicenseesAndCompanyRelationshipsIdempotently()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await UtahDreCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Utah" && candidate.SourceUrl == "https://secure.utah.gov/rer/relv/search.html");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Equal(23_261, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(3_089, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(23_761, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(0, await fixture.DbContext.RegulatoryLicensees.CountAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseStatus == "Expired"));
        Assert.True(await fixture.DbContext.RegulatoryLicensees.AnyAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseStatus == "Active On Probation"));

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "10804064-SA00");
        Assert.Equal("AARON MAXFIELD", salesperson.Name);
        Assert.Equal("Sales Agent", salesperson.LicenseType);
        Assert.Equal(new DateOnly(2022, 4, 5), salesperson.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2028, 4, 30), salesperson.LicenseRenewalDate);

        var affiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(record => record.Firm)
            .SingleAsync(record => record.SnapshotId == snapshot.Id && record.LicenseeId == salesperson.Id);
        Assert.Equal("5615211-CN00", affiliation.Firm!.LicenseNumber);
        Assert.Equal("30 DAY REALTY", affiliation.Firm.LegalName);

        await UtahDreCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Utah" && candidate.SourceUrl == "https://secure.utah.gov/rer/relv/search.html"));
    }

    [Fact]
    public async Task NewHampshireOplcCaptureSeed_ImportsCurrentPeopleAndBusinessLicensesIdempotently()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await NewHampshireOplcCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "New Hampshire"
            && candidate.SourceUrl == "https://www.oplc.nh.gov/list-oplc-licensees-and-their-license-types");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Equal(10_608, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(1_541, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Empty(await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Where(record => record.SnapshotId == snapshot.Id)
            .ToListAsync());
        Assert.Contains("1 missing license number; 2 missing name", snapshot.SourceName);
        Assert.All(
            await fixture.DbContext.RegulatoryLicensees.Where(record => record.SnapshotId == snapshot.Id).ToListAsync(),
            record => Assert.StartsWith("Active", record.LicenseStatus, StringComparison.Ordinal));

        var renewedLicensee = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "002026");
        Assert.Equal("Sandra Heino", renewedLicensee.Name);
        Assert.Equal("Associate Broker", renewedLicensee.LicenseType);
        Assert.Equal(DateOnly.FromDateTime(DateTime.FromOADate(46848)), renewedLicensee.LicenseRenewalDate);

        var dualLicensee = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "065898");
        Assert.Equal("Principal Broker, Associate Broker", dualLicensee.LicenseType);
        Assert.Equal("Principal Broker", dualLicensee.LicenseLevel);

        var tradeName = await fixture.DbContext.RegulatoryFirms.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "081829");
        Assert.Equal("Country Lane Realty", tradeName.LegalName);
        Assert.Equal("Active", tradeName.LicenseStatus);
        Assert.Equal(DateOnly.FromDateTime(DateTime.FromOADate(46305)), tradeName.LicenseRenewalDate);

        await NewHampshireOplcCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "New Hampshire"
            && candidate.SourceUrl == snapshot.SourceUrl));
    }

    [Fact]
    public async Task OregonOreaCaptureSeed_ImportsCurrentLicenseesAndExplicitBusinessAffiliationsIdempotently()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await OregonOreaCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Oregon"
            && candidate.SourceUrl == "https://orea.elicense.micropact.com/Lookup/GenerateRoster.aspx");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Equal(4_493, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(21_126, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(21_539, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Contains("2 unnamed individual records excluded", snapshot.SourceName);

        var multiTypeLicensee = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "200706105");
        Assert.Equal("JOSEPH GERARD DRESSMAN", multiTypeLicensee.Name);
        Assert.Equal("Principal Broker, Broker", multiTypeLicensee.LicenseType);
        Assert.Equal("Principal Broker", multiTypeLicensee.LicenseLevel);
        Assert.Equal(new DateOnly(2027, 11, 30), multiTypeLicensee.LicenseRenewalDate);
        var multiTypeAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.SingleAsync(record =>
            record.SnapshotId == snapshot.Id
            && record.LicenseeId == multiTypeLicensee.Id
            && record.Firm!.LicenseNumber == "201207614");
        Assert.Equal(new DateOnly(2017, 6, 12), multiTypeAffiliation.RelationshipEffectiveDate);

        var licensee = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "201208841");
        Assert.Equal("David Matthew Abrams", licensee.Name);
        Assert.Equal("Principal Broker", licensee.LicenseLevel);
        Assert.Equal(2, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseeId == licensee.Id));

        var affiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(record => record.Firm)
            .SingleAsync(record => record.SnapshotId == snapshot.Id
                && record.LicenseeId == licensee.Id
                && record.Firm!.LicenseNumber == "201207614");
        Assert.Equal("Think Real Estate", affiliation.Firm!.LegalName);
        Assert.Equal("HOPE BERNICE BERAKA", affiliation.BrokerInChargeName);
        Assert.Equal(new DateOnly(2025, 11, 14), affiliation.RelationshipEffectiveDate);

        var ambiguousManagerAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.SingleAsync(record =>
            record.SnapshotId == snapshot.Id
            && record.Licensee!.LicenseNumber == "201218113"
            && record.Firm!.LicenseNumber == "201236884");
        Assert.Null(ambiguousManagerAffiliation.BrokerInChargeName);
        Assert.False(ambiguousManagerAffiliation.IsBrokerInCharge);

        var principalBrokerAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .SingleAsync(record => record.SnapshotId == snapshot.Id
                && record.Licensee!.LicenseNumber == "931000192"
                && record.Firm!.LicenseNumber == "201112012");
        Assert.True(principalBrokerAffiliation.IsBrokerInCharge);

        await OregonOreaCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Oregon"
            && candidate.SourceUrl == snapshot.SourceUrl));
    }

    [Fact]
    public async Task SouthDakotaCitySampleCaptureSeed_ImportsDocumentedLicenseTypesAndMatchedFirmAffiliationsIdempotently()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await SouthDakotaCitySampleCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "South Dakota"
            && candidate.SourceUrl == "https://sdrec.sd.gov/registration/licenseelist.aspx");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Equal(22, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(45, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(40, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));

        var broker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "14048");
        Assert.Equal("Blake, Alisa (Lisa) M", broker.Name);
        Assert.Equal("Broker", broker.LicenseType);
        Assert.Equal("Broker", broker.LicenseLevel);
        Assert.Equal("Active", broker.LicenseStatus);
        Assert.Equal(new DateOnly(2008, 6, 4), broker.LicenseIssuedDate);
        Assert.Null(broker.LicenseRenewalDate);

        var rentalAgent = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "15116");
        Assert.Equal("Ehlers, Christine L", rentalAgent.Name);
        Assert.Equal("Res. Rental Agent", rentalAgent.LicenseType);
        Assert.Equal(new DateOnly(2012, 4, 18), rentalAgent.LicenseIssuedDate);
        Assert.Null(rentalAgent.LicenseRenewalDate);
        var rentalAgentAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(record => record.Firm)
            .SingleAsync(record => record.SnapshotId == snapshot.Id && record.LicenseeId == rentalAgent.Id);
        Assert.Equal("Costello Property Management", rentalAgentAffiliation.Firm!.LegalName);
        Assert.Equal("11054", rentalAgentAffiliation.Firm.LicenseNumber);

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "21142");
        Assert.Equal("Herding, Clay W", salesperson.Name);
        Assert.Equal("Broker Associate", salesperson.LicenseType);
        Assert.Equal(2, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseeId == salesperson.Id));

        var additionalBroker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "11590");
        Assert.Equal("Broker, Broker (additional)", additionalBroker.LicenseType);
        Assert.Equal(2, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseeId == additionalBroker.Id));

        var affiliate = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(record => record.Firm)
            .SingleAsync(record => record.SnapshotId == snapshot.Id
                && record.LicenseeId == broker.Id
                && record.Firm!.LicenseNumber == "15762");
        Assert.Equal("Lisa Blake Real Estate", affiliate.Firm!.LegalName);

        var firm = await fixture.DbContext.RegulatoryFirms.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "15762");
        Assert.Equal("Listed", firm.LicenseStatus);
        Assert.Equal("106 W Capital", firm.Address);
        Assert.Null(firm.LicenseRenewalDate);

        var unmatchedAffiliationLicensee = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "12256");
        Assert.Empty(await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.Where(record =>
            record.SnapshotId == snapshot.Id && record.LicenseeId == unmatchedAffiliationLicensee.Id).ToListAsync());

        await SouthDakotaCitySampleCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "South Dakota"
            && candidate.SourceUrl == snapshot.SourceUrl));
    }

    [Fact]
    public async Task SouthDakotaCitySampleCaptureSeed_RefreshesOutdatedPendingSnapshot()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);
        var oldSnapshot = new RegulatoryDataSnapshot
        {
            State = "South Dakota",
            SourceName = "South Dakota Real Estate Commission city-based licensee development sample",
            SourceUrl = "https://sdrec.sd.gov/registration/licenseelist.aspx",
            ParserVersion = "sdrec-pierre-real-estate-sample-v3-2026-10",
            RetrievedAtUtc = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = RegulatorySnapshotStatuses.PendingReview
        };
        var oldFirm = new RegulatoryFirm
        {
            SnapshotId = oldSnapshot.Id,
            State = "South Dakota",
            LicenseNumber = "OLD",
            LegalName = "Outdated firm"
        };
        var oldLicensee = new RegulatoryLicensee
        {
            SnapshotId = oldSnapshot.Id,
            State = "South Dakota",
            LicenseNumber = "OLD",
            Name = "Outdated licensee"
        };
        var oldAffiliation = new RegulatoryFirmLicenseeAffiliation
        {
            SnapshotId = oldSnapshot.Id,
            Firm = oldFirm,
            Licensee = oldLicensee
        };
        fixture.DbContext.AddRange(oldSnapshot, oldFirm, oldLicensee, oldAffiliation);
        await fixture.DbContext.SaveChangesAsync();

        await SouthDakotaCitySampleCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "South Dakota"
            && candidate.SourceUrl == oldSnapshot.SourceUrl);
        Assert.Equal("sdrec-city-real-estate-sample-v4-2026-10", snapshot.ParserVersion);
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal(22, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(45, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(40, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.False(await fixture.DbContext.RegulatoryFirms.AnyAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "OLD"));
        Assert.False(await fixture.DbContext.RegulatoryLicensees.AnyAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "OLD"));

        await SouthDakotaCitySampleCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(22, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(40, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));
    }

    [Fact]
    public async Task NorthDakotaFargoCaptureSeed_ImportsVerifiedActiveDirectorySampleIdempotently()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await NorthDakotaFargoCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "North Dakota"
            && candidate.SourceUrl == "https://services.realestatend.org/directory/index.asp");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal("ndrec-fargo-directory-sample-v1-2026-10-10", snapshot.ParserVersion);
        Assert.Equal(18, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(13, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(18, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(18, await fixture.DbContext.RegulatoryLicensees.CountAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseStatus == "Active"));

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "8480");
        Assert.Equal("Aanden, Jenna K", salesperson.Name);
        Assert.Equal("Salesperson", salesperson.LicenseType);
        Assert.Equal(new DateOnly(2011, 9, 23), salesperson.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2026, 12, 31), salesperson.LicenseRenewalDate);
        Assert.Equal("696", salesperson.RelatedLicenseNumber);
        Assert.Equal("Town & Country Realty, LLC", salesperson.RelatedLicenseName);

        var broker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "10052");
        Assert.Equal("Broker", broker.LicenseType);
        Assert.Equal("Active", broker.LicenseStatus);

        var brokerAssociate = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "11595");
        Assert.Equal("Broker Associate", brokerAssociate.LicenseType);

        await NorthDakotaFargoCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "North Dakota"
            && candidate.SourceUrl == snapshot.SourceUrl));
    }

    [Fact]
    public async Task NorthDakotaBismarckCaptureSeed_ImportsOnlyVerifiedZipCode58501Records()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);
        var previousSnapshot = new RegulatoryDataSnapshot
        {
            State = "North Dakota",
            SourceName = "Previous Bismarck capture",
            SourceUrl = "https://services.realestatend.org/directory/index.asp",
            ParserVersion = "ndrec-bismarck-58501-directory-sample-v1-2026-10-10",
            RetrievedAtUtc = new DateTime(2026, 10, 10, 19, 55, 0, DateTimeKind.Utc),
            Status = RegulatorySnapshotStatuses.PendingReview
        };
        fixture.DbContext.RegulatoryDataSnapshots.Add(previousSnapshot);
        fixture.DbContext.RegulatoryFirms.Add(new RegulatoryFirm
        {
            Snapshot = previousSnapshot,
            State = "North Dakota",
            LicenseNumber = "old-firm",
            LegalName = "Old Bismarck Firm"
        });
        fixture.DbContext.RegulatoryLicensees.Add(new RegulatoryLicensee
        {
            Snapshot = previousSnapshot,
            State = "North Dakota",
            LicenseNumber = "old-licensee",
            Name = "Old Bismarck Licensee"
        });
        await fixture.DbContext.SaveChangesAsync();

        await NorthDakotaBismarckCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "North Dakota"
            && candidate.SourceUrl == "https://services.realestatend.org/directory/index.asp"
            && candidate.RetrievedAtUtc == new DateTime(2026, 10, 10, 19, 55, 0, DateTimeKind.Utc));
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal("ndrec-bismarck-58501-directory-sample-v2-2026-10-10", snapshot.ParserVersion);
        Assert.Equal(20, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(8, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(20, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.False(await fixture.DbContext.RegulatoryFirms.AnyAsync(record => record.LegalName == "Old Bismarck Firm"));
        Assert.False(await fixture.DbContext.RegulatoryLicensees.AnyAsync(record => record.Name == "Old Bismarck Licensee"));

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "9364");
        Assert.Equal("Abel, Jana S", salesperson.Name);
        Assert.Equal("Salesperson", salesperson.LicenseType);
        Assert.Equal(new DateOnly(2016, 2, 16), salesperson.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2026, 12, 31), salesperson.LicenseRenewalDate);
        Assert.Equal("716", salesperson.RelatedLicenseNumber);

        var associate = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "8545");
        Assert.Equal("Broker Associate", associate.LicenseType);

        var broker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "10392");
        Assert.Equal("Broker", broker.LicenseType);

        var bitz = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "2166");
        Assert.Equal("Bitz Realty", bitz.RelatedLicenseName);
        var bitzAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(record => record.Firm)
            .SingleAsync(record => record.SnapshotId == snapshot.Id && record.LicenseeId == bitz.Id);
        Assert.Equal("2593", bitzAffiliation.Firm!.LicenseNumber);
        Assert.Equal("Bitz Realty - Branch Office", bitzAffiliation.Firm.LegalName);

        Assert.False(await fixture.DbContext.RegulatoryLicensees.AnyAsync(record =>
            record.SnapshotId == snapshot.Id && record.State != "North Dakota"));

        await NorthDakotaBismarckCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "North Dakota"
            && candidate.SourceUrl == snapshot.SourceUrl
            && candidate.RetrievedAtUtc == snapshot.RetrievedAtUtc));
    }

    [Fact]
    public async Task IowaPlbCaptureSeed_ImportsDesMoinesSampleWithVerifiedCategoriesAndExplicitAffiliations()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await IowaPlbCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Iowa"
            && candidate.SourceUrl == "https://ia-plb.my.site.com/LicenseSearchPage"
            && candidate.RetrievedAtUtc == new DateTime(2026, 10, 10, 20, 17, 37, DateTimeKind.Utc));
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal("iowa-plb-des-moines-50309-ak-pages1-2-v3-2026-10-10", snapshot.ParserVersion);
        Assert.Equal(26, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(18, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(25, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(record => record.SnapshotId == snapshot.Id));

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "S70563000");
        Assert.Equal("Alyson DeMoss", salesperson.Name);
        Assert.Equal("Real Estate Salesperson", salesperson.LicenseType);
        Assert.Equal("Salesperson", salesperson.LicenseLevel);
        Assert.Equal(new DateOnly(2022, 5, 20), salesperson.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2027, 12, 31), salesperson.LicenseRenewalDate);
        Assert.Equal("F05081000", salesperson.RelatedLicenseNumber);

        var brokerAssociate = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "B63177000");
        Assert.Equal("Real Estate Broker Associate", brokerAssociate.LicenseType);
        Assert.Equal("Broker", brokerAssociate.LicenseLevel);

        var brokerAssociateAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "B63177000");
        Assert.Equal("BG Brokerage LLC", brokerAssociateAffiliation.Firm!.LegalName);
        Assert.False(brokerAssociateAffiliation.IsBrokerInCharge);
        Assert.Null(brokerAssociateAffiliation.BrokerInChargeName);

        var managingBroker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "B70300000");
        Assert.Equal("Real Estate Broker Officer", managingBroker.LicenseType);
        Assert.Equal("Managing Broker", managingBroker.LicenseLevel);
        var managingBrokerAffiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.LicenseeId == managingBroker.Id);
        Assert.True(managingBrokerAffiliation.IsBrokerInCharge);
        Assert.Equal("Brandon Wayne Jennings", managingBrokerAffiliation.BrokerInChargeName);

        var tradeName = await fixture.DbContext.RegulatoryFirms.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "T06773000");
        Assert.Equal("Artisan Management Group", tradeName.TradeName);
        Assert.Equal("Active", tradeName.LicenseStatus);

        Assert.False(await fixture.DbContext.RegulatoryLicensees.AnyAsync(record =>
            record.SnapshotId == snapshot.Id && record.State != "Iowa"));

        var unaffiliatedBroker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "B73801000");
        Assert.Null(unaffiliatedBroker.RelatedLicenseNumber);
        Assert.False(await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.AnyAsync(affiliation =>
            affiliation.SnapshotId == snapshot.Id && affiliation.LicenseeId == unaffiliatedBroker.Id));

        await IowaPlbCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Iowa"
            && candidate.SourceUrl == snapshot.SourceUrl
            && candidate.RetrievedAtUtc == snapshot.RetrievedAtUtc));
    }

    [Fact]
    public async Task MinnesotaPulseCaptureSeed_ImportsBoundedSampleWithSourceCategoriesAndVerifiedAffiliation()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await MinnesotaPulseCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Minnesota");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal("mn-pulse-saint-paul-page1-sample-v1-2026-10-10", snapshot.ParserVersion);
        Assert.Equal(9, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(5, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Single(await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Where(record => record.SnapshotId == snapshot.Id)
            .ToListAsync());

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "40961338");
        Assert.Equal("Resident Real Est Salesperson", salesperson.LicenseType);
        Assert.Equal("Salesperson", salesperson.LicenseLevel);
        Assert.Equal(new DateOnly(2028, 6, 30), salesperson.LicenseRenewalDate);

        var broker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "40352890");
        Assert.Equal("Resident Real Estate Broker", broker.LicenseType);
        Assert.Equal("Broker", broker.LicenseLevel);
        Assert.Equal("40608435", broker.RelatedLicenseNumber);

        var affiliation = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(record => record.Firm)
            .Include(record => record.Licensee)
            .SingleAsync(record => record.SnapshotId == snapshot.Id);
        Assert.Equal("Blackbridge", affiliation.Firm!.LegalName);
        Assert.Equal("Gergen, Thomas J", affiliation.Licensee!.Name);
        Assert.False(affiliation.IsBrokerInCharge);

        await MinnesotaPulseCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Minnesota"
            && candidate.SourceUrl == snapshot.SourceUrl
            && candidate.RetrievedAtUtc == snapshot.RetrievedAtUtc));
    }

    [Fact]
    public async Task TennesseeCaptureSeed_ImportsActiveNashvilleSamplesWithoutInferringAffiliations()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);

        await TennesseeCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "Tennessee"
            && candidate.SourceUrl == "https://search.cloud.commerce.tn.gov/");
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, snapshot.Status);
        Assert.Equal("tn-verify-nashville-active-sample-v1-2026-10-10", snapshot.ParserVersion);
        Assert.Equal(40, await fixture.DbContext.RegulatoryLicensees.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Equal(20, await fixture.DbContext.RegulatoryFirms.CountAsync(record => record.SnapshotId == snapshot.Id));
        Assert.Empty(await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Where(record => record.SnapshotId == snapshot.Id)
            .ToListAsync());

        var broker = await fixture.DbContext.RegulatoryLicensees.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "281540");
        Assert.Equal("HAMMOND, GRANTLAND \"GRANT\" W.", broker.Name);
        Assert.Equal("Real Estate Broker", broker.LicenseType);
        Assert.Equal(new DateOnly(2027, 10, 17), broker.LicenseRenewalDate);

        var firm = await fixture.DbContext.RegulatoryFirms.SingleAsync(record =>
            record.SnapshotId == snapshot.Id && record.LicenseNumber == "266763");
        Assert.Equal("Vantage Real Estate LLC", firm.LegalName);
        Assert.Equal(new DateOnly(2027, 3, 10), firm.LicenseRenewalDate);

        await TennesseeCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "Tennessee" && candidate.SourceUrl == snapshot.SourceUrl));
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
        Assert.Equal(102, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));
        Assert.Equal(102, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id));

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
        Assert.Equal("Broker In Charge", marshallAffiliation.Licensee!.LicenseLevel);

        var boatwright = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "127018");
        Assert.Equal("AMY MARIA BOATWRIGHT", boatwright.Name);
        Assert.Equal("Associate", boatwright.LicenseLevel);
        Assert.Equal(new DateOnly(2027, 6, 30), boatwright.LicenseRenewalDate);
        Assert.Equal("Active", boatwright.LicenseStatus);
        Assert.Equal(new DateOnly(2022, 1, 13), boatwright.LicenseIssuedDate);

        var secondMarshallLicense = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "45160");
        Assert.Equal("ANDREW E MARSHALL", secondMarshallLicense.Name);
        Assert.Equal(new DateOnly(2004, 3, 30), secondMarshallLicense.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2028, 6, 30), secondMarshallLicense.LicenseRenewalDate);

        var brokerMarshall = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "18584");
        Assert.Equal("Broker", brokerMarshall.LicenseType);
        Assert.Equal("Active", brokerMarshall.LicenseStatus);
        Assert.Equal(new DateOnly(1982, 12, 13), brokerMarshall.LicenseIssuedDate);
        Assert.DoesNotContain(await fixture.DbContext.RegulatoryLicensees
            .Where(licensee => licensee.SnapshotId == snapshot.Id)
            .Select(licensee => licensee.LicenseNumber)
            .ToListAsync(), licenseNumber => licenseNumber == "79942");

        var abdalla = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "5135");
        Assert.Equal(new DateOnly(1980, 7, 8), abdalla.LicenseIssuedDate);
        Assert.Equal("RAYMOND G ABDALLA", abdalla.QualifyingBrokerName);
    }

    [Fact]
    public async Task SeedAsync_RefreshesSouthCarolinaAsANewSnapshot_AndKeepsPriorApprovedData()
    {
        await using var fixture = await SeedFixture.CreateAsync(seedCaptureData: false);
        var priorSnapshot = new RegulatoryDataSnapshot
        {
            State = "South Carolina",
            SourceName = "Prior SC profile capture",
            SourceUrl = "https://verify.llronline.com/LicLookup/Rec/Rec.aspx?div=19",
            ParserVersion = "sc-llr-columbia20-links-v1",
            RetrievedAtUtc = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
            Status = RegulatorySnapshotStatuses.Approved,
            ReviewedAtUtc = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc)
        };
        var priorLicensee = new RegulatoryLicensee
        {
            Snapshot = priorSnapshot,
            State = "South Carolina",
            LicenseNumber = "38611",
            Name = "Prior captured name",
            LicenseType = "Associate",
            LicenseLevel = "Associate",
            LicenseStatus = "Lapsed"
        };
        fixture.DbContext.RegulatoryDataSnapshots.Add(priorSnapshot);
        fixture.DbContext.RegulatoryLicensees.Add(priorLicensee);
        await fixture.DbContext.SaveChangesAsync();

        await StateRosterCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);

        var snapshots = await fixture.DbContext.RegulatoryDataSnapshots
            .Where(snapshot => snapshot.State == "South Carolina")
            .OrderBy(snapshot => snapshot.RetrievedAtUtc)
            .ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal("Prior captured name", (await fixture.DbContext.RegulatoryLicensees
            .SingleAsync(licensee => licensee.SnapshotId == priorSnapshot.Id)).Name);

        var latest = snapshots[^1];
        Assert.Equal("sc-llr-columbia20-profiles-v2", latest.ParserVersion);
        var refreshedLicensee = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == latest.Id && licensee.LicenseNumber == "38611");
        Assert.Equal("DANIEL J HELTNESS", refreshedLicensee.Name);
        Assert.Equal("Active", refreshedLicensee.LicenseStatus);
        Assert.Equal(new DateOnly(2002, 9, 18), refreshedLicensee.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2027, 6, 30), refreshedLicensee.LicenseRenewalDate);

        await StateRosterCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(2, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(snapshot => snapshot.State == "South Carolina"));
    }

    [Fact]
    public async Task SeedAsync_ImportsWestVirginiaArelloSamples_AsLicenseesOnly()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "West Virginia" && candidate.SourceUrl == "https://www.arello.com/");

        Assert.Equal(0, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(2, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));

        var dorsey = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "WV0006422");
        Assert.Equal("Salesperson", dorsey.LicenseLevel);
        Assert.Equal(new DateOnly(1974, 9, 20), dorsey.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2027, 6, 30), dorsey.LicenseRenewalDate);
    }

    [Fact]
    public async Task SeedAsync_ImportsWestVirginiaActiveRosters_PreservingBranchAffiliationsAndMergedCategories()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.State == "West Virginia" && candidate.SourceUrl == "https://rec.wv.gov/About/Pages/Active-Licensee-Rosters.aspx");

        Assert.Equal(RegulatorySnapshotStatuses.Approved, snapshot.Status);
        Assert.Equal(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc), snapshot.RetrievedAtUtc);
        Assert.Equal(906, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(5123, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));
        Assert.Equal(5170, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id));

        var dualCategoryLicensee = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "WV0020659");
        Assert.Equal("Broker", dualCategoryLicensee.LicenseLevel);
        Assert.Equal("Broker, Associate Broker", dualCategoryLicensee.LicenseType);
        Assert.Null(dualCategoryLicensee.LicenseRenewalDate);
        Assert.Null(dualCategoryLicensee.LicenseIssuedDate);

        var brokerAffiliations = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Where(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.LicenseeId == dualCategoryLicensee.Id)
            .ToListAsync();
        Assert.Equal(2, brokerAffiliations.Count);
        Assert.All(brokerAffiliations, affiliation => Assert.False(string.IsNullOrWhiteSpace(affiliation.ContactEmail)));

        var branch = await fixture.DbContext.RegulatoryFirms.SingleAsync(firm =>
            firm.SnapshotId == snapshot.Id && firm.LicenseNumber == "001157-00");
        Assert.Equal("J CRAWFORD GOLDMAN", branch.LegalName);
        Assert.Contains("CHARLESTON", branch.Address, StringComparison.OrdinalIgnoreCase);

        await StateRosterCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate =>
            candidate.State == "West Virginia" && candidate.SourceUrl == snapshot.SourceUrl));
        Assert.Equal(5170, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id));
    }

    private sealed class SeedFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public string WorkspaceRoot { get; }

        private SeedFixture(SqliteConnection connection, ApplicationDbContext dbContext, string workspaceRoot)
        {
            _connection = connection;
            DbContext = dbContext;
            WorkspaceRoot = workspaceRoot;
        }

        public static async Task<SeedFixture> CreateAsync(bool seedCaptureData = true)
        {
            var workspaceRoot = FindWorkspaceRoot();
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            if (seedCaptureData)
            {
                await StateRosterCaptureSeed.SeedAsync(dbContext, workspaceRoot);
                await StateRosterCaptureSeed.SeedAsync(dbContext, workspaceRoot);
            }

            return new SeedFixture(connection, dbContext, workspaceRoot);
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
