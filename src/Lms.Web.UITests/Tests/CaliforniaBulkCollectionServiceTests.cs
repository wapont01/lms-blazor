using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Application.Tests.Services;
using Lms.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class CaliforniaBulkCollectionServiceTests
{
    [Fact]
    public async Task OfficialBinaryDownloadActivationBrowsingRestartAndFailurePreserveFlorida()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        await fixture.Collection.ImportArchiveAsync(await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root));
        var florida = (await fixture.Collection.GetStatusAsync()).SnapshotId;
        using var archive = await CaliforniaArchive.CreateAsync();
        fixture.Handler.SourceArchive = archive.Path;
        await using var california = fixture.NewCaliforniaCollection();
        await california.CollectAsync();
        var status = await california.GetStatusAsync();
        Assert.Equal("Completed", status.State);
        Assert.Equal(2, status.Files.Count);
        Assert.Equal(8, status.Files.Sum(f => f.Rows));
        Assert.Equal(2, fixture.Handler.Calls);
        var browser = new CaliforniaBulkBrowseService(california, fixture.Services.GetRequiredService<IServiceScopeFactory>());
        Assert.Equal(7, (await browser.GetAsync(new(), CancellationToken.None))!.Total);
        var officers = await browser.GetAsync(new(Type: "Officer", NameOrLicense: "00001234", Status: "nba"), CancellationToken.None);
        Assert.Equal(2, officers!.Records.Count);
        Assert.All(officers.Records, r => Assert.True(r.IsAdditionalLicenseRecord));
        var associate = await browser.GetAsync(new(NameOrLicense: "00007890"), CancellationToken.None);
        Assert.Equal("Status not published", Assert.Single(associate!.Records).LicenseStatus);
        Assert.Equal("00009998", Assert.Single(associate.AssociateRelationships).ResponsibleNumber);
        Assert.Equal(1, (await browser.GetAsync(new(Page: 500), CancellationToken.None))!.Page);
        await Assert.ThrowsAsync<ArgumentException>(() => california.GetExportPathAsync("ExamineeList.zip"));
        var export = await california.GetExportPathAsync("CurrList.zip");
        await using var restart = fixture.NewCaliforniaCollection();
        Assert.Equal(status.SnapshotId, (await restart.GetStatusAsync()).SnapshotId);
        await File.WriteAllTextAsync(Path.Combine(archive.Path, "CurrList.zip"), "Incomplete new ZIP");
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => restart.CollectAsync());
        Assert.Equal(status.SnapshotId, (await restart.GetStatusAsync()).SnapshotId);
        Assert.Equal(export, await restart.GetExportPathAsync("CurrList.zip"));
        Assert.Equal(florida, (await fixture.Collection.GetStatusAsync()).SnapshotId);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.RegulatoryFirms.CountAsync(r => r.State == "California"));
        Assert.False(await db.RegulatoryFirms.AnyAsync(r => r.State == "California" && r.LicenseNumber == "00009998"));
    }

    [Fact]
    public async Task CaliforniaCancellationIsRestartableAndIsolatedFromFloridaAndTexas()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        await using var california = fixture.NewCaliforniaCollection();
        await using var texas = fixture.NewTexasCollection();
        await california.StartAsync();
        await fixture.Handler.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await california.CancelCollectionAsync();
        Assert.Equal("Cancelled", (await california.GetStatusAsync()).State);
        Assert.Equal("Idle", (await fixture.Collection.GetStatusAsync()).State);
        Assert.Equal("Idle", (await texas.GetStatusAsync()).State);
        await california.StartAsync();
        await california.CancelCollectionAsync();
        Assert.Equal("Cancelled", (await california.GetStatusAsync()).State);
    }
}
