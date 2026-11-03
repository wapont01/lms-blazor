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

public sealed class VermontOprBulkImportServiceTests
{
    [Fact]
    public void Parser_PreservesSourceFactsAndAcceptsHeaderOnlyCategories()
    {
        using var archive = Archive.Create();
        var rows = VermontOprBulkImportService.ReadRecords(archive.Path("NGLP082.xlsx"), "NGLP082.xlsx");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Jane Q Smith", rows[0].Name);
        Assert.Equal("Renewal Extension", rows[0].Status);
        Assert.Equal(new DateOnly(1970, 1, 1), rows[0].IssueDate);
        Assert.Equal(new DateOnly(2026, 6, 1), rows[0].EffectiveDate);
        Assert.Equal(new DateOnly(2028, 5, 31), rows[0].ExpirationDate);
        Assert.Equal("10 Main St, Montpelier, VT, US, 05602", rows[0].Address);
        Assert.Equal(0, VermontOprBulkImportService.CheckFile(archive.Path("NGLP081-EMGY.xlsx"), "NGLP081-EMGY.xlsx"));
        var selected = Assert.Single(VermontOprBulkImportService.SelectCurrent(rows));
        Assert.Equal(rows[0], selected);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("type")]
    [InlineData("date")]
    [InlineData("status")]
    [InlineData("name")]
    [InlineData("duplicate")]
    public void CheckFile_PerformsSemanticValidation(string change)
    {
        using var archive = Archive.Create();
        var headers = VermontOprBulkImportService.PersonHeaders.ToArray();
        var row = Person("082.0000001");
        var rows = new List<string[]> { headers, row };
        switch (change)
        {
            case "header": headers[0] = "License Number"; break;
            case "type": row[0] = "081.0000001"; break;
            case "date": row[11] = "unknown"; break;
            case "status": row[1] = ""; break;
            case "name": row[2] = row[3] = row[4] = ""; break;
            case "duplicate":
                var second = row.ToArray();
                second[1] = "Suspended";
                rows.Add(second);
                break;
        }
        File.Delete(archive.Path("NGLP082.xlsx"));
        WriteWorkbook(archive.Path("NGLP082.xlsx"), rows);
        Assert.Throws<InvalidDataException>(() => VermontOprBulkImportService.CheckFile(archive.Path("NGLP082.xlsx"), "NGLP082.xlsx"));
    }

    [Fact]
    public void SelectCurrent_RejectsUndatedConflictsAndChangedIdentityButAllowsExactDuplicates()
    {
        var current = new VermontOprRecord("082.0000001", "Jane Smith", "Real Estate Salesperson", "Active",
            null, new DateOnly(2026, 6, 1), new DateOnly(2028, 5, 31), null, null);
        Assert.Single(VermontOprBulkImportService.SelectCurrent([current, current]));
        Assert.Throws<InvalidDataException>(() => VermontOprBulkImportService.SelectCurrent([current, current with { EffectiveDate = null }]));
        Assert.Throws<InvalidDataException>(() => VermontOprBulkImportService.SelectCurrent([current, current with { Name = "Other Person", EffectiveDate = new(2024, 6, 1) }]));
        var latestInactive = current with { Status = "Inactive per Request", EffectiveDate = new(2026, 6, 7), ExpirationDate = new(2027, 1, 1) };
        Assert.Equal(latestInactive, Assert.Single(VermontOprBulkImportService.SelectCurrent([current, latestInactive])));
    }

    [Fact]
    public void Parser_PreservesPublishedLegacySuffixesAndBlankHistoricalEntityNames()
    {
        using var archive = Archive.Create();
        var row = Entity("083.0650411MAIN");
        row[2] = row[3] = "";
        File.Delete(archive.Path("NGLP083-MAIN.xlsx"));
        WriteWorkbook(archive.Path("NGLP083-MAIN.xlsx"), [VermontOprBulkImportService.EntityHeaders, row]);
        var record = Assert.Single(VermontOprBulkImportService.ReadRecords(archive.Path("NGLP083-MAIN.xlsx"), "NGLP083-MAIN.xlsx"));
        Assert.Equal("083.0650411MAIN", record.LicenseNumber);
        Assert.Equal("", record.Name);
        Assert.Null(record.BranchName);
    }

