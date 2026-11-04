using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class IdahoDoplBulkImportServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_AcceptsHistoricalCsvAndOriginalOdsPreservingAllPublishedFacts(bool ods)
    {
        using var archive = Archive.Create(ods: ods);
        var rows = IdahoDoplBulkImportService.ReadRecords(archive.Path(IdahoDoplBulkImportService.FileName));
        Assert.Equal(9, rows.Count);
        Assert.Equal("Jane Marie Smith JR", rows[0].Name);
        Assert.Equal("7271394", rows[2].LicenseNumber);
        Assert.Equal(new DateOnly(2019, 3, 8), rows[0].OriginalIssueDate);
        Assert.Equal(new DateOnly(2027, 3, 8), rows[0].ExpirationDate);
        Assert.Equal("Published, \"Realty\"", rows[3].Name);
        Assert.Equal("Public DBA", rows[3].TradeName);
        Assert.Equal("68 MILL ST, SUITE 2, MIDDLETON, MASSACHUSETTS, 01949, USA", rows[3].Address);
        Assert.Equal("Yes", rows[0].Fields[11]);
        Assert.Equal(3, rows.Count(IdahoDoplBulkImportService.IsEvidence));
    }

    [Fact]
    public void Reader_PreservesPublishedPersonalNamesForSoleProprietorCompanyCredentials()
    {
        using var archive = Archive.Create("sole-proprietor");
        var rows = IdahoDoplBulkImportService.ReadRecords(archive.Path(IdahoDoplBulkImportService.FileName));
        Assert.Equal("Jane Marie Smith JR", rows[3].Name);
        Assert.Equal(IdahoDoplBulkImportService.CompanyType, rows[3].SourceLicenseType);
        Assert.False(IdahoDoplBulkImportService.IsPerson(rows[3]));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("columns")]
    [InlineData("status")]
    [InlineData("type")]
    [InlineData("discipline")]
    [InlineData("date")]
    [InlineData("number")]
    [InlineData("person-name")]
    [InlineData("company-name")]
    [InlineData("person-conflict")]
    [InlineData("alias-conflict")]
    [InlineData("cross-type")]
    public void Reader_RejectsChangedSchemaUnsupportedTypesAndConflictingIdentities(string change)
    {
        using var archive = Archive.Create(change);
        Assert.Throws<InvalidDataException>(() => IdahoDoplBulkImportService.CheckFile(archive.Path(IdahoDoplBulkImportService.FileName)));
    }

    [Theory]
    [InlineData("repeated")]
    [InlineData("formula")]
    [InlineData("sheets")]
    [InlineData("stored-date")]
    [InlineData("number-cell")]
    [InlineData("doctype")]
    public void Reader_RejectsUnsupportedOrUnsafeOdsLayouts(string change)
    {
        using var archive = Archive.Create(change, ods: true);
        Assert.Throws<InvalidDataException>(() => IdahoDoplBulkImportService.CheckFile(archive.Path(IdahoDoplBulkImportService.FileName)));
    }

    [Fact]
    public async Task Import_DeduplicatesAliasesRetainsEvidenceAndNeverInfersCompanyOwnershipOrBrokerAffiliations()
    {
        using var archive = Archive.Create(ods: true);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        var importer = new IdahoDoplBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(9, await db.RegulatorySourceRecords.CountAsync());
        Assert.Equal(3, await db.RegulatoryLicensees.CountAsync());
        var firm = await db.RegulatoryFirms.SingleAsync();
        Assert.Equal("Public DBA", firm.TradeName);
        Assert.Equal(RegulatoryIndustries.RealEstate, firm.Industry);
        Assert.Null(firm.QualifyingBrokerName);
        Assert.Equal(new DateOnly(2019, 3, 8), firm.LicenseIssuedDate);
        var branch = await db.RegulatoryFirmLocations.SingleAsync();
        Assert.Equal(firm.LegalName, branch.Name);
        Assert.Null(branch.FirmId);
        Assert.Equal(RegulatoryLocationMatchMethods.NoMatch, branch.MatchMethod);
        Assert.Empty(await db.RegulatoryFirmLicenseeAffiliations.ToListAsync());
        var broker = await db.RegulatoryLicensees.SingleAsync(row => row.LicenseNumber == "DB37481");
        Assert.Equal("Broker", broker.LicenseType);
        Assert.Equal("Real Estate Broker License", broker.SourceLicenseType);
        Assert.Equal(new DateOnly(2019, 3, 8), broker.OriginalLicenseDate);
        Assert.Null(broker.LicenseIssuedDate);
        Assert.False(broker.IsBicEligible);
        Assert.False(broker.IsRestricted);
        var evidence = await db.RegulatorySourceRecords.Where(row => row.LicenseNumber == "CO30048").ToListAsync();
        Assert.Equal(2, evidence.Count);
        Assert.Contains(evidence, row => row.SourceRecordJson.Contains("Other public alias", StringComparison.Ordinal));
        var expression = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", id);
        Assert.Single(await db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {expression}
            """).ToListAsync());
    }

    [Fact]
    public async Task Import_RestoresPreviousCurrentSnapshotAndRejectsIncompleteExistingImports()
    {
        using var first = Archive.Create();
        using var second = Archive.Create("changed");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        var importer = new IdahoDoplBulkImportService(db);
        var firstId = await importer.ImportArchiveAsync(first.Root);
        var secondId = await importer.ImportArchiveAsync(second.Root);
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(firstId, await importer.ImportArchiveAsync(first.Root));
        Assert.Equal(firstId, await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == "Idaho"))
            .Select(row => row.Id).FirstAsync());
        await db.RegulatorySourceRecords.Where(row => row.SnapshotId == firstId).Take(1).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportArchiveAsync(first.Root));
    }

    [Fact]
    public async Task Import_CancellationRollsBackStagedSnapshotAndFtsRows()
    {
        using var archive = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IdahoDoplBulkImportService(db)
            .ImportArchiveAsync(archive.Root, cancellation.Token, staged: total => { if (total > 9) cancellation.Cancel(); }));
        Assert.Empty(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Empty(await db.RegulatorySourceRecords.ToListAsync());
        Assert.Empty(await db.RegulatoryLicensees.ToListAsync());
        Assert.Empty(await db.Database.SqlQueryRaw<string>("SELECT Name AS Value FROM RegulatoryLicenseeNameSearch").ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastRun_UsesSemanticChangesAndSnapshotScopedSearchRatherThanArchiveHash(bool changed)
    {
        using var first = Archive.Create();
        using var second = Archive.Create(changed ? "changed" : "different-hash");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Database(connection);
        await db.Database.EnsureCreatedAsync();
        var importer = new IdahoDoplBulkImportService(db);
        var previous = await importer.ImportArchiveAsync(first.Root);
        var incoming = await importer.ImportArchiveAsync(second.Root);
        Assert.NotEqual(previous, incoming);
        var statistics = await new RegulatoryStatisticsService(db, lastRuns: new LastRunSource(previous, incoming)).GetAsync("Idaho", bulkOnly: true);
        Assert.Equal(previous, statistics.Baseline!.Id);
        Assert.Equal(incoming, statistics.Incoming!.Id);
        Assert.DoesNotContain(statistics.Records, row => row.LicenseNumber is "I-4420" or "P-5773" or "TS-25410");
        Assert.Contains(statistics.Records, row => row.LicenseNumber == "SP33272"
            && row.Classification == (changed ? "Other updates" : "Unchanged"));
        foreach (var snapshot in new[] { previous, incoming })
        {
            var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", snapshot);
            var result = await db.RegulatoryLicensees.FromSqlInterpolated($"""
                SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
                WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
                """).ToListAsync();
            Assert.Equal(snapshot, Assert.Single(result).SnapshotId);
        }
        await db.RegulatoryDataSnapshots.Where(row => row.Id == incoming).ExecuteDeleteAsync();
        var deleted = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", incoming);
        Assert.Empty(await db.Database.SqlQueryRaw<string>(
            "SELECT Name AS Value FROM RegulatoryLicenseeNameSearch WHERE RegulatoryLicenseeNameSearch MATCH {0}", deleted!).ToListAsync());
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("rows")]
    [InlineData("url")]
    [InlineData("columns")]
    [InlineData("published")]
    [InlineData("missing")]
    public async Task Archive_RejectsCorruptedOrIncompleteAcquisitionEvidence(string change)
    {
        using var archive = Archive.Create();
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(archive.Path("manifest.json")))!;
        entries[0] = change switch
        {
            "hash" => entries[0] with { Sha256 = new string('0', 64) },
            "rows" => entries[0] with { Rows = 8, Columns = new() { ["23"] = 8 } },
            "url" => entries[0] with { SourceUrl = "https://example.invalid/" },
            "columns" => entries[0] with { Columns = new() { ["22"] = 9 } },
            "published" => entries[0] with { SourceRowCount = 8 },
            _ => entries[0]
        };
        if (change == "missing") entries.Clear();
        File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
        await Assert.ThrowsAsync<InvalidDataException>(() => IdahoDoplBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private static ApplicationDbContext Database(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
    private sealed class LastRunSource(Guid previous, Guid incoming) : IRegulatoryLastRunSource
    {
        public Task<RegulatoryLastRun?> GetAsync(string state, CancellationToken cancellationToken = default) =>
            Task.FromResult<RegulatoryLastRun?>(state == "Idaho" ? new(previous, incoming) : null);
    }

    private sealed class Archive : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(Directory.GetCurrentDirectory(), ".idaho-tests-" + Guid.NewGuid().ToString("N"));
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public static Archive Create(string? change = null, bool ods = false)
        {
            var archive = new Archive();
            Directory.CreateDirectory(archive.Root);
            var company = Row("CO30048", IdahoDoplBulkImportService.CompanyType);
            var alias = (string[])company.Clone(); alias[6] = "Other public alias"; alias[7] = "Other DBA";
            var rows = new List<string[]>
            {
                Row("DB37481", "Real Estate Broker License"), Row("SP33272", "Real Estate Salesperson License"),
                Row("7271394", "Real Estate Cooperative Broker License"), company, alias,
                Row("BO48115", IdahoDoplBulkImportService.BranchType),
                Row("I-4420", "Real Estate Instructor Certification License"),
                Row("P-5773", "Real Estate Provider Certification License"), Row("TS-25410", "Timeshare Registration")
            };
            rows[1][2] = "Jones"; rows[1][3] = ""; rows[1][4] = "John"; rows[1][5] = "";
            rows[2][2] = "Doe"; rows[2][3] = ""; rows[2][4] = "Alice"; rows[2][5] = "";
            if (change == "columns") rows[0] = rows[0][..22];
            if (change == "status") rows[0][8] = "Inactive";
            if (change == "type") rows[0][0] = "Real Estate Appraiser License";
            if (change == "discipline") rows[0][11] = "Unknown";
            if (change == "date") rows[0][10] = "not a date";
            if (change == "number") rows[0][1] = "DB 37481";
            if (change == "person-name") { rows[0][2] = rows[0][3] = rows[0][4] = rows[0][5] = ""; }
            if (change == "company-name") { company[2] = company[3] = company[4] = company[5] = company[6] = ""; }
            if (change == "sole-proprietor") company[6] = "";
            if (change == "person-conflict") { var other = (string[])rows[0].Clone(); other[2] = "Other person"; rows.Add(other); }
            if (change == "alias-conflict") alias[10] = "9-Mar-2027";
            if (change == "cross-type") company[1] = alias[1] = "DB37481";
            if (change == "changed") rows[1][10] = "8-Mar-2028";
            var headers = (string[])IdahoDoplBulkImportService.Headers.Clone();
            if (change == "header") headers[0] = "Profession";
            var all = new[] { headers }.Concat(rows);
            var path = archive.Path(IdahoDoplBulkImportService.FileName);
            if (ods) WriteOds(path, all, change);
            else File.WriteAllText(path, string.Join("\r\n", all.Select(row =>
                string.Join(",", row.Select(value => "\"" + value.Replace("\"", "\"\"") + "\"")))) + "\r\n"
                + (change == "different-hash" ? "\r\n" : ""), new UTF8Encoding(false));
            var bytes = File.ReadAllBytes(path);
            File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(new[]
            {
                new FloridaBulkManifestEntry(IdahoDoplBulkImportService.FileName, rows.Count, bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)), IdahoDoplBulkImportService.SourcePage,
                    DateTimeOffset.UtcNow, "Verified public export", new() { ["23"] = rows.Count }) { SourceRowCount = rows.Count }
            }));
            return archive;
        }

        private static string[] Row(string number, string type)
        {
            var row = Enumerable.Repeat("", 23).ToArray();
            row[0] = type; row[1] = number; row[2] = "Smith"; row[3] = "Marie"; row[4] = "Jane"; row[5] = "JR";
            row[6] = "Published, \"Realty\""; row[7] = "Public DBA"; row[8] = "Active";
            row[9] = "8-Mar-2019"; row[10] = "8-Mar-2027"; row[11] = "Yes";
            row[12] = "68 MILL ST"; row[14] = "SUITE"; row[15] = "2"; row[16] = "MIDDLETON";
            row[18] = "MASSACHUSETTS"; row[19] = "USA"; row[20] = "01949";
            return row;
        }

        private static void WriteOds(string path, IEnumerable<string[]> rows, string? change)
        {
            XNamespace table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
            XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
            XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
            var sheet = new XElement(table + "table", rows.Select((row, index) =>
                new XElement(table + "table-row", row.Select((value, column) =>
                    new XElement(table + "table-cell", new XAttribute(office + "value-type", index > 0 && column is 9 or 10 ? "date" : "string"),
                        index > 0 && column is 9 or 10
                            ? new XAttribute(office + "date-value", column == 9 ? "2019-03-08T00:00:00Z" : "2027-03-08T00:00:00Z") : null,
                        new XElement(text + "p", value))))));
            if (change == "repeated") sheet.Elements().First().SetAttributeValue(table + "number-rows-repeated", 2);
            var first = sheet.Descendants(table + "table-cell").First();
            if (change == "formula") first.SetAttributeValue(table + "formula", "of:=1+1");
            if (change == "number-cell") first.SetAttributeValue(office + "value-type", "float");
            if (change == "stored-date") sheet.Elements().Skip(1).First().Elements().ElementAt(9).SetAttributeValue(office + "date-value", "2000-01-01");
            var document = new XDocument(new XElement(office + "document-content",
                new XElement(office + "body", new XElement(office + "spreadsheet", sheet, change == "sheets" ? new XElement(sheet) : null))));
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var output = new StreamWriter(zip.CreateEntry("content.xml").Open(), new UTF8Encoding(false));
            if (change == "doctype") output.Write("<!DOCTYPE x [<!ENTITY exploit SYSTEM 'file:///not-permitted'>]>");
            output.Write(document.ToString());
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
