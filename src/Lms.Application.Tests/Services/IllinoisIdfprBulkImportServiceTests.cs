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

public sealed class IllinoisIdfprBulkImportServiceTests
{
    private const string Header = "license_type,description,license_number,license_status,business,title,first_name,middle,last_name,prefix,suffix,business_name,businessdba,original_issue_date,effective_date,expiration_date,city,state,zip,county,specialty_qualifier,controlled_substance_schedule,delegated_controlled_substance,ever_disciplined,lastmodifieddate,case_number,action,discipline_start_date,discipline_end_date,discipline_reason";
    private static readonly string Csv = Quote(Header.Split(',')) + "\n"
        + Row("P1", "LICENSED REAL ESTATE BROKER", status: "EXPIRED", expiration: "04/30/2018")
        + Row("P1", "LICENSED REAL ESTATE BROKER", effective: "05/01/2020")
        + Row("P1", "LICENSED REAL ESTATE BROKER", effective: "05/01/2021", caseNumber: "D1")
        + Row("P1", "LICENSED REAL ESTATE BROKER", effective: "05/01/2021", caseNumber: "D2", first: "Later")
        + Row("P2", "CERTIFIED GENERAL REAL ESTATE APPRAISER", profession: "APPRAISAL", status: "NOT RENEWED", issue: "", effective: "")
        + Row("F1", "LICENSED REAL ESTATE BROKER CORPORATION", business: "COASTAL REALTY LLC", trade: "Coastal Homes")
        + Row("F2", "APPRAISAL MANAGEMENT COMPANY", profession: "APPRAISAL", business: "VALUE AMC INC")
        + Row("F3", "LICENSED REAL ESTATE CE SCHOOL", business: "LEARNING SCHOOL")
        + Row("L1", "LICENSED REAL ESTATE BRANCH OFFICE", business: "COASTAL REALTY LLC")
        + Row("L2", "LICENSED REAL ESTATE VIRTUAL OFFICE", business: "UNRELATED VIRTUAL OFFICE", status: "CANCELLED")
        + Row("P3", "RESIDENTIAL LEASING AGENT PERMIT", first: "", middle: "", last: "", suffix: "", business: "Published Business")
        + Row("P4", "LICENSED REAL ESTATE CE INSTRUCTOR")
        + Row("", "LICENSED REAL ESTATE SALESPERSON");

    [Fact]
    public void ReadRecords_ParsesNamesAddressesDatesAndSkipsBlankNumbers()
    {
        using var archive = Archive.Create(Csv);
        var records = IllinoisIdfprBulkImportService.ReadRecords(archive.FilePath);

        Assert.Equal(13, IllinoisIdfprBulkImportService.CheckFile(archive.FilePath));
        Assert.Equal(12, records.Count);
        Assert.Equal(("Jane Q Smith Jr", "Chicago, IL 60601", "CHICAGO"), (records[0].Name, records[0].Address, records[0].City));
        Assert.Equal(new DateOnly(2019, 5, 1), records[0].IssueDate);
        Assert.Equal(new DateOnly(2021, 5, 1), records[2].EffectiveDate);
        Assert.Null(records[4].IssueDate);
        Assert.Equal("NOT RENEWED", records[4].LicenseStatus);
        Assert.Equal("COASTAL REALTY LLC", records[5].Name);
        Assert.Equal("Coastal Homes", records[5].TradeName);
        Assert.Null(records[6].TradeName);
        Assert.Equal("Published Business", records[10].Name);
    }