    [Fact]
    public async Task ValidateArchive_RejectsAnEntirelyEmptyArchiveButNotEmptyCategories()
    {
        using var archive = Archive.Create(empty: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => VermontOprBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("bytes")]
    [InlineData("rows")]
    [InlineData("columns")]
    [InlineData("url")]
    [InlineData("verified")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public async Task ValidateArchive_RejectsChangedOrIncompleteEvidence(string change)
    {
        using var archive = Archive.Create();
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(archive.Path("manifest.json")))!;
        entries[0] = change switch
        {
            "hash" => entries[0] with { Sha256 = new string('0', 64) },
            "bytes" => entries[0] with { Bytes = entries[0].Bytes + 1 },
            "rows" => entries[0] with { Rows = 99, Columns = new() { ["16"] = 99 } },
            "columns" => entries[0] with { Columns = new() { ["17"] = entries[0].Rows } },
            "url" => entries[0] with { SourceUrl = "https://example.invalid" },
            "verified" => entries[0] with { VerifiedAtUtc = default },
            _ => entries[0]
        };
        if (change == "missing") entries.RemoveAt(0);
        if (change == "duplicate") entries[1] = entries[0];
        File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
        await Assert.ThrowsAsync<InvalidDataException>(() => VermontOprBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    [Fact]
    public async Task ImportArchive_IsIdempotentIndexesPeopleAndDoesNotInferOwnershipOrBic()
    {
        using var archive = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new VermontOprBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(4, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(2, await db.RegulatoryFirms.CountAsync());
        Assert.Empty(await db.RegulatoryFirmLicenseeAffiliations.ToListAsync());
        var branch = Assert.Single(await db.RegulatoryFirmLocations.ToListAsync());
        Assert.Null(branch.FirmId);
        Assert.Equal("Downtown", branch.Name);
        Assert.Equal("Branch office (Expired)", branch.Kind);
        var people = await db.RegulatoryLicensees.ToListAsync();
        Assert.All(people, person => { Assert.False(person.IsBicEligible); Assert.False(person.IsRestricted); });
        Assert.Contains(people, person => person.SourceLicenseType == "Real Estate Salesperson - Emergency Temporary License");
        var expression = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", id);
        Assert.Equal(4, (await db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {expression}
            """).ToListAsync()).Count);
    }

    [Fact]
    public async Task ImportArchive_RollsBackInterruptedStaging()
    {
        using var archive = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new VermontOprBulkImportService(db);
        await Assert.ThrowsAsync<OperationCanceledException>(() => importer.ImportArchiveAsync(archive.Root,
            staged: _ => throw new OperationCanceledException()));
        Assert.Empty(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Empty(await db.RegulatoryLicensees.ToListAsync());
        await using var countIndex = connection.CreateCommand();
        countIndex.CommandText = "SELECT COUNT(*) FROM RegulatoryLicenseeNameSearch";
        Assert.Equal(0L, Convert.ToInt64(await countIndex.ExecuteScalarAsync()));
        await importer.ImportArchiveAsync(archive.Root);
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(4L, Convert.ToInt64(await countIndex.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Statistics_CompareSameDataWithChangedHashAndKeepFtsScopedWhenRestoringOrDeletingSnapshots()
    {
        using var first = Archive.Create();
        using var second = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new VermontOprBulkImportService(db);
        var firstId = await importer.ImportArchiveAsync(first.Root);
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(second.Path("manifest.json")))!;
        using (var zip = ZipFile.Open(second.Path("NGLP082.xlsx"), ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(zip.CreateEntry("collection-note.txt").Open());
            writer.Write("Different workbook metadata; identical official license rows.");
        }
        var bytes = File.ReadAllBytes(second.Path("NGLP082.xlsx"));
        var index = entries.FindIndex(entry => entry.File == "NGLP082.xlsx");
        entries[index] = entries[index] with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), Bytes = bytes.Length };
        File.WriteAllText(second.Path("manifest.json"), JsonSerializer.Serialize(entries));
        var secondId = await importer.ImportArchiveAsync(second.Root);
        Assert.NotEqual(firstId, secondId);
        var statistics = await new RegulatoryStatisticsService(db).GetAsync("Vermont", bulkOnly: true);
        Assert.Equal(firstId, statistics.Baseline!.Id);
        Assert.Equal(secondId, statistics.Incoming!.Id);
        Assert.NotEmpty(statistics.Professions);
        Assert.All(statistics.Professions, row =>
        {
            Assert.Equal(0, row.New);
            Assert.Equal(0, row.Changed);
            Assert.Equal(row.Collected, row.Unchanged);
        });
        Assert.Equal(4, await SearchCountAsync(firstId));
        Assert.Equal(4, await SearchCountAsync(secondId));
        Assert.Equal(firstId, await importer.ImportArchiveAsync(first.Root));
        Assert.Equal(firstId, await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == "Vermont"))
            .Select(row => row.Id).FirstAsync());
        Assert.Equal(8, await db.RegulatoryLicensees.CountAsync());
        await db.RegulatoryDataSnapshots.Where(row => row.Id == secondId).ExecuteDeleteAsync();
        Assert.Equal(0, await SearchCountAsync(secondId));
        Assert.Equal(4, await SearchCountAsync(firstId));
        await using var countIndex = connection.CreateCommand();
        countIndex.CommandText = "SELECT COUNT(*) FROM RegulatoryLicenseeNameSearch";
        Assert.Equal(4L, Convert.ToInt64(await countIndex.ExecuteScalarAsync()));

        async Task<int> SearchCountAsync(Guid snapshotId)
        {
            var expression = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", snapshotId);
            return (await db.RegulatoryLicensees.FromSqlInterpolated($"""
                SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
                WHERE f.RegulatoryLicenseeNameSearch MATCH {expression}
                """).ToListAsync()).Count;
        }
    }

    private sealed class Archive : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "vermont-test-" + Guid.NewGuid().ToString("N"));
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public static Archive Create(bool empty = false)
        {
            var archive = new Archive();
            Directory.CreateDirectory(archive.Root);
            var entries = new List<FloridaBulkManifestEntry>();
            foreach (var file in VermontOprBulkImportService.Definitions)
            {
                var code = VermontOprBulkImportService.ProfessionCode(file.Name);
                var entity = file.Columns == 16;
                var rows = new List<string[]> { entity ? VermontOprBulkImportService.EntityHeaders : VermontOprBulkImportService.PersonHeaders };
                switch (code)
                {
                    case "NGLP083-MAIN": rows.Add(Entity("083.0000001-MAIN")); break;
                    case "NGLP083-SOLE": rows.Add(Entity("083.0000002-SOLE")); break;
                    case "NGLP083-BRCH": rows.Add(Entity("083.0000003-BRCH")); break;
                    case "NGLP081": rows.Add(Person("081.0000001")); break;
                    case "NGLP081-TEMP": rows.Add(Person("081.0000002-TEMP")); break;
                    case "NGLP082-EMGY": rows.Add(Person("082.0000002-EMGY")); break;
                    case "NGLP082":
                        rows.Add(Person("082.0000001"));
                        var old = Person("082.0000001");
                        old[1] = "Expired"; old[12] = "06/01/2024"; old[13] = "05/31/2026";
                        rows.Add(old);
                        break;
                }
                if (empty) rows.RemoveRange(1, rows.Count - 1);
                var count = rows.Count - 1;
                if (count == 0) rows.Add(rows[0].ToArray());
                WriteWorkbook(archive.Path(file.Name), rows);
                var bytes = File.ReadAllBytes(archive.Path(file.Name));
                entries.Add(new(file.Name, count, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
                    file.Url, DateTimeOffset.UtcNow, "test", new() { [file.Columns.ToString()] = count }));
            }
            File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
            return archive;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private static string[] Person(string number) =>
        [number, "Renewal Extension", "Jane", "Smith", "Q", "10 Main St", "", "Montpelier", "VT", "US", "05602",
            "01/01/1970", "06/01/2026", "05/31/2028", "", "Published supervisor", "Published Realty"];
    private static string[] Entity(string number) =>
        [number, "Expired", "Published Realty", "Downtown", "10 Main St", "", "Montpelier", "VT", "US", "05602",
            "01/01/1970", "04/01/2024", "03/31/2026", "", "Published Realty", "Published supervisor"];
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
