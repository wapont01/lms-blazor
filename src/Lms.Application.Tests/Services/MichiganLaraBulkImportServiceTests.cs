using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class MichiganLaraBulkImportServiceTests
{
    [Fact]
    public void ReadRecords_PreservesPublishedRolesStatusesAndDates()
    {
        using var archive = Archive.Create();
        var rows = MichiganLaraBulkImportService.ReadRecords(archive.Path(MichiganLaraBulkImportService.LicenseFile));
        Assert.Equal(5, rows.Count);
        Assert.Equal("Jane Q Smith Jr", rows[0].Name);
        Assert.Equal(new DateOnly(2026, 1, 1), rows[0].IssueDate);
        Assert.Equal("Suspended", rows[0].Status);
        Assert.Equal("10 Main St, Detroit, MI, 48201", rows[0].Address);
        Assert.Equal("Broker Individual", rows[2].LicenseType);
        Assert.True(MichiganLaraBulkImportService.IsPerson(rows[2]));
        Assert.True(MichiganLaraBulkImportService.IsFirm(rows[2]));
        Assert.Equal(3, MichiganLaraBulkImportService.CheckFile(archive.Path(MichiganLaraBulkImportService.EmployerFile),
            MichiganLaraBulkImportService.EmployerFile));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ReadRecords_RejectsChangedHeadersAndOtherProfessions(bool badHeader, bool badProfession)
    {
        using var archive = Archive.Create(badHeader: badHeader, badProfession: badProfession);
        Assert.Throws<InvalidDataException>(() => MichiganLaraBulkImportService.ReadRecords(archive.Path(MichiganLaraBulkImportService.LicenseFile)));
    }

    [Fact]
    public void ReadRecords_UsesPublishedPersonalOrFacilityNameWhenTheOtherIsBlank()
    {
        using var archive = Archive.Create(alternateNames: true);
        var rows = MichiganLaraBulkImportService.ReadRecords(archive.Path(MichiganLaraBulkImportService.LicenseFile));
        Assert.Equal("Jane Q Smith Jr", rows[1].Name);
        Assert.Equal("Published Realty", rows[2].Name);
        Assert.Equal("Jane Q Smith Jr", rows[3].Name);
    }

    [Fact]
    public async Task ImportArchive_IndexesPeopleLinksOnlyPublishedOwnersAndReusesSameSnapshot()
    {
        using var archive = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new MichiganLaraBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(3, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(2, await db.RegulatoryFirms.CountAsync());
        Assert.Equal(1, await db.RegulatoryFirmLocations.CountAsync());
        var link = await db.RegulatoryFirmLicenseeAffiliations.Include(row => row.Firm).Include(row => row.Licensee).SingleAsync();
        Assert.Equal("6505000001", link.Firm!.LicenseNumber);
        Assert.Equal("6501000001", link.Licensee!.LicenseNumber);
        Assert.Equal("jane@example.invalid", link.ContactEmail);
        Assert.Equal(new DateOnly(2026, 1, 1), link.RelationshipEffectiveDate);
        var branch = await db.RegulatoryFirmLocations.SingleAsync();
        Assert.Equal(link.FirmId, branch.FirmId);
        Assert.Equal("Published broker license", branch.MatchMethod);
        var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", id);
        Assert.Equal(3, (await db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
            """).ToListAsync()).Count);
        Assert.True(RegulatoryRoster.HasBulkProvider("Michigan"));
        Assert.Equal(MichiganLaraBulkImportService.ParserVersion, RegulatoryRoster.BulkParser("Michigan"));
        Assert.All(await db.RegulatoryLicensees.ToListAsync(), row => Assert.True(RegulatoryLicenseTypes.IsRealEstate("Michigan", row.LicenseType)));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("rows")]
    [InlineData("url")]
    [InlineData("missing")]
    public async Task ValidateArchive_RejectsIncompleteOrChangedEvidence(string change)
    {
        using var archive = Archive.Create();
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(archive.Path("manifest.json")))!;
        entries[0] = change switch
        {
            "hash" => entries[0] with { Sha256 = new string('0', 64) },
            "rows" => entries[0] with { Rows = 99, Columns = new() { ["20"] = 99 } },
            "url" => entries[0] with { SourceUrl = "https://example.invalid/roster" },
            _ => entries[0]
        };
        if (change == "missing") entries.RemoveAt(1);
        File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
        await Assert.ThrowsAsync<InvalidDataException>(() => MichiganLaraBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    [Fact]
    public async Task Statistics_CompareIdenticalRostersAndRestoreOnlyOnePublishedSnapshot()
    {
        using var first = Archive.Create();
        using var second = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new MichiganLaraBulkImportService(db);
        var firstId = await importer.ImportArchiveAsync(first.Root);
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(second.Path("manifest.json")))!;
        // XLSX generators change ZIP metadata between downloads even when license rows are unchanged.
        using (var zip = ZipFile.Open(second.Path(MichiganLaraBulkImportService.LicenseFile), ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(zip.CreateEntry("collection-note.txt").Open());
            writer.Write("Second download; same published license rows.");
        }
        var bytes = File.ReadAllBytes(second.Path(MichiganLaraBulkImportService.LicenseFile));
        entries[0] = entries[0] with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Bytes = bytes.Length };
        File.WriteAllText(second.Path("manifest.json"), JsonSerializer.Serialize(entries));
        var secondId = await importer.ImportArchiveAsync(second.Root);
        Assert.NotEqual(firstId, secondId);
        var statistics = await new RegulatoryStatisticsService(db).GetAsync("Michigan", bulkOnly: true);
        Assert.Equal(firstId, statistics.Baseline!.Id);
        Assert.Equal(secondId, statistics.Incoming!.Id);
        Assert.All(statistics.Professions, row =>
        {
            Assert.Equal(0, row.New);
            Assert.Equal(0, row.Changed);
            Assert.Equal(row.Collected, row.Unchanged);
        });
        Assert.Equal(firstId, await importer.ImportArchiveAsync(first.Root));
        Assert.Equal(firstId, await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == "Michigan"))
            .Select(row => row.Id).FirstAsync());
    }

    private sealed class Archive : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "michigan-test-" + Guid.NewGuid().ToString("N"));
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public static Archive Create(bool badHeader = false, bool badProfession = false, bool alternateNames = false)
        {
            var archive = new Archive();
            Directory.CreateDirectory(archive.Root);
            var headers = MichiganLaraBulkImportService.LicenseHeaders.ToArray();
            if (badHeader) headers[8] = "Changed";
            var rows = new List<string[]>
            {
                headers,
                License("6501000001", "Salesperson", "Suspended", badProfession ? "Other" : "Real Estate"),
                License("6505000001", "Broker Company"),
                License("6502000001", "Broker Individual"),
                License("6505000001BR0001", "Branch Office"),
                License("6501000002", "Salesperson")
            };
            if (alternateNames)
            {
                rows[2][4] = "";
                rows[4][4] = "";
                for (var i = 0; i < 4; i++) rows[3][i] = "";
            }
            WriteWorkbook(archive.Path(MichiganLaraBulkImportService.LicenseFile), rows);
            WriteWorkbook(archive.Path(MichiganLaraBulkImportService.EmployerFile),
            [
                ["\"Active\" - 'Salesperson' Licenses and their associated Broker Information as on 11/03/2026"],
                MichiganLaraBulkImportService.EmployerHeaders,
                Employer("6501000001", "6505000001", "Real Estate Salesperson License"),
                Employer("6505000001BR0001", "6505000001", "Real Estate Branch Office License"),
                Employer("6501000002", "", "Real Estate Salesperson License")
            ]);
            var entries = MichiganLaraBulkImportService.Definitions.Select(definition =>
            {
                var bytes = File.ReadAllBytes(archive.Path(definition.Name));
                long count = definition.Name == MichiganLaraBulkImportService.LicenseFile ? 5 : 3;
                return new FloridaBulkManifestEntry(definition.Name, count, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
                    definition.Url, DateTimeOffset.UtcNow, "test", new() { [definition.Columns.ToString()] = count });
            }).ToList();
            File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
            return archive;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static string[] License(string number, string type, string status = "Active", string profession = "Real Estate") =>
        ["Smith", "Jane", "Q", "Jr", "Published Realty", profession, type, "", number, status, "2026-01-01", "2027-01-01",
            "10 Main St", "", "", "Detroit", "MI", "48201", "Wayne", "jane@example.invalid"];
    private static string[] Employer(string license, string broker, string type) =>
        [license, "2026-01-01", type, "Jane", "Q", "Smith", "Jr", "", "2027-01-01", "Active",
            "Detroit", "48201", "2026-01-01", "Published Realty", broker, "2027-01-01", "Active", "Detroit", "48201"];
    private static void WriteWorkbook(string path, IEnumerable<string[]> rows)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write("[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
            """);
        Write("_rels/.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        Write("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Roster" sheetId="1" r:id="rId1"/></sheets></workbook>""");
        Write("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var data = new XElement(ns + "sheetData", rows.Select((row, index) => new XElement(ns + "row",
            new XAttribute("r", index + 1), row.Select((value, column) => new XElement(ns + "c",
                new XAttribute("r", $"{(char)('A' + column)}{index + 1}"), new XAttribute("t", "inlineStr"),
                new XElement(ns + "is", new XElement(ns + "t", value)))))));
        Write("xl/worksheets/sheet1.xml", new XElement(ns + "worksheet", data).ToString());
        void Write(string name, string contents) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(contents); }
    }
}