    [Fact]
    public void DistinctAndLocations_CollapseDisciplinaryRowsAndLinkExactOwnerName()
    {
        using var archive = Archive.Create(Csv);
        var records = IllinoisIdfprBulkImportService.Distinct(IllinoisIdfprBulkImportService.ReadRecords(archive.FilePath));

        Assert.Equal(9, records.Count);
        var broker = records.Single(record => record.LicenseNumber == "P1");
        Assert.Equal("ACTIVE", broker.LicenseStatus);
        Assert.Equal(new DateOnly(2021, 5, 1), broker.EffectiveDate);
        Assert.Equal("Jane Q Smith Jr", broker.Name);
        Assert.Equal(["F1", "F2", "F3"], records.Where(IllinoisIdfprBulkImportService.IsFirm).Select(firm => firm.LicenseNumber));
        var locations = IllinoisIdfprBulkImportService.SelectLocations(records);
        Assert.Equal(("L1", "F1", RegulatoryLocationMatchMethods.ExactName),
            (locations[0].Branch.LicenseNumber, locations[0].FirmLicenseNumber, locations[0].MatchMethod));
        Assert.Null(locations[1].FirmLicenseNumber);
        Assert.Equal(RegulatoryLocationMatchMethods.NoMatch, locations[1].MatchMethod);
    }

    [Fact]
    public void Distinct_UsesLatestIssueOrEffectiveDate()
    {
        using var archive = Archive.Create(Quote(Header.Split(',')) + "\n"
            + Row("P", "LICENSED REAL ESTATE BROKER", issue: "05/01/2022", effective: "05/01/2020")
            + Row("P", "LICENSED REAL ESTATE BROKER", issue: "05/01/2019", effective: "05/01/2021"));
        var record = Assert.Single(IllinoisIdfprBulkImportService.Distinct(IllinoisIdfprBulkImportService.ReadRecords(archive.FilePath)));
        Assert.Equal(new DateOnly(2022, 5, 1), record.IssueDate);
    }

    [Theory]
    [InlineData("LICENSED REAL ESTATE BROKER CORPORATION", RegulatoryIndustries.RealEstate)]
    [InlineData("LICENSED REAL ESTATE BROKER PARTNERSHIP", RegulatoryIndustries.RealEstate)]
    [InlineData("LICENSED REAL ESTATE LIMITED LIABILITY FIRM", RegulatoryIndustries.RealEstate)]
    [InlineData("LICENSED REAL ESTATE BROKER ASSOCIATION", RegulatoryIndustries.RealEstate)]
    [InlineData("APPRAISAL MANAGEMENT COMPANY", RegulatoryIndustries.Appraisal)]
    [InlineData("LICENSED REAL ESTATE CE SCHOOL", RegulatoryIndustries.Education)]
    [InlineData("LICENSED REAL ESTATE PRE-LICENSE SCHOOL", RegulatoryIndustries.Education)]
    [InlineData("LICENSED REAL ESTATE EDUCATION PROVIDER", RegulatoryIndustries.Education)]
    [InlineData("LICENSED APPRAISER EDUCATION PROVIDER", RegulatoryIndustries.Education)]
    public void Classification_RecognizesAllFirmIndustries(string description, string industry)
    {
        using var archive = Archive.Create(Quote(Header.Split(',')) + "\n" + Row("F", description));
        var record = Assert.Single(IllinoisIdfprBulkImportService.ReadRecords(archive.FilePath));
        Assert.True(IllinoisIdfprBulkImportService.IsFirm(record));
        Assert.Equal(industry, IllinoisIdfprBulkImportService.FirmIndustry(record));
    }

