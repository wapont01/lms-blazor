using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public class NcRecCharlotteCaptureSeedTests
{
    [Fact]
    public async Task SeedAsync_ImportsCharlotteCapture_AndIsIdempotent()
    {
        var workspaceRoot = FindWorkspaceRoot();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        await NcRecCharlotteCaptureSeed.SeedAsync(dbContext, workspaceRoot);
        await NcRecCharlotteCaptureSeed.SeedAsync(dbContext, workspaceRoot);

        var snapshot = await dbContext.RegulatoryDataSnapshots.SingleAsync(candidate =>
            candidate.SourceUrl == "https://license.ncrec.gov/ncrec/oecgi3.exe/O4W_FIRM_SEARCH_NEW");
        Assert.Equal("North Carolina", snapshot.State);
        Assert.Equal(RegulatorySnapshotStatuses.Approved, snapshot.Status);

        var firmCount = await dbContext.RegulatoryFirms.CountAsync(firm => firm.SnapshotId == snapshot.Id);
        Assert.Equal(65, firmCount);

        // 92 BIC detail records plus 1,400+ supervised licensees from the BIC rosters.
        var licenseeCount = await dbContext.RegulatoryLicensees.CountAsync(licensee => licensee.SnapshotId == snapshot.Id);
        Assert.InRange(licenseeCount, 1000, 2000);

        // BIC detail record: issue date captured from the license detail page.
        var holbert = await dbContext.RegulatoryLicensees.SingleAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "132919");
        Assert.Equal(new DateOnly(1991, 7, 3), holbert.LicenseIssuedDate);
        Assert.Equal("B", holbert.LicenseLevel);
        Assert.True(holbert.IsBicEligible);

        // Supervised roster row: renewal date and CE hours captured from the BIC roster page.
        var supervised = await dbContext.RegulatoryLicensees.FirstAsync(licensee =>
            licensee.SnapshotId == snapshot.Id && licensee.LicenseNumber == "280913");
        Assert.Equal(new DateOnly(2026, 5, 17), supervised.LicenseRenewalDate);
        Assert.NotNull(supervised.UpdateHours);

        var bicAffiliation = await dbContext.RegulatoryFirmLicenseeAffiliations
            .Include(affiliation => affiliation.Firm)
            .Include(affiliation => affiliation.Licensee)
            .SingleAsync(affiliation => affiliation.SnapshotId == snapshot.Id
                && affiliation.Licensee!.LicenseNumber == "132919" && affiliation.IsBrokerInCharge);
        Assert.Equal("C38999", bicAffiliation.Firm!.LicenseNumber);

        var supervisedAffiliations = await dbContext.RegulatoryFirmLicenseeAffiliations
            .CountAsync(affiliation => affiliation.SnapshotId == snapshot.Id && !affiliation.IsBrokerInCharge);
        Assert.True(supervisedAffiliations > 500);
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
