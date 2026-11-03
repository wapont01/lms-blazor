using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class MontanaRreBulkImportServiceTests
{
    [Fact]
    public void ReadRecords_PreservesNamesStatusesDatesAndAllPublishedFields()
    {
        var fallback = Row("RRE-RPM-TMP-1", "Property Manager Temporary License", first: "", last: "", business: "Published Property Management");
        var unnamed = Row("RRE-RBS-TMP-2", "Salesperson Temporary License", first: "", last: "", business: "");
        var doubleSpace = Row("RRE-RPM-LIC-3", "Property Manager  License");
        using var archive = Archive.Create([Row(), fallback, unnamed, doubleSpace]);
        var rows = MontanaRreBulkImportService.ReadRecords(archive.Path(MontanaRreBulkImportService.FileName));
        Assert.Equal(4, rows.Count);
        Assert.Equal("Jane Smith", rows[0].Name);
        Assert.Equal("Active", rows[0].Status);
        Assert.Equal(new DateOnly(2027, 10, 31), rows[0].ExpirationDate);
        Assert.Equal("Published Property Management", rows[1].Name);
        Assert.Equal("", rows[2].Name);
        Assert.Equal("Property Manager License", rows[3].LicenseType);
        Assert.Equal("Property Manager  License", rows[3].SourceLicenseType);
        Assert.Equal("Helena", rows[0].Fields[7]);
        Assert.Equal("MT", rows[0].Fields[8]);
        Assert.Equal("59601", rows[0].Fields[9]);
        Assert.Equal("Published Realty, LLC", rows[0].Fields[4]);
    }

    [Theory]
    [InlineData(0, "RRE-BRO-LIC-not-a-number")]
    [InlineData(0, "RRE-RPM-LIC-1")]
    [InlineData(1, "Other Board License")]
    [InlineData(2, "")]
    [InlineData(3, "not a date")]
    [InlineData(3, "02/30/2026")]
    public void ReadRecords_RejectsInvalidIdentitiesTypesStatusesAndDates(int column, string value)
    {
        var row = Row();
        row[column] = value;
        using var archive = Archive.Create([row]);
        Assert.Throws<InvalidDataException>(() => MontanaRreBulkImportService.CheckFile(archive.Path(MontanaRreBulkImportService.FileName)));
    }

    [Fact]
    public void ReadRecords_AllowsMissingExpirationAndRejectsChangedSchemaOrConflictingDuplicates()
    {
        var row = Row();
        row[3] = "";
        using var archive = Archive.Create([row]);
        Assert.Null(Assert.Single(MontanaRreBulkImportService.ReadRecords(archive.Path(MontanaRreBulkImportService.FileName))).ExpirationDate);
        var file = archive.Path(MontanaRreBulkImportService.FileName);
        File.WriteAllText(file, File.ReadAllText(file).Replace("License Number", "Changed Header", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => MontanaRreBulkImportService.CheckFile(file));
        using var badColumns = Archive.Create([Row().SkipLast(1).ToArray()]);
        Assert.Throws<InvalidDataException>(() => MontanaRreBulkImportService.CheckFile(badColumns.Path(MontanaRreBulkImportService.FileName)));
        var conflicting = Row();
        conflicting[2] = "Terminated";
        using var duplicates = Archive.Create([Row(), conflicting]);
        Assert.Throws<InvalidDataException>(() => MontanaRreBulkImportService.CheckFile(duplicates.Path(MontanaRreBulkImportService.FileName)));
    }

    [Fact]
    public async Task ImportArchive_RetainsEvidenceWithoutInventingFirmsOrLicenseesAndIndexesNames()
    {
        var rows = new[]
        {
            Row(),
            Row("RRE-RPM-LIC-2", "Property Manager  License"),
            Row("RRE-RPM-TMP-3", "Property Manager Temporary License", first: "", last: "", business: ""),
            Row("RRE-PLI-LIC-4", "Pre-Licensing Instructor License"),
            Row("RRE-TSS-LIC-5", "Timeshare Salesperson License"),
            Row("RRE-RBS-PRV-6", "Salesperson Temporary License"),
            Row("RRE-PLC-LIC-7", "Pre-Licensing Course License"),
            Row("RRE-TSD-LIC-8", "Timeshare Offering Registration (license)"),
            Row()
        };
        using var archive = Archive.Create(rows);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new MontanaRreBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(9, await db.RegulatorySourceRecords.CountAsync());
        Assert.Equal(6, await db.RegulatoryLicensees.CountAsync());
        Assert.False(await db.RegulatoryFirms.AnyAsync());
        Assert.False(await db.RegulatoryFirmLocations.AnyAsync());
        Assert.False(await db.RegulatoryFirmLicenseeAffiliations.AnyAsync());
        Assert.Equal("", (await db.RegulatoryLicensees.SingleAsync(row => row.LicenseNumber == "RRE-RPM-TMP-3")).Name);
        var propertyManager = await db.RegulatoryLicensees.SingleAsync(row => row.LicenseNumber == "RRE-RPM-LIC-2");
        Assert.Equal("Property Manager License", propertyManager.LicenseType);
        Assert.Equal("Property Manager  License", propertyManager.SourceLicenseType);
        var evidence = await db.RegulatorySourceRecords.SingleAsync(row => row.LicenseNumber == "RRE-PLC-LIC-7");
        using var json = JsonDocument.Parse(evidence.SourceRecordJson);
        Assert.Equal("Helena", json.RootElement.GetProperty("fields").GetProperty("City").GetString());
        Assert.Equal("MT", json.RootElement.GetProperty("fields").GetProperty("State").GetString());
        Assert.Equal("59601", json.RootElement.GetProperty("fields").GetProperty("Zip").GetString());
        var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", id);
        Assert.Equal(5, (await db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
            """).ToListAsync()).Count);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("rows")]
    [InlineData("columns")]
    [InlineData("url")]
    [InlineData("published")]
    [InlineData("date")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public async Task ValidateArchive_RejectsChangedOrIncompleteManifest(string change)
    {
        using var archive = Archive.Create([Row()]);
        var entries = archive.Manifest();
        entries[0] = change switch
        {
            "hash" => entries[0] with { Sha256 = new string('0', 64) },
            "rows" => entries[0] with { Rows = 2, Columns = new() { ["10"] = 2 } },
            "columns" => entries[0] with { Columns = new() { ["11"] = 1 } },
            "url" => entries[0] with { SourceUrl = "https://example.invalid/export" },
            "published" => entries[0] with { SourceRowCount = 99 },
            "date" => entries[0] with { VerifiedAtUtc = default },
            _ => entries[0]
        };
        if (change == "missing") entries.Clear();
        if (change == "duplicate") entries.Add(entries[0]);
        archive.WriteManifest(entries);
        await Assert.ThrowsAsync<InvalidDataException>(() => MontanaRreBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    [Fact]
    public async Task Statistics_CompareOldBaselineWithNewChangedAndUnchangedCurrentAndRestore()
    {
        var broker = Row();
        var salesperson = Row("RRE-RBS-LIC-2", "Salesperson License");
        var unchanged = Row("RRE-RBS-LIC-3", "Salesperson License");
        using var baseline = Archive.Create([broker, salesperson, unchanged], new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var changed = salesperson.ToArray();
        changed[2] = "Terminated";
        using var current = Archive.Create([broker, changed, unchanged, Row("RRE-RBS-LIC-4", "Salesperson License")]);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var importer = new MontanaRreBulkImportService(db);
        var baselineId = await importer.ImportArchiveAsync(baseline.Root);
        var currentId = await importer.ImportArchiveAsync(current.Root);
        Assert.Equal(currentId, await importer.ImportArchiveAsync(current.Root));
        var stats = await new RegulatoryStatisticsService(db).GetAsync("Montana", bulkOnly: true);
        Assert.Equal(baselineId, stats.Baseline!.Id);
        Assert.Equal(currentId, stats.Incoming!.Id);
        Assert.Equal(1, stats.Professions.Sum(row => row.New));
        Assert.Equal(1, stats.Professions.Sum(row => row.Changed));
        Assert.Equal(2, stats.Professions.Sum(row => row.Unchanged));
        Assert.Equal(baselineId, await importer.ImportArchiveAsync(baseline.Root));
        Assert.Equal(baselineId, await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == "Montana"))
            .Select(row => row.Id).FirstAsync());
    }

    [Fact]
    public async Task Statistics_DifferentFileHashesWithSameDataRemainUnchanged()
    {
        using var first = Archive.Create([Row()]);
        using var second = Archive.Create([Row()]);
        File.AppendAllText(second.Path(MontanaRreBulkImportService.FileName), "\r\n");
        second.RefreshManifest();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var importer = new MontanaRreBulkImportService(db);
        var firstId = await importer.ImportArchiveAsync(first.Root);
        var secondId = await importer.ImportArchiveAsync(second.Root);
        Assert.NotEqual(firstId, secondId);
        var stats = await new RegulatoryStatisticsService(db).GetAsync("Montana", bulkOnly: true);
        var profession = Assert.Single(stats.Professions);
        Assert.Equal(0, profession.New);
        Assert.Equal(0, profession.Changed);
        Assert.Equal(1, profession.Unchanged);
    }

    [Fact]
    public async Task ImportArchive_RollsBackInterruptedStagingAndRejectsIncompleteExistingSnapshots()
    {
        using var archive = Archive.Create([Row()]);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var importer = new MontanaRreBulkImportService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportArchiveAsync(archive.Root,
            staged: _ => throw new InvalidOperationException("Interrupted staging")));
        Assert.False(await db.RegulatoryDataSnapshots.AnyAsync());
        Assert.False(await db.RegulatorySourceRecords.AnyAsync());
        Assert.False(await db.RegulatoryLicensees.AnyAsync());
        await importer.ImportArchiveAsync(archive.Root);
        await db.RegulatorySourceRecords.ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
    }

    private static ApplicationDbContext CreateDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static string[] Row(string number = "RRE-BRO-LIC-1", string type = "Broker License",
        string first = "Jane", string last = "Smith", string business = "Published Realty, LLC") =>
        [number, type, "Active", "10/31/2027", business, last, first, "Helena", "MT", "59601"];

    private sealed class Archive : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(Environment.CurrentDirectory, ".montana-test-" + Guid.NewGuid().ToString("N"));
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public static Archive Create(IReadOnlyCollection<string[]> rows, DateTimeOffset? acquired = null)
        {
            var archive = new Archive();
            Directory.CreateDirectory(archive.Root);
            static string Csv(string[] fields) => string.Join(',', fields.Select(value => "\"" + value.Replace("\"", "\"\"") + "\""));
            File.WriteAllLines(archive.Path(MontanaRreBulkImportService.FileName),
                new[] { MontanaRreBulkImportService.Headers }.Concat(rows).Select(Csv), new UTF8Encoding(false));
            archive.WriteManifest([new(MontanaRreBulkImportService.FileName, rows.Count, 0, "", MontanaRreBulkImportService.SourcePage,
                acquired ?? new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero), "Manual CAPTCHA-authorized export",
                new() { ["10"] = rows.Count })]);
            archive.RefreshManifest();
            return archive;
        }
        public List<FloridaBulkManifestEntry> Manifest() =>
            JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(Path("manifest.json")))!;
        public void WriteManifest(List<FloridaBulkManifestEntry> entries) =>
            File.WriteAllText(Path("manifest.json"), JsonSerializer.Serialize(entries));
        public void RefreshManifest()
        {
            var entries = Manifest();
            var bytes = File.ReadAllBytes(Path(MontanaRreBulkImportService.FileName));
            entries[0] = entries[0] with { Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
            WriteManifest(entries);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