    [Theory]
    [InlineData("LICENSED REAL ESTATE ASSOCIATE BROKER", "Associate Broker", true)]
    [InlineData("LICENSED REAL ESTATE BROKER", "Broker", true)]
    [InlineData("LICENSED REAL ESTATE MANAGING BROKER", "Managing Broker", true)]
    [InlineData("LICENSED REAL ESTATE SALESPERSON", "Salesperson", true)]
    [InlineData("LICENSED REAL ESTATE RESIDENTIAL LEASING AGENT", "Residential Leasing Agent", true)]
    [InlineData("RESIDENTIAL LEASING AGENT PERMIT", "Residential Leasing Agent Permit", true)]
    [InlineData("LICENSED REAL ESTATE CE INSTRUCTOR", "CE Instructor", false)]
    [InlineData("LICENSED REAL ESTATE PRE-LICENSE INSTRUCTOR", "Pre-License Instructor", false)]
    [InlineData("ASSOCIATE REAL ESTATE TRAINEE APPRAISER", "Associate Real Estate Trainee Appraiser", false)]
    [InlineData("CERTIFIED GENERAL REAL ESTATE APPRAISER", "Certified General Real Estate Appraiser", false)]
    [InlineData("CERTIFIED RESIDENTIAL REAL ESTATE APPRAISER", "Certified Residential Real Estate Appraiser", false)]
    [InlineData("STATE LICENSED REAL ESTATE APPRAISER", "State Licensed Real Estate Appraiser", false)]
    [InlineData("TEMPORARY PRACTICE REAL ESTATE APPRAISER", "Temporary Practice Real Estate Appraiser", false)]
    public void Classification_UsesFriendlyLicenseTypes(string description, string type, bool realEstate)
    {
        using var archive = Archive.Create(Quote(Header.Split(',')) + "\n" + Row("P", description));
        var record = Assert.Single(IllinoisIdfprBulkImportService.ReadRecords(archive.FilePath));
        Assert.False(IllinoisIdfprBulkImportService.IsFirm(record));
        Assert.Empty(IllinoisIdfprBulkImportService.SelectLocations([record]));
        Assert.Equal(type, record.LicenseType);
        Assert.Equal(realEstate, RegulatoryLicenseTypes.IsRealEstate("Illinois", record.LicenseType));
    }

    [Fact]
    public void ReadRecords_UsesNumberFallbackAndExcludesCourseApprovals()
    {
        using var archive = Archive.Create(Quote(Header.Split(',')) + "\n"
            + Row("P", "LICENSED REAL ESTATE BROKER", first: "", middle: "", last: "", suffix: "")
            + Row("C1", "LICENSED APPRAISER CE COURSE")
            + Row("C2", "LICENSED APPRAISER PRE-LIC COURSE")
            + Row("C3", "LICENSED REAL ESTATE CE COURSE")
            + Row("C4", "LICENSED REAL ESTATE PRE-LICENSE COURSE"));
        Assert.Equal("Name not published (P)", Assert.Single(IllinoisIdfprBulkImportService.ReadRecords(archive.FilePath)).Name);
        Assert.Equal(5, IllinoisIdfprBulkImportService.CheckFile(archive.FilePath));
    }

