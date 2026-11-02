using System.IO.Compression;
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

public sealed class CaliforniaBulkImportServiceTests
{
    [Fact]
    public async Task RolesRelationshipsRestrictionsUnknownAssociateStatusAndRepeatActivationArePreserved()
    {
        using var archive = await CaliforniaArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new CaliforniaBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Path);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Path));
        Assert.Equal(1, await db.RegulatoryDataSnapshots.CountAsync());
        Assert.Equal(7, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(2, await db.RegulatoryFirms.CountAsync());
        var officers = await db.RegulatoryLicensees.Where(r => r.LicenseNumber == "00001234" && r.LicenseType == "Officer").ToListAsync();
        Assert.Equal(2, officers.Count);
        Assert.Contains(officers, r => r.RelatedLicenseNumber == "00009012");
        var salesperson = await db.RegulatoryLicensees.SingleAsync(r => r.LicenseType == "Salesperson");
        Assert.Equal("María Doe", salesperson.Name);
        Assert.Equal("Licensed NBA", salesperson.LicenseStatus);
        Assert.Equal(new DateOnly(2020, 1, 1), salesperson.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2027, 12, 31), salesperson.LicenseRenewalDate);
        Assert.Equal(new DateOnly(2019, 1, 1), salesperson.OriginalLicenseDate);
        Assert.False(salesperson.IsAdditionalLicenseRecord);
        Assert.True(salesperson.IsRestricted);
        Assert.True(salesperson.EthicsAndAgencyOnly);
        var associate = await db.RegulatoryLicensees.SingleAsync(r => r.LicenseNumber == "00007890");
        Assert.Equal("Status not published", associate.LicenseStatus);
        Assert.Null(associate.IsAdditionalLicenseRecord);
        Assert.Equal("Broker Associate", associate.LicenseLevel);
        Assert.DoesNotContain(await db.RegulatoryFirms.Select(r => r.LicenseNumber).ToListAsync(), n => n == "00009999");
        var stats = await new RegulatoryStatisticsService(db).GetAsync("California", bulkOnly: true);
        Assert.Equal(7, stats.Professions.Where(r => r.Kind == "Licensees").Sum(r => r.Collected));
        Assert.Equal(id, stats.Incoming!.Id);
        Assert.Equal(RegulatorySnapshotStatuses.Approved, (await db.RegulatoryDataSnapshots.SingleAsync()).Status);
        var admin = new UserAccount { Role = "Admin", IsActive = true, Email = "admin@example.invalid" };
        var alternate = new RegulatoryDataSnapshot { State = "California", SourceName = "Synthetic alternate",
            ParserVersion = CaliforniaBulkImportService.ParserVersion, Status = RegulatorySnapshotStatuses.Approved,
            RetrievedAtUtc = DateTime.UtcNow, ActivatedAtUtc = DateTime.UtcNow };
        db.AddRange(admin, alternate);
        db.RegulatoryLicensees.Add(new() { SnapshotId = alternate.Id, State = "California", Name = "Alternate", LicenseNumber = "00000001", LicenseType = "Broker" });
        await db.SaveChangesAsync();
        var directory = new RegulatoryDirectoryService(db, new AuditLogService(db));
        await directory.RestoreFloridaRosterAsync(id, alternate.Id, admin.Id, "California recovery test", "California");
        Assert.Equal(id, (await directory.GetLatestApprovedSnapshotAsync("California"))!.Id);
        Assert.Equal(2, (await directory.GetFloridaRosterHistoryAsync(state: "California")).Count);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Path));
    }

    [Fact]
    public async Task CancellationRollsBackWithoutReplacingCurrentCaliforniaRoster()
    {
        using var archive = await CaliforniaArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var previous = new RegulatoryDataSnapshot { State = "California", SourceName = "Previous", Status = RegulatorySnapshotStatuses.Approved };
        db.Add(previous);
        await db.SaveChangesAsync();
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CaliforniaBulkImportService(db)
            .ImportArchiveAsync(archive.Path, stop.Token, staged: _ => stop.Cancel()));
        Assert.Equal(previous.Id, await db.RegulatoryDataSnapshots.Select(s => s.Id).SingleAsync());
        Assert.Equal(0, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(0, await db.RegulatoryFirms.CountAsync());
    }

    [Fact]
    public async Task ConflictingExpirationDatesNeverActivate()
    {
        using var archive = await CaliforniaArchive.CreateAsync(associateExpiration: "2028-12-31");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new CaliforniaBulkImportService(db).ImportArchiveAsync(archive.Path));
        Assert.Equal(0, await db.RegulatoryDataSnapshots.CountAsync());
    }

    [Fact]
    public async Task ArchiveRejectsHashChangesMissingSourcesAndZipTraversalEntries()
    {
        using var archive = await CaliforniaArchive.CreateAsync();
        Assert.Equal(2, (await CaliforniaBulkImportService.ValidateArchiveAsync(archive.Path)).Count);
        await File.AppendAllTextAsync(System.IO.Path.Combine(archive.Path, "broker_associates_list.xls"), "tamper");
        await Assert.ThrowsAsync<InvalidDataException>(() => CaliforniaBulkImportService.ValidateArchiveAsync(archive.Path));
        await archive.RewriteAsync();
        using (var zip = ZipFile.Open(System.IO.Path.Combine(archive.Path, "CurrList.zip"), ZipArchiveMode.Update))
            zip.CreateEntry("../bad.csv");
        Assert.Throws<InvalidDataException>(() => CaliforniaBulkImportService.CheckFile(
            System.IO.Path.Combine(archive.Path, "CurrList.zip"), "CurrList.zip"));
        await archive.RewriteAsync();
        File.Delete(System.IO.Path.Combine(archive.Path, "broker_associates_list.xls"));
        await Assert.ThrowsAnyAsync<IOException>(() => CaliforniaBulkImportService.ValidateArchiveAsync(archive.Path));
    }

    [Fact]
    public async Task DatedOfficialBinarySourcesDecodeWithoutExcelAutomation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var sources = System.IO.Path.Combine(root!.FullName, "src", "docs", "CALIFORNIA", "LICENSING", "XLXS");
        using var archive = await CaliforniaArchive.CreateAsync();
        File.Copy(System.IO.Path.Combine(sources, "CurrList_20261009.zip"), System.IO.Path.Combine(archive.Path, "CurrList.zip"), true);
        File.Copy(System.IO.Path.Combine(sources, "broker_associates_list_20261009.xls"),
            System.IO.Path.Combine(archive.Path, "broker_associates_list.xls"), true);
        Assert.Equal(427890, CaliforniaBulkImportService.CheckFile(System.IO.Path.Combine(archive.Path, "CurrList.zip"), "CurrList.zip"));
        var rows = CaliforniaBulkImportService.CheckFile(System.IO.Path.Combine(archive.Path, "broker_associates_list.xls"), "broker_associates_list.xls");
        Assert.Equal(25519, rows);
        Assert.Contains("02215480", await File.ReadAllTextAsync(System.IO.Path.Combine(archive.Path, "broker-associates.csv")));
    }
}
