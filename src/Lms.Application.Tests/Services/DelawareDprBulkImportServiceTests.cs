using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class DelawareDprBulkImportServiceTests
{
    // Layout copied from the data.delaware.gov pjnv-eaih CSV export.
    private const string Csv =
        "\"last_name\",\"first_name\",\"combined_name\",\"license_no\",\"profession_id\",\"license_type\",\"city\",\"state\",\"zip_code\",\"country\",\"issue_date\",\"expiration_date\",\"disciplinary_action\",\"license_status\",\"count\"\n"
        + "\"Smith\",\"Jane\",\"Smith, Jane\",\"RS-0012345\",\"Real Estate\",\"Salesperson\",\"Dover\",\"DE\",\"19901\",\"United States\",\"2019-05-01T00:00:00.000\",\"2026-04-30T00:00:00.000\",\"N\",\"Active\",\"1\"\n"
        + "\"Smith\",\"Jane\",\"Smith, Jane\",\"RS-0012345\",\"Real Estate\",\"Salesperson\",\"Dover\",\"DE\",\"19901\",\"United States\",\"2015-05-01T00:00:00.000\",\"2018-04-30T00:00:00.000\",\"N\",\"Expired\",\"1\"\n"
        + "\"Jones\",\"Bob\",\"Jones, Bob\",\"RB-0000777\",\"Real Estate\",\"Broker\",\"Lewes\",\"DE\",\"19958\",\"United States\",\"1900-01-01T00:00:00.000\",\"2026-04-30T00:00:00.000\",\"N\",\"Active\",\"1\"\n"
        + "\"COASTAL REALTY LLC\",\"\",\"COASTAL REALTY LLC\",\"RO-0000100\",\"Real Estate\",\"Main Office Permit\",\"Lewes\",\"DE\",\"19958\",\"United States\",\"2010-01-01T00:00:00.000\",\"2026-04-30T00:00:00.000\",\"N\",\"Active\",\"1\"\n"
        + "\"COASTAL REALTY LLC\",\"\",\"COASTAL REALTY LLC\",\"RO-0000200\",\"Real Estate\",\"Main Office Permit\",\"Dover\",\"DE\",\"19901\",\"United States\",\"2011-01-01T00:00:00.000\",\"2026-04-30T00:00:00.000\",\"N\",\"Active\",\"1\"\n"
        + "\"COASTAL REALTY, LLC\",\"\",\"COASTAL REALTY, LLC\",\"RP-0000300\",\"Real Estate\",\"Branch Office Permit\",\"Dover\",\"DE\",\"19901\",\"United States\",\"2012-01-01T00:00:00.000\",\"2026-04-30T00:00:00.000\",\"N\",\"Active\",\"1\"\n"
        + "\"VALUE AMC INC\",\"\",\"VALUE AMC INC\",\"X5-0000050\",\"Real Estate Appraisers\",\"Appraisal Management Company\",\"Austin\",\"TX\",\"78701\",\"United States\",\"2018-01-01T00:00:00.000\",\"2026-12-31T00:00:00.000\",\"N\",\"Active\",\"1\"\n"
        + "\"Lee\",\"Ann\",\"Lee, Ann\",\"X1-0000900\",\"Real Estate Appraisers\",\"Certified General Real Property Appraise\",\"Toronto\",\"ON\",\"M5V\",\"Canada\",\"2018-01-01T00:00:00.000\",\"2026-12-31T00:00:00.000\",\"N\",\"Inactive\",\"1\"\n";

    [Fact]
    public void ReadRecords_BuildsNamesAddressesAndTreats1900AsNoDate()
    {
        using var archive = Archive.Create(Csv);

        var records = DelawareDprBulkImportService.ReadRecords(archive.FilePath);

        Assert.Equal(8, records.Count);
        Assert.Equal(("Jane Smith", "Dover, DE 19901", "DOVER"), (records[0].Name, records[0].Address, records[0].City));
        Assert.Equal(new DateOnly(2019, 5, 1), records[0].IssueDate);
        Assert.Null(records[2].IssueDate);
        Assert.Equal("COASTAL REALTY LLC", records[3].Name);
        Assert.Equal("Toronto, ON M5V, Canada", records[7].Address);
    }

    [Fact]
    public void DistinctAndLocations_KeepLatestTermAndLinkBranchToSameCityMainOffice()
    {
        using var archive = Archive.Create(Csv);
        var records = DelawareDprBulkImportService.Distinct(DelawareDprBulkImportService.ReadRecords(archive.FilePath));

        Assert.Equal(7, records.Count);
        Assert.Equal("Active", records.Single(record => record.LicenseNumber == "RS-0012345").LicenseStatus);
        Assert.Equal(["RO-0000100", "RO-0000200", "X5-0000050"], records.Where(DelawareDprBulkImportService.IsFirm).Select(firm => firm.LicenseNumber));
        Assert.Equal(RegulatoryIndustries.Appraisal, DelawareDprBulkImportService.FirmIndustry(records.Single(record => record.LicenseNumber == "X5-0000050")));
        var location = Assert.Single(DelawareDprBulkImportService.SelectLocations(records));
        Assert.Equal(("RP-0000300", "RO-0000200", RegulatoryLocationMatchMethods.NormalizedName),
            (location.Branch.LicenseNumber, location.FirmLicenseNumber, location.MatchMethod));
    }

    [Fact]
    public void ReadRecords_RejectsChangedHeaderAndUnexpectedProfession()
    {
        using var renamed = Archive.Create(Csv.Replace("\"license_type\"", "\"type\"", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => DelawareDprBulkImportService.ReadRecords(renamed.FilePath));

        using var other = Archive.Create(Csv.Replace("\"Real Estate\",\"Broker\"", "\"Nursing\",\"Broker\"", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => DelawareDprBulkImportService.ReadRecords(other.FilePath));
    }

    [Fact]
    public async Task ImportArchive_StoresFirmsLocationsAndLicenseesIdempotently()
    {
        using var archive = Archive.Create(Csv);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new DelawareDprBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);

        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync();
        Assert.Equal(("Delaware", DelawareDprBulkImportService.ParserVersion), (snapshot.State, snapshot.ParserVersion));
        Assert.NotNull(snapshot.ActivatedAtUtc);

        var firms = await db.RegulatoryFirms.OrderBy(firm => firm.LicenseNumber).ToListAsync();
        Assert.Equal(["RO-0000100", "RO-0000200", "X5-0000050"], firms.Select(firm => firm.LicenseNumber));
        Assert.Equal(RegulatoryIndustries.RealEstate, firms[0].Industry);
        Assert.Equal(RegulatoryIndustries.Appraisal, firms[2].Industry);
        Assert.Equal(new DateOnly(2010, 1, 1), firms[0].LicenseIssuedDate);

        var branch = await db.RegulatoryFirmLocations.SingleAsync();
        Assert.Equal(("RP-0000300", firms[1].Id, "Branch office permit (Active)"), (branch.RegistrationNumber, branch.FirmId, branch.Kind));

        var people = await db.RegulatoryLicensees.OrderBy(person => person.LicenseNumber).ToListAsync();
        Assert.Equal(["RB-0000777", "RS-0012345", "X1-0000900"], people.Select(person => person.LicenseNumber));
        Assert.Equal(("Salesperson", "Real Estate - Salesperson", new DateOnly(2026, 4, 30)),
            (people[1].LicenseType, people[1].SourceLicenseType, people[1].LicenseRenewalDate));
        Assert.Equal("Inactive", people[2].LicenseStatus);
        Assert.Empty(await db.RegulatoryFirmLicenseeAffiliations.ToListAsync());

        Assert.True(RegulatoryRoster.HasBulkProvider("Delaware"));
        Assert.Equal(DelawareDprBulkImportService.ParserVersion, RegulatoryRoster.BulkParser("Delaware"));
        Assert.False(RegulatoryLicenseTypes.IsRealEstate("Delaware", "Certified General Real Property Appraise"));
        Assert.True(RegulatoryLicenseTypes.IsRealEstate("Delaware", "Salesperson"));
    }

    [Fact]
    public async Task ValidateArchive_RejectsManifestRowCountMismatch()
    {
        using var archive = Archive.Create(Csv, declaredRows: 7);

        await Assert.ThrowsAsync<InvalidDataException>(() => DelawareDprBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private sealed class Archive : IDisposable
    {
        private Archive(string root) => Root = root;
        public string Root { get; }
        public string FilePath => Path.Combine(Root, DelawareDprBulkImportService.FileName);

        public static Archive Create(string csv, long? declaredRows = null)
        {
            var archive = new Archive(Path.Combine(Path.GetTempPath(), $"de-dpr-test-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(archive.Root);
            var definition = DelawareDprBulkImportService.Definitions[0];
            var bytes = new UTF8Encoding(false).GetBytes(csv);
            File.WriteAllBytes(archive.FilePath, bytes);
            var rows = declaredRows ?? csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
            FloridaBulkManifestEntry[] manifest =
            [
                new(definition.RelativePath, rows, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), definition.Url,
                    DateTimeOffset.UtcNow, "test", new() { [definition.Columns.ToString()] = rows })
            ];
            File.WriteAllText(Path.Combine(archive.Root, "manifest.json"), JsonSerializer.Serialize(manifest));
            return archive;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
