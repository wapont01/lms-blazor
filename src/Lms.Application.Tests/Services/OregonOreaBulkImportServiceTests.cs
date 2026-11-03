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

public sealed class OregonOreaBulkImportServiceTests
{
    // Layouts copied from OREA downloads: split header labels, empty spacer columns and a trailing comma on every line.
    private const string BusinessesCsv =
        "Business,Name,Address,Line 1,Address Line,2,Name of,City,State,Zip,Code,County,Business Phone,Number(s),Registration Type,Business License,Number,Expiration Date,\r\n"
        + "\tREALTY OF AMERICA LLC,,\"555 SE Martin Luther King, Jr. Blvd. Suite 105\",,,,Portland,,OR,97214,,Multnomah,(833) 256-4244,,RBN,201259724,,02/28/2027,\r\n"
        + "ABBAS REAL ESTATE LLC,,6827 NE 33RD ST,,PO BOX 64,,REDMOND,,OR,97756,,Deschutes,,,RBN,200312244,,12/31/2027,\r\n"
        + "ABBAS REAL ESTATE LLC,,100 MAIN ST,,,,BEND,,OR,97701,,Deschutes,,,RBO,201300001,,01/01/1900,\r\n"
        + "ACME ESCROW LLC,,1 ESCROW WAY,,,,SALEM,,OR,97301,,Marion,,,EA,201400001,,06/30/2027,\r\n";

    private const string IndividualHeader =
        "Last Name,-,First Name,--,Middle,---,Street,Address Line 1,Street Address,Line 2,City,----,State,Zip,-----,County Name,-------,License Type,License Number,--------,License Expiration,-----=---,License Status,";

    private const string ActiveCsv =
        IndividualHeader + "Business Name,--------------,Business License,-----------------,Licensee Associated to RBN Date,Key Manager,-------------------------,Key Manager Credential Number,\r\n"
        + "ABBAS,,LINDA,,S,,6827 NE 33RD ST,,,,REDMOND,,OR,97756-0000,,Deschutes,,PB,931200129,,04-30-2028,,ACTIVE,\"ABBAS REAL ESTATE, LLC\",,200312244,,12-30-2003,LINDA S ABBAS,,931200129,\r\n"
        + "ABBAS,,LINDA,,S,,6827 NE 33RD ST,,,,REDMOND,,OR,97756-0000,,Deschutes,,PM,931200129,,04-30-2028,,ACTIVE,REALTY OF AMERICA LLC,,201259724,,05-08-2023,JOE KEY,,201000001,\r\n"
        + "KEY,,JOE,,,,1 MAIN ST,,Apt 2,,PORTLAND,,OR,97214,,Multnomah,,PB,201000001,,09-30-2027,,ACTIVE,REALTY OF AMERICA LLC,,201259724,,01-15-2020,JOE KEY,,201000001,\r\n"
        + "BAXTER,,KARISTEN,,,,2 MAIN ST,,,,PORTLAND,,OR,97214,,Multnomah,,PB,201240974,,09-30-2028,,ACTIVE,REALTY OF AMERICA LLC,,201259724,,11-20-2025,JOE KEY,,201000001,\r\n";

    private const string InactiveCsv =
        IndividualHeader + "\r\n"
        + "Baxter,,Karisten,,,,2 MAIN ST,,,,PORTLAND,,OR,97214,,Multnomah,,B,201240974,,09-30-2028,,INACTIVE,\r\n"
        + "abdullah,,haider,,ali,,19243 ne Clackamas st,,,,Portland,,OR,97230,,Baker,,B,201257029,,04-30-2028,,INACTIVE,\r\n";

