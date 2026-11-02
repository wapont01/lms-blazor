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

public sealed class FloridaBulkImportServiceTests
{
    [Fact]
    public async Task CancellationDuringStagingRollsBackAllPendingRecords()
    {
        using var archive = await BulkTestArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var current = new RegulatoryDataSnapshot
        {
            State = "Florida", SourceName = "Previous bulk", Status = RegulatorySnapshotStatuses.Approved,
            ParserVersion = FloridaBulkImportService.ParserVersion
        };
        db.RegulatoryDataSnapshots.Add(current);
        await db.SaveChangesAsync();
        using var stop = new CancellationTokenSource();
        long staged = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FloridaBulkImportService(db).ImportArchiveAsync(archive.Path, stop.Token,
                stagingProgress: records => { staged = records; stop.Cancel(); }));
        Assert.True(staged > 0);
        Assert.Equal(current.Id, await db.RegulatoryDataSnapshots.Select(s => s.Id).SingleAsync());
        Assert.Equal(RegulatorySnapshotStatuses.Approved, await db.RegulatoryDataSnapshots.Select(s => s.Status).SingleAsync());
        Assert.Equal(0, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(0, await db.RegulatoryFirms.CountAsync());
        Assert.Equal(0, await db.RegulatoryFirmLicenseeAffiliations.CountAsync());
    }

    [Fact]
    public void BoundaryParserPreservesInteriorQuotesAndRejectsWrongLayout()
    {
        Assert.Equal(["one", "literal \"quote\"", "three"], FloridaBulkCsv.ReadFields("\"one\",\"literal \"quote\"\",\"three\"", 3));
        Assert.Throws<InvalidDataException>(() => FloridaBulkCsv.ReadFields("\"one\",\"two\"", 3));
        Assert.Throws<InvalidDataException>(() => FloridaBulkCsv.ReadFields("one,two", 2));
    }

    [Theory]
    [InlineData("BK", "Broker", "BK12")]
    [InlineData("BL", "Broker Sales", "BK12")]
    [InlineData("SL", "Sales Associate", "SL12")]
    public void RealEstateRankUsesAuthoritativeNumberAndExactDates(string rank, string type, string number)
    {
        var fields = BulkTestArchive.Person(rank);
        var row = FloridaBulkImportService.ParseRow(FloridaBulkImportService.Definitions[0], fields)!;
        Assert.Equal(number, row.LicenseNumber);
        Assert.Equal(type, row.Type);
        Assert.Equal(new DateOnly(1999, 11, 9), row.OriginalDate);
        Assert.Equal(new DateOnly(2008, 1, 24), row.IssuedDate);
        Assert.Equal(new DateOnly(2027, 9, 30), row.RenewalDate);
        Assert.Equal("Current, Active", row.Status);
        fields[17] = "BL12";
        if (rank == "BL")
            Assert.Equal("BK12", FloridaBulkImportService.ParseRow(FloridaBulkImportService.Definitions[0], fields)!.LicenseNumber);
        else
            Assert.Throws<InvalidDataException>(() => FloridaBulkImportService.ParseRow(FloridaBulkImportService.Definitions[0], fields));
    }

    [Theory]
    [InlineData("MC")]
    [InlineData("FMC")]
    public void AppraisalManagementCompaniesAreFirmsNotPeople(string rank)
    {
        var fields = BulkTestArchive.Appraiser();
        fields[1] = rank;
        var row = FloridaBulkImportService.ParseRow(FloridaBulkImportService.Definitions.Single(d => d.Columns == 20), fields)!;
        Assert.True(row.IsFirm);
        Assert.StartsWith(rank, row.LicenseNumber);
        Assert.Null(row.Type);
    }

    [Theory]
    [InlineData("Invol Inactive", "Inactive", "Involuntary Inactive, Inactive")]
    [InlineData("Probation", "Active", "Current with Probation, Active")]
    [InlineData("RAW", "OTHER", "RAW, OTHER")]
    public void StatusAliasesAreCanonicalAndUnknownCodesPreserved(string primary, string secondary, string expected)
    {
        var fields = BulkTestArchive.Person();
        fields[12] = primary;
        fields[13] = secondary;
        Assert.Equal(expected, FloridaBulkImportService.ParseRow(FloridaBulkImportService.Definitions[0], fields)!.Status);
    }

