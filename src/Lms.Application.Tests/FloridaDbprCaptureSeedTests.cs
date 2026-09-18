using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public class FloridaDbprCaptureSeedTests
{
    [Fact]
    public async Task SeedAsync_ImportsQualifyingBrokersAndSalesAssociates()
    {
        var workspaceRoot = FindWorkspaceRoot();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        await FloridaDbprCaptureSeed.SeedAsync(dbContext, workspaceRoot);
        await FloridaDbprCaptureSeed.SeedAsync(dbContext, workspaceRoot);

        var snapshot = await dbContext.RegulatoryDataSnapshots.SingleAsync(candidate => candidate.State == "Florida");
        Assert.Equal(RegulatorySnapshotStatuses.Approved, snapshot.Status);

        var firmCount = await dbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id);
        Assert.Equal(9, firmCount);

        // 5 qualifying brokers plus 60 employed associates; firm names starting with digits must not break roster matching
        var licenseeCount = await dbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id);
        Assert.Equal(65, licenseeCount);

        var associate = await dbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "SL698466");
        Assert.Equal("Sales Associate", associate.LicenseLevel);

        var flamingoAffiliations = await dbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Where(affiliation => affiliation.SnapshotId == snapshot.Id && affiliation.Firm!.LegalName == "1 FLAMINGO REALTY LLC")
            .CountAsync();
        Assert.True(flamingoAffiliations > 1);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs", "FLORIDA")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Workspace root with docs folder not found.");
    }
}