    [Fact]
    public void ReadBusinesses_ReadsValuesByPositionAndTreats1900AsNoDate()
    {
        using var archive = Archive.Create(BusinessesCsv, ActiveCsv, InactiveCsv);

        var records = OregonOreaBulkImportService.ReadBusinesses(archive.Path(0));

        Assert.Equal(4, records.Count);
        Assert.Equal("REALTY OF AMERICA LLC", records[0].Name);
        Assert.Equal("555 SE Martin Luther King, Jr. Blvd. Suite 105, Portland, OR 97214", records[0].Address);
        Assert.Equal(new DateOnly(2027, 2, 28), records[0].ExpirationDate);
        Assert.Equal("6827 NE 33RD ST, PO BOX 64, REDMOND, OR 97756", records[1].Address);
        Assert.Null(records[2].ExpirationDate);
        Assert.Equal("BEND", records[2].City);
        Assert.Equal(["RBN", "RBN", "EA"], OregonOreaBulkImportService.SelectFirms(records).Select(firm => firm.RegistrationType));
        Assert.Equal(RegulatoryIndustries.Escrow, OregonOreaBulkImportService.FirmIndustry(records[3]));
        var location = Assert.Single(OregonOreaBulkImportService.SelectLocations(records));
        Assert.Equal(("201300001", "200312244", RegulatoryLocationMatchMethods.ExactName),
            (location.Branch.LicenseNumber, location.FirmLicenseNumber, location.MatchMethod));
    }

    [Fact]
    public void SelectLocations_MatchesBranchesByExactThenNormalizedThenClosestNamePreferringSameCity()
    {
        static OregonOreaBusiness Business(long row, string number, string name, string type, string city) =>
            new(row, number, name, type, null, null, city);
        var records = new[]
        {
            Business(1, "100", "JOHN L SCOTT", "RBN", "PORTLAND"),
            Business(2, "101", "JOHN L SCOTT", "RBN", "BEND"),
            Business(3, "200", "Smith & Jones Realty, LLC", "RBN", "SALEM"),
            Business(4, "300", "CASCADE HOMES GROUP", "RBN", "EUGENE"),
            Business(5, "900", "JOHN L SCOTT", "RBO", "BEND"),
            Business(6, "901", "SMITH AND JONES REALTY RBO", "RBO", "SALEM"),
            Business(7, "902", "CASCADE HOME GROUP", "RBO", "EUGENE")
        };

        var locations = OregonOreaBulkImportService.SelectLocations(records);

        Assert.Equal(
            [("900", "101", RegulatoryLocationMatchMethods.ExactName),
                ("901", "200", RegulatoryLocationMatchMethods.NormalizedName),
                ("902", "300", RegulatoryLocationMatchMethods.ClosestName)],
            locations.Select(item => (item.Branch.LicenseNumber, item.FirmLicenseNumber, item.MatchMethod)));
    }

