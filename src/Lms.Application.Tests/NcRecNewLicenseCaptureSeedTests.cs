using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public class NcRecNewLicenseCaptureSeedTests
{
    [Fact]
    public async Task SeedAsync_ImportsWhatsNewCaptures_AndIsIdempotent()
    {
        var workspaceRoot = FindWorkspaceRoot();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        await NcRecNewLicenseCaptureSeed.SeedAsync(dbContext, workspaceRoot);
        await NcRecNewLicenseCaptureSeed.SeedAsync(dbContext, workspaceRoot);

        var snapshots = await dbContext.RegulatoryDataSnapshots
            .Where(snapshot => snapshot.ParserVersion == "ncrec-whats-new-v1")
            .ToListAsync();
        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal("North Carolina", snapshot.State);
            Assert.Equal(RegulatorySnapshotStatuses.Approved, snapshot.Status);
        });

        // one snapshot per capture file, even after a second seed run
        var snapshotIds = snapshots.Select(snapshot => snapshot.Id).ToList();
        var licenseeCount = await dbContext.RegulatoryLicensees.CountAsync(licensee => snapshotIds.Contains(licensee.SnapshotId));
        Assert.True(licenseeCount >= 46);

        // freshly issued provisional broker from the 2026-09-17 --new run
        var newPb = await dbContext.RegulatoryLicensees.SingleAsync(licensee =>
            snapshotIds.Contains(licensee.SnapshotId) && licensee.LicenseNumber == "368498");
        Assert.Equal("MARTINEZ SORIANO, JAZMIN", newPb.Name);
        Assert.Equal("PB", newPb.LicenseLevel);
        Assert.Equal(new DateOnly(2026, 9, 16), newPb.LicenseIssuedDate);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs", "NORTH CAROLINA")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Workspace root with docs folder not found.");
    }
}