    [Fact]
    public void ReadRecords_RejectsChangedHeaderAndUnexpectedProfession()
    {
        using var renamed = Archive.Create(Csv.Replace("\"description\"", "\"type\"", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => IllinoisIdfprBulkImportService.ReadRecords(renamed.FilePath));
        using var other = Archive.Create(Csv.Replace("\"REAL ESTATE\"", "\"NURSING\"", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => IllinoisIdfprBulkImportService.ReadRecords(other.FilePath));
    }

    [Fact]
    public async Task ImportArchive_StoresFirmsLocationsAndLicenseesIdempotently()
    {
        using var archive = Archive.Create(Csv);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new IllinoisIdfprBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync();
        Assert.Equal(("Illinois", IllinoisIdfprBulkImportService.ParserVersion), (snapshot.State, snapshot.ParserVersion));
        Assert.Equal("Illinois Department of Financial and Professional Regulation open data: real estate and appraisal licenses (all statuses)", snapshot.SourceName);
        Assert.Equal(IllinoisIdfprBulkImportService.SourcePage, snapshot.SourceUrl);
        Assert.NotNull(snapshot.ActivatedAtUtc);

        var firms = await db.RegulatoryFirms.OrderBy(firm => firm.LicenseNumber).ToListAsync();
        Assert.Equal(["F1", "F2", "F3"], firms.Select(firm => firm.LicenseNumber));
        Assert.Equal(RegulatoryIndustries.RealEstate, firms[0].Industry);
        Assert.Equal(RegulatoryIndustries.Appraisal, firms[1].Industry);
        Assert.Equal(RegulatoryIndustries.Education, firms[2].Industry);
        Assert.Equal(("COASTAL REALTY LLC", "Coastal Homes"), (firms[0].LegalName, firms[0].TradeName));
        Assert.Equal(new DateOnly(2019, 5, 1), firms[0].LicenseIssuedDate);

        var locations = await db.RegulatoryFirmLocations.OrderBy(location => location.RegistrationNumber).ToListAsync();
        Assert.Equal(("L1", firms[0].Id, "Branch office (ACTIVE)"), (locations[0].RegistrationNumber, locations[0].FirmId, locations[0].Kind));
        Assert.Null(locations[1].FirmId);
        Assert.Equal("Virtual office (CANCELLED)", locations[1].Kind);
        var people = await db.RegulatoryLicensees.OrderBy(person => person.LicenseNumber).ToListAsync();
        Assert.Equal(["P1", "P2", "P3", "P4"], people.Select(person => person.LicenseNumber));
        Assert.Equal(("Broker", "REAL ESTATE - LICENSED REAL ESTATE BROKER", new DateOnly(2026, 4, 30)),
            (people[0].LicenseType, people[0].SourceLicenseType, people[0].LicenseRenewalDate));
        Assert.Equal("NOT RENEWED", people[1].LicenseStatus);
        Assert.Empty(await db.RegulatoryFirmLicenseeAffiliations.ToListAsync());
        Assert.True(RegulatoryRoster.HasBulkProvider("Illinois"));
        Assert.Equal(IllinoisIdfprBulkImportService.ParserVersion, RegulatoryRoster.BulkParser("Illinois"));
    }

    [Theory]
    [InlineData("LICENSED REAL ESTATE BRANCH OFFICE", "Branch office (ACTIVE)")]
    [InlineData("LICENSED REAL ESTATE VIRTUAL OFFICE", "Virtual office (ACTIVE)")]
    [InlineData("LICENSED REAL ESTATE PRE-LICENSE BRANCH", "Pre-license school branch (ACTIVE)")]
    [InlineData("REAL ESTATE EDUCATION PROVIDER VIRTUAL OFFICE", "Education provider virtual office (ACTIVE)")]
    public async Task ImportArchive_RecognizesEveryLocationKind(string description, string kind)
    {
        using var archive = Archive.Create(Quote(Header.Split(',')) + "\n" + Row("L", description, business: "Office"));
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await new IllinoisIdfprBulkImportService(db).ImportArchiveAsync(archive.Root);
        var location = await db.RegulatoryFirmLocations.SingleAsync();
        Assert.Equal(kind, location.Kind);
        Assert.Null(location.FirmId);
        Assert.Empty(await db.RegulatoryLicensees.ToListAsync());
        Assert.Empty(await db.RegulatoryFirms.ToListAsync());
    }

    [Fact]
    public async Task ValidateArchive_RejectsManifestRowCountMismatch()
    {
        using var archive = Archive.Create(Csv, declaredRows: 12);
        await Assert.ThrowsAsync<InvalidDataException>(() => IllinoisIdfprBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private static string Quote(IEnumerable<string> fields) => string.Join(',', fields.Select(field => "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    private static string Row(string number, string description, string profession = "REAL ESTATE", string status = "ACTIVE",
        string first = " Jane ", string middle = " Q ", string last = " Smith ", string suffix = " Jr ", string business = "",
        string trade = "", string issue = "05/01/2019", string effective = "05/01/2019", string expiration = "04/30/2026", string caseNumber = "") =>
        Quote([profession, description, number, status, "", "", first, middle, last, "", suffix, business, trade, issue, effective,
            expiration, "Chicago", "IL", "60601", "COOK", "", "", "", "", "", caseNumber, "", "", "", ""]) + "\n";

    private sealed class Archive : IDisposable
    {
        private Archive(string root) => Root = root;
        public string Root { get; }
        public string FilePath => Path.Combine(Root, IllinoisIdfprBulkImportService.FileName);

        public static Archive Create(string csv, long? declaredRows = null)
        {
            var archive = new Archive(Path.Combine(Directory.GetCurrentDirectory(), $"illinois-idfpr-test-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(archive.Root);
            var definition = IllinoisIdfprBulkImportService.Definitions[0];
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