    [Fact]
    public void ReadRows_RejectsChangedHeadersAndValuesInSpacerColumns()
    {
        using var renamed = Archive.Create(BusinessesCsv.Replace("Registration Type", "Type", StringComparison.Ordinal), ActiveCsv, InactiveCsv);
        Assert.Throws<InvalidDataException>(() => OregonOreaBulkImportService.ReadBusinesses(renamed.Path(0)));

        using var shifted = Archive.Create(BusinessesCsv, ActiveCsv, InactiveCsv.Replace("Baxter,,Karisten", "Baxter,X,Karisten", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => OregonOreaBulkImportService.ReadIndividuals(shifted.Path(2), active: false));
    }

    [Fact]
    public void SelectLicensees_KeepsOneLicensePreferringActiveHighestTypeAndLatestAssociation()
    {
        using var archive = Archive.Create(BusinessesCsv, ActiveCsv, InactiveCsv);
        var active = OregonOreaBulkImportService.ReadIndividuals(archive.Path(1), active: true);
        var inactive = OregonOreaBulkImportService.ReadIndividuals(archive.Path(2), active: false);

        var people = OregonOreaBulkImportService.SelectLicensees(active, inactive);

        Assert.Equal(["931200129", "201000001", "201240974", "201257029"], people.Select(person => person.Record.LicenseNumber));
        var linda = people[0];
        Assert.Equal("LINDA S ABBAS", linda.Record.Name);
        Assert.Equal("PB", linda.Record.LicenseTypeCode);
        Assert.Equal("PB, PM", linda.LicenseTypeCodes);
        Assert.Equal("201259724", linda.Association?.BusinessLicenseNumber);
        Assert.Equal("Active", people[2].Record.LicenseStatus);
        Assert.Equal("haider ali abdullah", people[3].Record.Name);
        Assert.Equal("Inactive", people[3].Record.LicenseStatus);
        Assert.Null(people[3].Association);
    }

    [Fact]
    public async Task ImportArchive_StoresFirmsPeopleAndKeyManagerAffiliationsIdempotently()
    {
        using var archive = Archive.Create(BusinessesCsv, ActiveCsv, InactiveCsv);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new OregonOreaBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);

        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync();
        Assert.Equal("Oregon", snapshot.State);
        Assert.Equal(OregonOreaBulkImportService.ParserVersion, snapshot.ParserVersion);
        Assert.NotNull(snapshot.ActivatedAtUtc);

        var firms = await db.RegulatoryFirms.OrderBy(firm => firm.LicenseNumber).ToListAsync();
        Assert.Equal(["200312244", "201259724", "201400001"], firms.Select(firm => firm.LicenseNumber));
        Assert.Equal("LINDA S ABBAS", firms[0].QualifyingBrokerName);
        Assert.Equal("JOE KEY", firms[1].QualifyingBrokerName);
        Assert.Equal("Active", firms[1].LicenseStatus);
        Assert.Null(firms[1].TradeName);
        Assert.Equal(RegulatoryIndustries.RealEstate, firms[1].Industry);
        Assert.Equal(RegulatoryIndustries.Escrow, firms[2].Industry);
        Assert.Equal(new DateOnly(2027, 6, 30), firms[2].LicenseRenewalDate);

        var branch = await db.RegulatoryFirmLocations.SingleAsync();
        Assert.Equal(("201300001", firms[0].Id, OregonOreaBulkImportService.BranchOfficeKind, RegulatoryLocationMatchMethods.ExactName),
            (branch.RegistrationNumber, branch.FirmId, branch.Kind, branch.MatchMethod));
        Assert.Equal("100 MAIN ST, BEND, OR 97701", branch.Address);

        var people = await db.RegulatoryLicensees.ToListAsync();
        Assert.Equal(4, people.Count);
        var linda = people.Single(person => person.LicenseNumber == "931200129");
        Assert.Equal("Principal Broker", linda.LicenseType);
        Assert.Equal("PB, PM", linda.SourceLicenseType);
        Assert.Equal("Active", linda.LicenseStatus);
        Assert.Equal(new DateOnly(2028, 4, 30), linda.LicenseRenewalDate);
        Assert.Equal("201259724", linda.RelatedLicenseNumber);
        Assert.Equal("Registered Business Name", linda.RelatedLicenseType);
        Assert.Equal("REALTY OF AMERICA LLC", linda.RelatedLicenseName);
        Assert.Equal("Inactive", people.Single(person => person.LicenseNumber == "201257029").LicenseStatus);

        var affiliations = await db.RegulatoryFirmLicenseeAffiliations.ToListAsync();
        Assert.Equal(4, affiliations.Count);
        Assert.Single(affiliations, affiliation => affiliation.LicenseeId == linda.Id && affiliation.FirmId == firms[0].Id
            && affiliation.IsBrokerInCharge && affiliation.RelationshipEffectiveDate == new DateOnly(2003, 12, 30));
        Assert.Single(affiliations, affiliation => affiliation.LicenseeId == linda.Id && affiliation.FirmId == firms[1].Id
            && !affiliation.IsBrokerInCharge);
        var joe = people.Single(person => person.LicenseNumber == "201000001").Id;
        Assert.Single(affiliations, affiliation => affiliation.LicenseeId == joe && affiliation.FirmId == firms[1].Id
            && affiliation.IsBrokerInCharge);

        Assert.True(RegulatoryRoster.HasBulkProvider("Oregon"));
        Assert.Equal(OregonOreaBulkImportService.ParserVersion, RegulatoryRoster.BulkParser("Oregon"));
    }

