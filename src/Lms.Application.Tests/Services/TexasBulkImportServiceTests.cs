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

public sealed class TexasBulkImportServiceTests
{
    [Fact]
    public async Task AllStatusesSuffixesMissingNamesAndActualRelationshipsSurviveImport()
    {
        using var archive = await TexasArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new TexasBulkImportService(db);
        var id = await service.ImportArchiveAsync(archive.Path);
        Assert.Equal(id, await service.ImportArchiveAsync(archive.Path));
        Assert.Equal(1, await db.RegulatoryDataSnapshots.CountAsync());
        Assert.Equal(1, await db.RegulatoryFirms.CountAsync());
        Assert.Equal(3, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(2, await db.RegulatoryFirmLicenseeAffiliations.CountAsync());
        var missing = await db.RegulatoryLicensees.SingleAsync(r => r.LicenseNumber == "00003-SA");
        Assert.Equal("Name not published (00003-SA)", missing.Name);
        Assert.Equal("Future unknown status", missing.LicenseStatus);
        Assert.Equal("999-BB", missing.RelatedLicenseNumber);
        Assert.False(missing.IsBicEligible);
        Assert.Equal("Revoked", await db.RegulatoryLicensees.Where(r => r.LicenseNumber == "00002-SA").Select(r => r.LicenseStatus).SingleAsync());
        Assert.Equal(new DateOnly(2025, 1, 2), missing.OriginalLicenseDate);
        Assert.Equal(new DateOnly(2027, 1, 31), missing.LicenseRenewalDate);
        Assert.Equal("Texas", (await RegulatoryRoster.Published(db.RegulatoryDataSnapshots).SingleAsync()).State);
        Assert.Equal(RegulatorySnapshotStatuses.Approved, (await db.RegulatoryDataSnapshots.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancellationAtStagingRollsBackAndPreservesPreviousRoster()
    {
        using var archive = await TexasArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var prior = new RegulatoryDataSnapshot { State = "Texas", SourceName = "Prior", Status = RegulatorySnapshotStatuses.Approved };
        db.Add(prior);
        await db.SaveChangesAsync();
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TexasBulkImportService(db)
            .ImportArchiveAsync(archive.Path, stop.Token, staged: _ => stop.Cancel()));
        Assert.Equal(prior.Id, await db.RegulatoryDataSnapshots.Select(s => s.Id).SingleAsync());
        Assert.Equal(0, await db.RegulatoryFirms.CountAsync());
        Assert.Equal(0, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(0, await db.RegulatoryFirmLicenseeAffiliations.CountAsync());
    }

    [Fact]
    public async Task TexasRestoreUsesActivationAndReacquiringSameArchivePreservesHistoricalLinks()
    {
        using var archive = await TexasArchive.CreateAsync();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new TexasBulkImportService(db);
        var original = await importer.ImportArchiveAsync(archive.Path);
        var admin = new UserAccount { Email = "admin@example.invalid", Role = "Admin", IsActive = true };
        db.Add(admin);
        await db.SaveChangesAsync();
        var record = await db.RegulatoryLicensees.FirstAsync();
        db.UserRegulatoryLicenseLinks.Add(new() { RegulatoryLicenseeId = record.Id, RegulatorySnapshotId = original, UserAccountId = admin.Id });
        await db.SaveChangesAsync();
        await archive.RewriteAsync(TexasArchive.Rows.Replace("Revoked", "Inactive"));
        var newer = await importer.ImportArchiveAsync(archive.Path);
        var directory = new RegulatoryDirectoryService(db, new AuditLogService(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.RestoreFloridaRosterAsync(original, Guid.NewGuid(), admin.Id, "Stale", "Texas"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.RestoreFloridaRosterAsync(original, newer, admin.Id, "Wrong state", "Florida"));
        await directory.RestoreFloridaRosterAsync(original, newer, admin.Id, "Texas recovery test", "Texas");
        Assert.Equal(original, (await directory.GetLatestApprovedSnapshotAsync("Texas"))!.Id);
        Assert.Equal(2, (await directory.GetFloridaRosterHistoryAsync(state: "Texas")).Count);
        Assert.Equal(3, (await directory.GetFloridaRosterRecordsAsync(original, state: "Texas")).Count);
        var comparison = await new RegulatoryStatisticsService(db).GetAsync("Texas", bulkOnly: true);
        Assert.Equal(original, comparison.Incoming!.Id);
        Assert.Equal(newer, comparison.Baseline!.Id);
        Assert.Equal(newer, await importer.ImportArchiveAsync(archive.Path));
        Assert.Equal(newer, (await directory.GetLatestApprovedSnapshotAsync("Texas"))!.Id);
        Assert.Equal(2, await db.RegulatoryDataSnapshots.CountAsync());
        Assert.Equal(original, (await db.UserRegulatoryLicenseLinks.SingleAsync()).RegulatorySnapshotId);
        Assert.All(await directory.GetApprovedLicenseesAsync("Texas"), r => Assert.DoesNotContain("Revoked", r.LicenseStatus ?? ""));
    }

    [Theory]
    [InlineData("License Type", "Unexpected Header")]
    [InlineData("Broker Company", "School")]
    [InlineData("00002-SA", "00002-B")]
    [InlineData("01/02/2025", "02/30/2025")]
    [InlineData(",0,Travis", ",X,Travis")]
    public async Task ChangedSchemaInvalidIdentityDatesAndFlagsFailExplicitly(string from, string to)
    {
        using var archive = await TexasArchive.CreateAsync();
        await archive.RewriteAsync(TexasArchive.Rows.Replace(from, to), TexasArchive.Header.Replace(from, to));
        Assert.Throws<InvalidDataException>(() => TexasBulkImportService.ReadRecords(archive.Csv));
    }

    [Fact]
    public async Task DuplicateCredentialsAndHashTamperingCannotActivate()
    {
        using var archive = await TexasArchive.CreateAsync();
        await File.AppendAllTextAsync(archive.Csv, TexasArchive.Rows.Split('\n')[1] + "\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => TexasBulkImportService.ValidateArchiveAsync(archive.Path));
        await archive.RewriteAsync(TexasArchive.Rows + TexasArchive.Rows.Split('\n')[1] + "\n");
        Assert.Throws<InvalidDataException>(() => TexasBulkImportService.ReadRecords(archive.Csv));
    }

    [Fact]
    public void SavedOfficialCaptureRetainsAllMeasuredRowsWithoutSyntheticEntities()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var path = System.IO.Path.Combine(root!.FullName, "src", "docs", "TEXAS", "LICENSING", "XLXS",
            "Texas_TREC_Broker_and_Sales_Agent_License_Holder_Information_2026-10-10.csv");
        var records = TexasBulkImportService.ReadRecords(path);
        Assert.Equal(326020, records.Count);
        Assert.Equal(19236, records.Count(r => r.Type == "Broker Company"));
        Assert.Equal(42694, records.Count(r => r.Type == "Broker Individual"));
        Assert.Equal(264090, records.Count(r => r.Type == "Sales Agent"));
        Assert.Equal(188538, records.Count(r => r.Status == "Active"));
        Assert.Equal(1142, records.Count(r => r.Status == "Revoked"));
        Assert.Equal(193, records.Count(r => r.Name.StartsWith("Name not published (")));
    }
}

internal sealed class TexasArchive : IDisposable
{
    public const string Header = "License Type,License Number,Full Name,Suffix,Status,Original License Date,License Expiration Date,Designated Supervisor Flag,County,Related License Type,Related License Number,Related License Full Name,Related License Suffix,Related License Start Date,Agency Identifier,Key Name,First Name,Middle Name,Last Name,Updated";
    public const string Rows =
        "Broker Company,00001-BB,\"Example, LLC\",,Active,01/02/2025,01/31/2027,0,Travis,Broker Individual,00001-B,Example Broker,,01/02/2025,1,Example LLC,,,,10/10/2026\n" +
        "Broker Individual,00001-B,Example Broker,,Active,01/02/2025,01/31/2027,0,Travis,,,,,,2,Example Broker,Example,,Broker,10/10/2026\n" +
        "Sales Agent,00002-SA,Example Agent,,Revoked,01/02/2025,01/31/2027,0,Travis,Broker Company,00001-BB,Example LLC,,01/02/2025,3,Example Agent,Example,,Agent,10/10/2026\n" +
        "Sales Agent,00003-SA,,,Future unknown status,01/02/2025,01/31/2027,0,Travis,Broker Company,999-BB,Not present firm,,01/02/2025,4,,,,,10/10/2026\n";
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "texas-roster-test-" + Guid.NewGuid().ToString("N"));
    public string Csv => System.IO.Path.Combine(Path, "license-holders.csv");
    public static async Task<TexasArchive> CreateAsync()
    {
        var archive = new TexasArchive();
        Directory.CreateDirectory(archive.Path);
        await archive.RewriteAsync(Rows);
        return archive;
    }
    public async Task RewriteAsync(string rows, string header = Header)
    {
        var bytes = Encoding.UTF8.GetBytes(header + "\n" + rows);
        await File.WriteAllBytesAsync(Csv, bytes);
        var entries = new[] { new FloridaBulkManifestEntry("license-holders.csv", rows.Count(c => c == '\n'), bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)), TexasBulkImportService.SourceUrl, DateTimeOffset.UtcNow,
            "synthetic test", new() { ["20"] = rows.Count(c => c == '\n') })
        { SourceRowCount = rows.Count(c => c == '\n'), SourceRowCountUrl = TexasBulkImportService.RowCountUrl,
            SourceRowCountVerifiedAtUtc = DateTimeOffset.UtcNow } };
        await File.WriteAllTextAsync(System.IO.Path.Combine(Path, "manifest.json"), JsonSerializer.Serialize(entries));
    }
    public void Dispose() => Directory.Delete(Path, true);
}
