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

public sealed class MaineAlmsBulkImportServiceTests
{
    [Fact]
    public void Reader_PreservesAllAliasRowsPublishedDatesAndOutOfStateAddresses()
    {
        using var archive = Archive.Create();
        var rows = MaineAlmsBulkImportService.ReadRecords(archive.Path(MaineAlmsBulkImportService.CompanyFile),
            MaineAlmsBulkImportService.CompanyFile);
        Assert.Equal(4, rows.Count);
        Assert.Equal("Published, \"Realty\"", rows[0].Name);
        Assert.Equal("Other published alias", rows[1].Name);
        Assert.Equal(new DateOnly(2027, 3, 8), rows[0].ExpirationDate);
        Assert.Equal("68 MILL ST, MIDDLETON, MA, 01949", rows[0].Address);
        Assert.Equal("DB123", rows[0].ResponsibleLicenseNumber);
        Assert.Equal(40, rows[0].Fields.Length);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("columns")]
    [InlineData("board")]
    [InlineData("status")]
    [InlineData("type")]
    [InlineData("date")]
    public void Reader_RejectsChangedSchemaAndUnsupportedFacts(string change)
    {
        using var archive = Archive.Create(change);
        Assert.Throws<InvalidDataException>(() => MaineAlmsBulkImportService.CheckFile(
            archive.Path(MaineAlmsBulkImportService.IndividualFile), MaineAlmsBulkImportService.IndividualFile));
    }