    [Fact]
    public async Task ImportArchive_ReusesSnapshotWhenSourceReordersRows()
    {
        static string Reverse(string csv)
        {
            var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            return string.Join("\r\n", lines.Take(1).Concat(lines.Skip(1).Reverse())) + "\r\n";
        }
        using var archive = Archive.Create(BusinessesCsv, ActiveCsv, InactiveCsv);
        using var reordered = Archive.Create(Reverse(BusinessesCsv), Reverse(ActiveCsv), InactiveCsv);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new OregonOreaBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);

        Assert.Equal(id, await importer.ImportArchiveAsync(reordered.Root));
        Assert.Equal(1, await db.RegulatoryDataSnapshots.CountAsync());

        await using var otherConnection = new SqliteConnection("Data Source=:memory:");
        await otherConnection.OpenAsync();
        await using var otherDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(otherConnection).Options);
        await otherDb.Database.EnsureCreatedAsync();
        Assert.Equal(id, await new OregonOreaBulkImportService(otherDb).ImportArchiveAsync(reordered.Root));
        Assert.Equal(await Content(db), await Content(otherDb));
    }

    private static async Task<string> Content(ApplicationDbContext db)
    {
        var people = await db.RegulatoryLicensees.Select(person => $"{person.LicenseNumber}|{person.Name}|{person.LicenseType}|{person.SourceLicenseType}|{person.LicenseStatus}|{person.LicenseRenewalDate}|{person.RelatedLicenseNumber}|{person.RelatedLicenseName}").ToListAsync();
        var firms = await db.RegulatoryFirms.Select(firm => $"{firm.LicenseNumber}|{firm.LegalName}|{firm.TradeName}|{firm.Address}|{firm.LicenseRenewalDate}|{firm.QualifyingBrokerName}|{firm.Industry}").ToListAsync();
        var locations = await db.RegulatoryFirmLocations.Select(location => $"{location.RegistrationNumber}|{location.Name}|{location.Address}|{location.Firm!.LicenseNumber}|{location.MatchMethod}").ToListAsync();
        var edges = await db.RegulatoryFirmLicenseeAffiliations.Select(edge => $"{edge.Firm!.LicenseNumber}|{edge.Licensee!.LicenseNumber}|{edge.RelationshipEffectiveDate}|{edge.IsBrokerInCharge}").ToListAsync();
        return string.Join("\n", people.Order(StringComparer.Ordinal).Concat(firms.Order(StringComparer.Ordinal))
            .Concat(locations.Order(StringComparer.Ordinal)).Concat(edges.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task ValidateArchive_RejectsManifestRowCountMismatch()
    {
        using var archive = Archive.Create(BusinessesCsv, ActiveCsv, InactiveCsv, inactiveRows: 3);

        await Assert.ThrowsAsync<InvalidDataException>(() => OregonOreaBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private sealed class Archive : IDisposable
    {
        private Archive(string root) => Root = root;
        public string Root { get; }
        public string Path(int index) => System.IO.Path.Combine(Root, OregonOreaBulkImportService.Definitions[index].RelativePath);

        public static Archive Create(string businesses, string active, string inactive, long? inactiveRows = null)
        {
            var archive = new Archive(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"or-orea-test-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(archive.Root);
            var manifest = new[]
            {
                Write(archive.Root, OregonOreaBulkImportService.Definitions[0], businesses, null),
                Write(archive.Root, OregonOreaBulkImportService.Definitions[1], active, null),
                Write(archive.Root, OregonOreaBulkImportService.Definitions[2], inactive, inactiveRows)
            };
            File.WriteAllText(System.IO.Path.Combine(archive.Root, "manifest.json"), JsonSerializer.Serialize(manifest));
            return archive;
        }

        private static FloridaBulkManifestEntry Write(string root, FloridaBulkFileDefinition definition, string csv, long? declaredRows)
        {
            var bytes = new UTF8Encoding(false).GetBytes(csv);
            File.WriteAllBytes(System.IO.Path.Combine(root, definition.RelativePath), bytes);
            var rows = declaredRows ?? csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length - 1;
            return new(definition.RelativePath, rows, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), definition.Url,
                DateTimeOffset.UtcNow, "test", new() { [definition.Columns.ToString()] = rows });
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
