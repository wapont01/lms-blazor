using System.IO.Compression;
using System.Text;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public class CaliforniaDreCaptureSeedTests
{
    private static readonly string[] CurrentListHeaders =
    [
        "Multiple_License_Ind",
        "LastName_Primary",
        "FirstName_Secondary",
        "Name_Suffix",
        "Lic_Number",
        "Lic_Type",
        "Lic_Status",
        "Lic_Effective_Date",
        "Lic_Expiration_Date",
        "Original_date_of_license",
        "Related_Lic_Number",
        "Related_LastName_Primary",
        "Related_FirstName_Secondary",
        "Related_Name_Suffix",
        "Related_Lic_Type",
        "Address_1",
        "Address_2",
        "City",
        "State",
        "Zip_Code",
        "Foreign_Nation",
        "Foreign_Postal_Info",
        "County_name",
        "Restricted_Flag",
        "Ethics_and_Agency_Ind"
    ];

    private static readonly string[] BrokerAssociateHeaders =
    [
        "rb_license_id",
        "ba_license_id",
        "affiliation_start_date",
        "rb_name",
        "rb_addresss_line_1",
        "rb_addresss_line_2",
        "rb_city",
        "rb_state",
        "rb_zip_code",
        "rb_license_type",
        "rb_expiration_date",
        "ba_name",
        "ba_addresss_line_1",
        "ba_addresss_line_2",
        "ba_city",
        "ba_state",
        "ba_zip_code",
        "ba_expiration_date"
    ];

    [Fact]
    public async Task SeedAsync_PreservesLicenseRolesDatesRelationshipsAndBrokerAssociations_AndIsIdempotent()
    {
        await using var fixture = await CaptureFixture.CreateAsync();

        await CaliforniaDreCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        var snapshot = await fixture.DbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "California");

        Assert.Equal(RegulatorySnapshotStatuses.Approved, snapshot.Status);
        Assert.Equal("https://dre.ca.gov/Licensees/ExamineeLicenseeListDataFiles.html", snapshot.SourceUrl);
        Assert.Contains("education information only", snapshot.SourceName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new DateTime(2026, 10, 10, 0, 32, 13, DateTimeKind.Utc).AddTicks(1_852_840), snapshot.RetrievedAtUtc);
        Assert.Equal(3, await fixture.DbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id));
        Assert.Equal(6, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));
        Assert.Equal(4, await fixture.DbContext.RegulatoryFirmLicenseeAffiliations.CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id));

        var salesperson = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id
            && licensee.LicenseNumber == "00001234"
            && licensee.LicenseType == "Salesperson"
            && licensee.IsAdditionalLicenseRecord == false);
        Assert.Equal("Jane M Doe, Jr", salesperson.Name);
        Assert.Equal(new DateOnly(2020, 1, 1), salesperson.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2019, 1, 1), salesperson.OriginalLicenseDate);
        Assert.Equal(new DateOnly(2026, 12, 31), salesperson.LicenseRenewalDate);
        Assert.Equal("00005678", salesperson.RelatedLicenseNumber);
        Assert.Equal("Broker", salesperson.RelatedLicenseType);
        Assert.Equal("Robert Smith", salesperson.RelatedLicenseName);
        Assert.True(salesperson.IsRestricted);
        Assert.True(salesperson.EthicsAndAgencyOnly);

        var officer = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id
            && licensee.LicenseNumber == "00001234"
            && licensee.LicenseType == "Officer");
        Assert.True(officer.IsAdditionalLicenseRecord);
        Assert.Equal("Acme, Realty Inc.", officer.RelatedLicenseName);

        var brokerAssociate = await fixture.DbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "00007890");
        Assert.Equal("Broker Associate", brokerAssociate.LicenseLevel);

        var brokerAssociateAffiliations = await fixture.DbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Where(affiliation => affiliation.SnapshotId == snapshot.Id && affiliation.LicenseeId == brokerAssociate.Id)
            .ToListAsync();
        Assert.Equal(2, brokerAssociateAffiliations.Count);
        Assert.Contains(brokerAssociateAffiliations, affiliation =>
            affiliation.Firm!.LicenseNumber == "00005678"
            && affiliation.RelationshipEffectiveDate == new DateOnly(2023, 1, 10));
        Assert.Contains(brokerAssociateAffiliations, affiliation =>
            affiliation.Firm!.LicenseNumber == "00009012"
            && affiliation.RelationshipEffectiveDate == new DateOnly(2024, 2, 11));

        await CaliforniaDreCaptureSeed.SeedAsync(fixture.DbContext, fixture.WorkspaceRoot);
        Assert.Equal(1, await fixture.DbContext.RegulatoryDataSnapshots.CountAsync(candidate => candidate.State == "California"));
        Assert.Equal(6, await fixture.DbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id));
    }

    private sealed class CaptureFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _workspaceRoot;

        public ApplicationDbContext DbContext { get; }
        public string WorkspaceRoot => _workspaceRoot;

        private CaptureFixture(SqliteConnection connection, ApplicationDbContext dbContext, string workspaceRoot)
        {
            _connection = connection;
            DbContext = dbContext;
            _workspaceRoot = workspaceRoot;
        }

        public static async Task<CaptureFixture> CreateAsync()
        {
            var workspaceRoot = Path.Combine(Path.GetTempPath(), $"ca-dre-seed-{Guid.NewGuid():N}");
            var sourceDirectory = Path.Combine(workspaceRoot, "docs", "CALIFORNIA", "LICENSING", "XLXS");
            Directory.CreateDirectory(sourceDirectory);
            WriteCurrentListArchive(Path.Combine(sourceDirectory, "CurrList_20261009.zip"));
            WriteBrokerAssociateCsv(Path.Combine(sourceDirectory, "broker_associates_20261009.csv"));

            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var dbContext = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await dbContext.Database.EnsureCreatedAsync();

            return new CaptureFixture(connection, dbContext, workspaceRoot);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
            Directory.Delete(_workspaceRoot, recursive: true);
        }

        private static void WriteCurrentListArchive(string path)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("CurrList.csv");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.WriteLine(WriteCsv(CurrentListHeaders));
            writer.WriteLine(WriteCsv(CreateLicenseeRow(
                "00001234", "Salesperson", "Doe", "Jane M", "Jr", "00005678", "Broker", "Smith", "Robert", "",
                "20200101", "20261231", "20190101", "Y", "Y")));
            writer.WriteLine(WriteCsv(CreateLicenseeRow(
                "00001234", "Salesperson", "Doe", "Jane M", "Jr", "00005678", "Broker", "Smith", "Robert", "",
                "20200101", "20261231", "20190101", "Y", "Y", "SECOND ADDRESS")));
            writer.WriteLine(WriteCsv(CreateLicenseeRow(
                "00001234", "Officer", "Doe", "Jane M", "Jr", "00000009", "Corporation", "Acme, Realty Inc.", "", "",
                "20210201", "20270201", "20190101", "N", "N", multipleLicenseIndicator: "Y")));
            writer.WriteLine(WriteCsv(CreateLicenseeRow(
                "00005678", "Broker", "Smith", "Robert", "", "", "", "", "", "",
                "20100101", "20281231", "20100101", "N", "N")));
            writer.WriteLine(WriteCsv(CreateLicenseeRow(
                "00007890", "Broker", "Associate", "Alex", "", "", "", "", "", "",
                "20200101", "20291231", "20200101", "N", "N")));
            writer.WriteLine(WriteCsv(CreateLicenseeRow(
                "00000009", "Corporation", "Acme, Realty Inc.", "", "", "00001234", "Officer", "Doe", "Jane M", "Jr",
                "20200101", "20290521", "20100101", "N", "N")));
            writer.WriteLine(WriteCsv(CreateLicenseeRow(
                "00009012", "Corporation", "Second Realty Inc.", "", "", "", "", "", "", "",
                "20200101", "20290620", "20200101", "N", "N")));
        }

        private static string[] CreateLicenseeRow(
            string number,
            string type,
            string lastName,
            string firstName,
            string suffix,
            string relatedNumber,
            string relatedType,
            string relatedLastName,
            string relatedFirstName,
            string relatedSuffix,
            string effective,
            string expiration,
            string original,
            string restricted,
            string ethics,
            string address = "1 TEST ST",
            string multipleLicenseIndicator = "N") =>
        [
            multipleLicenseIndicator, lastName, firstName, suffix, number, type, "Licensed", effective, expiration, original,
            relatedNumber, relatedLastName, relatedFirstName, relatedSuffix, relatedType,
            address, "", "SACRAMENTO", "CA", "95814", "", "", "SACRAMENTO", restricted, ethics
        ];

        private static void WriteBrokerAssociateCsv(string path)
        {
            var rows = new[]
            {
                new[]
                {
                    "00005678", "00007890", "2023-01-10", "Robert Smith", "", "", "SACRAMENTO", "CA", "95814",
                    "Broker", "2028-12-31", "Associate, Alex", "", "", "SACRAMENTO", "CA", "95814", "2029-12-31"
                },
                new[]
                {
                    "00009012", "00007890", "2024-02-11", "Acme, Realty Inc.", "", "", "SACRAMENTO", "CA", "95814",
                    "Corporation", "2029-06-20", "Associate, Alex", "", "", "SACRAMENTO", "CA", "95814", "2029-12-31"
                }
            };

            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine(WriteCsv(BrokerAssociateHeaders));
            foreach (var row in rows)
            {
                writer.WriteLine(WriteCsv(row));
            }
        }

        private static string WriteCsv(IEnumerable<string> values) =>
            string.Join(",", values.Select(value => $"\"{value.Replace("\"", "\"\"")}\""));
    }
}