    [Fact]
    public async Task Import_RetainsRawRowsDeduplicatesAliasesAndLinksOnlyPublishedResponsibleCredentials()
    {
        using var archive = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new MaineAlmsBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(6, await db.RegulatorySourceRecords.CountAsync());
        Assert.Equal(2, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(2, await db.RegulatoryFirms.CountAsync());
        var branch = await db.RegulatoryFirmLocations.SingleAsync();
        Assert.Null(branch.FirmId);
        Assert.Equal(RegulatoryLocationMatchMethods.NoMatch, branch.MatchMethod);
        var link = await db.RegulatoryFirmLicenseeAffiliations.Include(row => row.Firm).Include(row => row.Licensee).SingleAsync();
        Assert.Equal("AL123", link.Firm!.LicenseNumber);
        Assert.Equal("DB123", link.Licensee!.LicenseNumber);
        Assert.False(link.IsBrokerInCharge);
        Assert.Null(link.RelationshipEffectiveDate);
        var person = await db.RegulatoryLicensees.SingleAsync(row => row.LicenseNumber == "DB123");
        Assert.Equal("Designated Broker", person.LicenseType);
        Assert.Equal("DESIGNATED BROKER", person.SourceLicenseType);
        Assert.Null(person.LicenseIssuedDate);
        Assert.False(person.IsBicEligible);
        var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smith", id);
        Assert.Single(await db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
            """).ToListAsync());
        var evidence = await db.RegulatorySourceRecords.Where(row => row.LicenseNumber == "AL123").ToListAsync();
        Assert.Equal(2, evidence.Count);
        Assert.Contains(evidence, row => row.SourceRecordJson.Contains("Other published alias", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("company-conflict")]
    [InlineData("person-conflict")]
    [InlineData("cross-file")]
    public async Task Import_RejectsConflictingIdentitiesWithoutPublishingAnything(string change)
    {
        using var archive = Archive.Create(change);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new MaineAlmsBulkImportService(db).ImportArchiveAsync(archive.Root));
        Assert.Empty(await db.RegulatoryDataSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Import_RestoresValidatedSnapshotsAndRejectsIncompleteExistingImports()
    {
        using var first = Archive.Create();
        using var second = Archive.Create();
        File.AppendAllText(second.Path(MaineAlmsBulkImportService.CompanyFile), "\r\n");
        var manifest = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(second.Path("manifest.json")))!;
        var bytes = File.ReadAllBytes(second.Path(MaineAlmsBulkImportService.CompanyFile));
        manifest[1] = manifest[1] with { Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        File.WriteAllText(second.Path("manifest.json"), JsonSerializer.Serialize(manifest));
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new MaineAlmsBulkImportService(db);
        var firstId = await importer.ImportArchiveAsync(first.Root);
        var secondId = await importer.ImportArchiveAsync(second.Root);
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(firstId, await importer.ImportArchiveAsync(first.Root));
        Assert.Equal(firstId, await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == "Maine"))
            .Select(row => row.Id).FirstAsync());
        await db.RegulatorySourceRecords.Where(row => row.SnapshotId == firstId).Take(1).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportArchiveAsync(first.Root));
    }

    [Fact]
    public async Task Import_RollsBackAllStagedRowsWhenCancelled()
    {
        using var archive = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MaineAlmsBulkImportService(db)
            .ImportArchiveAsync(archive.Root, cancellation.Token, staged: _ => cancellation.Cancel()));
        Assert.Empty(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Empty(await db.RegulatorySourceRecords.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastRun_ReportsSameSemanticRowsUnchangedAndKeepsSnapshotScopedSearchSynchronized(bool changedHash)
    {
        using var first = Archive.Create();
        using var second = Archive.Create();
        if (changedHash)
        {
            File.AppendAllText(second.Path(MaineAlmsBulkImportService.CompanyFile), "\r\n");
            var manifest = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(second.Path("manifest.json")))!;
            var bytes = File.ReadAllBytes(second.Path(MaineAlmsBulkImportService.CompanyFile));
            manifest[1] = manifest[1] with { Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
            File.WriteAllText(second.Path("manifest.json"), JsonSerializer.Serialize(manifest));
        }
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new MaineAlmsBulkImportService(db);
        var previous = await importer.ImportArchiveAsync(first.Root);
        var incoming = await importer.ImportArchiveAsync(second.Root);
        Assert.Equal(changedHash, previous != incoming);
        Assert.Equal(changedHash ? 2 : 1, await db.RegulatoryDataSnapshots.CountAsync());
        var statistics = await new RegulatoryStatisticsService(db, lastRuns: new LastRunSource(previous, incoming))
            .GetAsync("Maine", bulkOnly: true);
        Assert.Equal(previous, statistics.Baseline!.Id);
        Assert.Equal(incoming, statistics.Incoming!.Id);
        Assert.NotEmpty(statistics.Professions);
        Assert.Equal(4, statistics.Professions.Sum(row => row.Collected));
        Assert.Equal(4, statistics.Records.Count);
        Assert.All(statistics.Records, row => Assert.Equal("Unchanged", row.Classification));
        Assert.All(statistics.Professions, row =>
        {
            Assert.Equal(row.Current, row.Collected);
            Assert.Equal(0, row.New);
            Assert.Equal(0, row.Changed);
            Assert.Equal(row.Collected, row.Unchanged);
        });
        Assert.All(statistics.Statuses, row =>
        {
            Assert.Equal(row.Current, row.Collected);
            Assert.Equal(0, row.NewLicenses);
            Assert.Equal(0, row.Entered);
            Assert.Equal(0, row.Left);
        });
        foreach (var snapshot in new[] { previous, incoming }.Distinct())
        {
            var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", snapshot);
            var result = await db.RegulatoryLicensees.FromSqlInterpolated($"""
                SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
                WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
                """).ToListAsync();
            Assert.Equal(snapshot, Assert.Single(result).SnapshotId);
        }
        await db.RegulatoryDataSnapshots.Where(row => row.Id == incoming).ExecuteDeleteAsync();
        var deletedMatch = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", incoming);
        Assert.Empty(await db.Database.SqlQueryRaw<string>(
            "SELECT Name AS Value FROM RegulatoryLicenseeNameSearch WHERE RegulatoryLicenseeNameSearch MATCH {0}", deletedMatch!)
            .ToListAsync());
        if (changedHash)
        {
            var retainedMatch = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", previous);
            Assert.Single(await db.Database.SqlQueryRaw<string>(
                "SELECT Name AS Value FROM RegulatoryLicenseeNameSearch WHERE RegulatoryLicenseeNameSearch MATCH {0}", retainedMatch!)
                .ToListAsync());
        }
    }

    private sealed class LastRunSource(Guid previous, Guid incoming) : IRegulatoryLastRunSource
    {
        public Task<RegulatoryLastRun?> GetAsync(string state, CancellationToken cancellationToken = default) =>
            Task.FromResult<RegulatoryLastRun?>(state == "Maine" ? new(previous, incoming) : null);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("rows")]
    [InlineData("url")]
    [InlineData("missing")]
    [InlineData("columns")]
    [InlineData("published")]
    public async Task Archive_RejectsChangedOrIncompleteEvidence(string change)
    {
        using var archive = Archive.Create();
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(archive.Path("manifest.json")))!;
        entries[0] = change switch
        {
            "hash" => entries[0] with { Sha256 = new string('0', 64) },
            "rows" => entries[0] with { Rows = 99, Columns = new() { ["40"] = 99 } },
            "url" => entries[0] with { SourceUrl = "https://example.invalid/" },
            "columns" => entries[0] with { Columns = new() { ["39"] = 2 } },
            "published" => entries[0] with { SourceRowCount = 99 },
            _ => entries[0]
        };
        if (change == "missing") entries.RemoveAt(1);
        File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
        await Assert.ThrowsAsync<InvalidDataException>(() => MaineAlmsBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private sealed class Archive : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(Directory.GetCurrentDirectory(), ".maine-tests-" + Guid.NewGuid().ToString("N"));
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public static Archive Create(string? change = null)
        {
            var archive = new Archive();
            Directory.CreateDirectory(archive.Root);
            var people = new List<string[]>
            {
                Row("SMITH, JANE", "DB123", "DESIGNATED BROKER"),
                Row("JONES, JOHN", "SA123", "SALES AGENT")
            };
            var company = Row("Published, \"Realty\"", "AL123", "LIMITED LIABILITY");
            company[32] = "JANE SMITH"; company[33] = "DB123";
            var alias = (string[])company.Clone();
            alias[0] = alias[1] = "Other published alias";
            var branch = Row("Published branch", "AB123", "BRANCH OFFICE");
            branch[33] = "DB123";
            var unresolved = Row("Unresolved firm", "AC123", "CORPORATION");
            unresolved[33] = "DB999";
            if (change == "company-conflict") alias[31] = "03/09/2027";
            if (change == "person-conflict") { var other = (string[])people[0].Clone(); other[0] = "DIFFERENT PERSON"; people.Add(other); }
            if (change == "cross-file") company[2] = alias[2] = "DB123";
            if (change == "columns") people[0] = people[0][..39];
            if (change == "board") people[0][37] = "OTHER BOARD";
            if (change == "status") people[0][4] = "INACTIVE";
            if (change == "type") people[0][3] = "APPRAISER";
            if (change == "date") people[0][31] = "not a date";
            Write(archive.Path(MaineAlmsBulkImportService.IndividualFile), people, change == "header");
            Write(archive.Path(MaineAlmsBulkImportService.CompanyFile), [company, alias, branch, unresolved]);
            var entries = MaineAlmsBulkImportService.Definitions.Select(file =>
            {
                var bytes = File.ReadAllBytes(archive.Path(file.Name));
                long count = file.Name == MaineAlmsBulkImportService.IndividualFile ? people.Count : 4;
                return new FloridaBulkManifestEntry(file.Name, count, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
                    file.Url, DateTimeOffset.UtcNow, "Live HTTP export", new() { ["40"] = count });
            }).ToList();
            File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
            return archive;
        }
        private static string[] Row(string name, string number, string type)
        {
            var row = Enumerable.Repeat("", 40).ToArray();
            row[0] = row[1] = name; row[2] = number; row[3] = type; row[4] = "ACTIVE";
            row[5] = "68 MILL ST"; row[9] = "MIDDLETON"; row[10] = "MA"; row[11] = "01949";
            row[31] = "03/08/2027"; row[37] = "REAL ESTATE COMMISSION";
            return row;
        }
        private static void Write(string path, IEnumerable<string[]> rows, bool changedHeader = false)
        {
            var headers = (string[])MaineAlmsBulkImportService.Headers.Clone();
            if (changedHeader) headers[0] = "Changed";
            File.WriteAllLines(path, new[] { headers }.Concat(rows).Select(row =>
                string.Join(",", row.Select(value => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""))), new UTF8Encoding(true));
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
