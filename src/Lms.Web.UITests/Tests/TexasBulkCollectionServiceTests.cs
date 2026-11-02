using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Lms.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class TexasBulkCollectionServiceTests
{
    [Fact]
    public async Task OfficialDownloadActivationFullArchiveFiltersRestartAndFailureAreIsolatedFromFlorida()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        await fixture.Collection.ImportArchiveAsync(await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root));
        var florida = (await fixture.Collection.GetStatusAsync()).SnapshotId;
        var archive = Path.Combine(fixture.Root, "texas-input");
        Directory.CreateDirectory(archive);
        const string header = "License Type,License Number,Full Name,Suffix,Status,Original License Date,License Expiration Date,Designated Supervisor Flag,County,Related License Type,Related License Number,Related License Full Name,Related License Suffix,Related License Start Date,Agency Identifier,Key Name,First Name,Middle Name,Last Name,Updated";
        const string rows =
            "Broker Company,123-BB,Example Firm,,Active,01/01/2025,01/31/2027,0,Travis,,,,,,1,Example Firm,,,,10/10/2026\n" +
            "Broker Individual,00123-B,Example Broker,,Active,01/01/2025,01/31/2027,0,Travis,Broker Company,123-BB,Example Firm,,01/01/2025,2,Example Broker,Example,,Broker,10/10/2026\n" +
            "Sales Agent,00124-SA,Example Agent,,Suspended,01/01/2025,01/31/2027,0,Dallas,Broker Individual,00123-B,Example Broker,,01/01/2025,3,Example Agent,Example,,Agent,10/10/2026\n";
        var bytes = Encoding.UTF8.GetBytes(header + "\n" + rows);
        await File.WriteAllBytesAsync(Path.Combine(archive, "license-holders.csv"), bytes);
        await File.WriteAllTextAsync(Path.Combine(archive, "manifest.json"),
            JsonSerializer.Serialize(new[] { new FloridaBulkManifestEntry("license-holders.csv", 3, bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)), TexasBulkImportService.SourceUrl, DateTimeOffset.UtcNow,
                "synthetic source", new() { ["20"] = 3 }) }));
        fixture.Handler.SourceArchive = archive;
        await using var texas = fixture.NewTexasCollection();
        await texas.CollectAsync();
        var status = await texas.GetStatusAsync();
        Assert.Equal("Completed", status.State);
        Assert.Single(status.Files);
        Assert.Equal(3, status.Files[0].Rows);
        Assert.Equal(2, fixture.Handler.Calls);
        Assert.NotEqual(florida, status.SnapshotId);
        var browser = new TexasBulkBrowseService(texas);
        Assert.Equal(3, (await browser.GetAsync(new(), CancellationToken.None))!.Total);
        var filtered = await browser.GetAsync(new(Type: "Sales Agent", County: " dallas ", Status: "suspended",
            NameOrLicense: "00124"), CancellationToken.None);
        Assert.Equal("00124-SA", Assert.Single(filtered!.Records).Number);
        Assert.Equal("00123-B", filtered.Records[0].RelatedNumber);
        await Assert.ThrowsAsync<ArgumentException>(() => texas.GetExportPathAsync("REALESTATE2501LICENSE_1.csv"));
        var export = await texas.GetExportPathAsync("license-holders.csv");
        await using var restart = fixture.NewTexasCollection();
        Assert.Equal(status.SnapshotId, (await restart.GetStatusAsync()).SnapshotId);
        fixture.Handler.PublishedRows = 2;
        await Assert.ThrowsAsync<InvalidDataException>(() => restart.CollectAsync());
        Assert.Equal(status.SnapshotId, (await restart.GetStatusAsync()).SnapshotId);
        fixture.Handler.PublishedRows = 3;
        await File.WriteAllTextAsync(Path.Combine(archive, "license-holders.csv"), "Changed format");
        await Assert.ThrowsAsync<InvalidDataException>(() => restart.CollectAsync());
        Assert.Equal("Failed", (await restart.GetStatusAsync()).State);
        Assert.Equal(status.SnapshotId, (await restart.GetStatusAsync()).SnapshotId);
        Assert.Equal(export, await restart.GetExportPathAsync("license-holders.csv"));
        Assert.Equal(florida, (await fixture.Collection.GetStatusAsync()).SnapshotId);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.RegulatoryDataSnapshots.CountAsync(s => s.Status == RegulatorySnapshotStatuses.Approved));
        Assert.Equal(1, await db.RegulatoryFirms.CountAsync(r => r.State == "Texas"));
    }

    [Fact]
    public async Task TexasCancellationDoesNotCancelFloridaOrShutDownTheHost()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        await using var texas = fixture.NewTexasCollection();
        await texas.StartAsync();
        await fixture.Handler.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await texas.CancelCollectionAsync();
        Assert.Equal("Cancelled", (await texas.GetStatusAsync()).State);
        Assert.Equal("Idle", (await fixture.Collection.GetStatusAsync()).State);
        await texas.StartAsync();
        await texas.CancelCollectionAsync();
        Assert.Equal("Cancelled", (await texas.GetStatusAsync()).State);
    }
}