    [Fact]
    public void AppraisalUsesActualTwentyColumnsAndOnlyOfficialKnownStatusCodes()
    {
        var f = BulkTestArchive.Appraiser();
        var definition = FloridaBulkImportService.Definitions.Single(d => d.Columns == 20);
        var row = FloridaBulkImportService.ParseRow(definition, f)!;
        Assert.Equal("RD360", row.LicenseNumber);
        Assert.Equal("Certified Residential Appraiser", row.Type);
        Assert.Equal("Current, Active", row.Status);
        Assert.Equal(new DateOnly(1994, 1, 25), row.OriginalDate);
        Assert.Equal(new DateOnly(2000, 9, 13), row.IssuedDate);
        Assert.Equal(new DateOnly(2028, 11, 30), row.RenewalDate);
        f[13] = "UNKNOWN";
        f[14] = "X";
        Assert.Equal("UNKNOWN, X", FloridaBulkImportService.ParseRow(definition, f)!.Status);
        f[17] = "02/30/2028";
        Assert.Throws<InvalidDataException>(() => FloridaBulkImportService.ParseRow(definition, f));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("PVDR")]
    public void AppraisalCoursesAndProvidersAreArchivedNotFabricatedAsPeople(string rank)
    {
        var fields = BulkTestArchive.Appraiser();
        fields[1] = rank;
        Assert.Null(FloridaBulkImportService.ParseRow(FloridaBulkImportService.Definitions.Single(d => d.Columns == 20), fields));
    }

    [Fact]
    public void BlankCombinedLicenseNumberIsDerivedOnlyFromValidatedRankAndNumericField()
    {
        var fields = BulkTestArchive.Person("BL");
        fields[17] = "";
        Assert.Equal("BK12", FloridaBulkImportService.ParseRow(FloridaBulkImportService.Definitions[0], fields)!.LicenseNumber);
        fields[11] = "not-a-number";
        Assert.Throws<InvalidDataException>(() => FloridaBulkImportService.ParseRow(FloridaBulkImportService.Definitions[0], fields));
    }

    [Fact]
    public async Task ImportActivatesValidatedRosterIdempotentlyWithoutTrackedGraph()
    {
        using var archive = await BulkTestArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new FloridaBulkImportService(db);
        var id = await service.ImportArchiveAsync(archive.Path);
        Assert.Empty(db.ChangeTracker.Entries());
        var snapshot = await db.RegulatoryDataSnapshots.AsNoTracking().SingleAsync();
        Assert.Equal(RegulatorySnapshotStatuses.Approved, snapshot.Status);
        Assert.NotNull(snapshot.ReviewedAtUtc);
        Assert.Null(snapshot.ReviewedByUserId);
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(3, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(1, await db.RegulatoryFirms.CountAsync());
        Assert.Equal(1, await db.RegulatoryFirmLicenseeAffiliations.CountAsync());
        var linked = await db.RegulatoryLicensees.AsNoTracking().SingleAsync(l => l.LicenseNumber == "BK12");
        Assert.Equal(new DateOnly(1999, 11, 9), linked.OriginalLicenseDate);
        Assert.Null(linked.UpdateHours);
        Assert.Null(linked.RelatedLicenseNumber);
        Assert.Null(linked.IsAdditionalLicenseRecord);
        Assert.Equal(id, await service.ImportArchiveAsync(archive.Path));
        Assert.Equal(1, await db.RegulatoryDataSnapshots.CountAsync());
        var activatedAt = snapshot.ReviewedAtUtc;
        await service.ImportArchiveAsync(archive.Path);
        Assert.Equal(RegulatorySnapshotStatuses.Approved, await db.RegulatoryDataSnapshots.Select(s => s.Status).SingleAsync());
        Assert.Equal(activatedAt, await db.RegulatoryDataSnapshots.Select(s => s.ReviewedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task ExistingValidatedPendingArchiveIsActivatedWithoutDuplicatingRecords()
    {
        using var archive = await BulkTestArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new FloridaBulkImportService(db);
        var id = await service.ImportArchiveAsync(archive.Path);
        await db.RegulatoryDataSnapshots.Where(s => s.Id == id).ExecuteUpdateAsync(s =>
            s.SetProperty(p => p.Status, RegulatorySnapshotStatuses.PendingReview).SetProperty(p => p.ReviewedAtUtc, (DateTime?)null));
        db.ChangeTracker.Clear();
        Assert.Equal(id, await service.ImportArchiveAsync(archive.Path));
        Assert.Equal(RegulatorySnapshotStatuses.Approved, await db.RegulatoryDataSnapshots.Select(s => s.Status).SingleAsync());
        Assert.Equal(3, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(1, await db.RegulatoryFirms.CountAsync());
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("utf8")]
    [InlineData("date")]
    [InlineData("traversal")]
    public async Task InvalidArchivesFailBeforeStaging(string defect)
    {
        using var archive = await BulkTestArchive.CreateAsync(defect);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => new FloridaBulkImportService(db).ImportArchiveAsync(archive.Path));
        Assert.False(await db.RegulatoryDataSnapshots.AnyAsync());
        Assert.False(await db.RegulatoryLicensees.AnyAsync());
    }

    [Fact]
    public async Task FailureDuringBulkWriteRollsBackSnapshotAndAllRows()
    {
        using var archive = await BulkTestArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var current = new RegulatoryDataSnapshot
        {
            State = "Florida", SourceName = "Previous synchronized roster",
            Status = RegulatorySnapshotStatuses.Approved, ParserVersion = FloridaBulkImportService.ParserVersion
        };
        db.RegulatoryDataSnapshots.Add(current);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_bulk BEFORE INSERT ON RegulatoryLicensees BEGIN SELECT RAISE(ABORT, 'synthetic bulk write failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => new FloridaBulkImportService(db).ImportArchiveAsync(archive.Path));
        var retained = await db.RegulatoryDataSnapshots.AsNoTracking().SingleAsync();
        Assert.Equal(current.Id, retained.Id);
        Assert.Equal(RegulatorySnapshotStatuses.Approved, retained.Status);
        Assert.False(await db.RegulatoryFirms.AnyAsync());
    }

    [Fact]
    public async Task RejectedArchiveIsNotSilentlyReactivated()
    {
        using var archive = await BulkTestArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new FloridaBulkImportService(db);
        var id = await service.ImportArchiveAsync(archive.Path);
        await db.RegulatoryDataSnapshots.Where(s => s.Id == id).ExecuteUpdateAsync(s =>
            s.SetProperty(p => p.Status, RegulatorySnapshotStatuses.Rejected));
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportArchiveAsync(archive.Path));
        Assert.Equal(RegulatorySnapshotStatuses.Rejected, await db.RegulatoryDataSnapshots.Select(s => s.Status).SingleAsync());
    }

    [Fact]
    public async Task PreCancelledValidationDoesNotReadOrWrite()
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        using var archive = await BulkTestArchive.CreateAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FloridaBulkImportService.ValidateArchiveAsync(archive.Path, token.Token));
    }

}

public sealed class BulkTestArchive : IDisposable
{
    public string Path { get; }
    private BulkTestArchive(string path) => Path = path;

    public static string[] Person(string rank = "BK") =>
        ["2501 Real Estate Broker or Sales", "Person \"quoted\" Example", "", rank + " Broker", "1 MAIN ST", "", "", "MIAMI", "FL", "33101", "Dade", "12",
            "Current", "Active", "09-NOV-99", "24-JAN-08", "30-SEP-27", (rank == "BL" ? "BK" : rank) + "12", "", "Firm Example", "34"];
    public static string[] Appraiser() =>
        ["64", "RD", "Appraiser Example", "", "", "PO BOX 911", "", "", "WINTER PARK", "FL", "32790", "58", "0000360", "C", "A", "01/25/1994", "09/13/2000", "11/30/2028", "", "1"];

    public static async Task<BulkTestArchive> CreateAsync(string? defect = null, string? parent = null)
    {
        var root = System.IO.Path.Combine(parent ?? Directory.GetCurrentDirectory(), ".florida-bulk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archive = new BulkTestArchive(root);
        var entries = new List<FloridaBulkManifestEntry>();
        foreach (var definition in FloridaBulkImportService.Definitions)
        {
            var f = definition.Columns == 20 ? Appraiser() : definition.Columns == 12
                ? new[] { "BK", "Broker", "BK12", "Person", "1 MAIN ST", "", "MIAMI FL 33101", "09/30/2027", "123", "CE", "14", "06/30/2025" }
                : Person();
            if (definition.Name == "RealEstateCorpLicense.csv")
            {
                f[0] = "2502 Real Estate Corporation"; f[1] = "Firm Example"; f[3] = "CQ RE Corp.";
                f[11] = "34"; f[17] = "CQ34"; f[19] = ""; f[20] = "";
            }
            if (definition.Name == "RealEstateSchoolLicense.csv")
            {
                f[0] = "2505 Real Estate Instructor"; f[3] = "ZH RE Instructor"; f[17] = "ZH12"; f[19] = ""; f[20] = "";
            }
            if (defect == "date" && definition.Name == "REALESTATE2501LICENSE_1.csv") f[16] = "31-FEB-27";
            var line = "\"" + string.Join("\",\"", f) + "\"\n";
            long rows = 1;
            if (defect == "duplicate" && definition.Name == "REALESTATE2501LICENSE_1.csv")
            {
                f[1] = "Conflicting name";
                line += "\"" + string.Join("\",\"", f) + "\"\n";
                rows++;
            }
            if (defect == "empty" && definition.Name == "REALESTATE2501LICENSE_1.csv") line = "";
            var path = System.IO.Path.Combine(root, definition.RelativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes(line);
            if (defect == "utf8" && definition.Name == "REALESTATE2501LICENSE_1.csv") bytes[5] = 0xff;
            await File.WriteAllBytesAsync(path, bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (defect == "hash" && definition.Name == "REALESTATE2501LICENSE_1.csv") hash = new string('0', 64);
            entries.Add(new(defect == "traversal" ? @"..\outside.csv" : definition.RelativePath, rows, bytes.Length, hash, definition.Url,
                new DateTimeOffset(2026, 10, 31, 15, 26, 50, TimeSpan.Zero), "synthetic test", new() { [definition.Columns.ToString()] = rows }));
        }
        await File.WriteAllTextAsync(System.IO.Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(entries));
        return archive;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
        var parent = System.IO.Path.GetDirectoryName(Path)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }
}
