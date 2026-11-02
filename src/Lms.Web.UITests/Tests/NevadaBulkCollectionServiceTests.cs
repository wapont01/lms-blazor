using System.IO.Compression;
using System.Xml.Linq;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Lms.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class NevadaBulkCollectionServiceTests
{
    [Fact]
    public async Task DownloadsAllWorkbooksActivatesComparesAndPreservesCurrentOnFailure()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        fixture.Handler.SourceArchive = await CreateSourceAsync(fixture.Root, "first", "First synthetic licensee");
        await using var collection = fixture.NewNevadaCollection();
        await collection.CollectAsync();
        var first = await collection.GetStatusAsync();
        Assert.Equal("Completed", first.State);
        Assert.Equal(12, first.Files.Count);
        Assert.Equal(12, fixture.Handler.Calls);
        Assert.All(first.Files, file => Assert.Equal(1, file.Rows));
        await collection.CollectAsync();
        Assert.Equal(first.SnapshotId, (await collection.GetStatusAsync()).SnapshotId);
        fixture.Handler.SourceArchive = await CreateSourceAsync(fixture.Root, "second", "Updated synthetic licensee");
        await collection.CollectAsync();
        var second = await collection.GetStatusAsync();
        Assert.NotEqual(first.SnapshotId, second.SnapshotId);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stats = await new RegulatoryStatisticsService(db).GetAsync("Nevada", bulkOnly: true);
            Assert.Equal(first.SnapshotId, stats.Baseline!.Id);
            Assert.Equal(second.SnapshotId, stats.Incoming!.Id);
            Assert.Equal(11, stats.Records.Count);
            Assert.All(stats.Records, row => Assert.Equal("Other updates", row.Classification));
            Assert.All(await db.RegulatoryDataSnapshots.ToListAsync(),
                snapshot => Assert.Equal(RegulatorySnapshotStatuses.Approved, snapshot.Status));
            Assert.Equal(24, await db.RegulatorySourceRecords.CountAsync());
            var browser = new NevadaBulkBrowseService(fixture.Services.GetRequiredService<IServiceScopeFactory>());
            var all = await browser.GetAsync(new());
            Assert.Equal(11, all!.Total);
            Assert.All(all.Records, row => Assert.Equal("Updated synthetic licensee", row.Name));
            var filtered = await browser.GetAsync(new("Salesperson", "s.00001"));
            Assert.Equal("S.00001", Assert.Single(filtered!.Records).Number);
            Assert.Empty((await browser.GetAsync(new(NameOrLicense: "not-a-license")))!.Records);
        }
        var export = await collection.GetExportPathAsync("NRED-ACTIVE-BROKERS.xlsx");
        await File.WriteAllTextAsync(Path.Combine(fixture.Handler.SourceArchive, "NRED-ACTIVE-BROKERS.xlsx"), "Not a workbook");
        await Assert.ThrowsAsync<InvalidDataException>(() => collection.CollectAsync());
        Assert.Equal("Failed", (await collection.GetStatusAsync()).State);
        Assert.Equal(second.SnapshotId, (await collection.GetStatusAsync()).SnapshotId);
        Assert.Equal(export, await collection.GetExportPathAsync("NRED-ACTIVE-BROKERS.xlsx"));
        await using var restarted = fixture.NewNevadaCollection();
        Assert.Equal(second.SnapshotId, (await restarted.GetStatusAsync()).SnapshotId);
        Assert.Equal("Idle", (await fixture.Collection.GetStatusAsync()).State);
    }

    [Fact]
    public async Task CancellationPublishesNothingAndDoesNotStopOtherStateCollectors()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        await using var collection = fixture.NewNevadaCollection();
        await collection.StartAsync();
        await fixture.Handler.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await collection.CancelCollectionAsync();
        var status = await collection.GetStatusAsync();
        Assert.Equal("Cancelled", status.State);
        Assert.Null(status.SnapshotId);
        Assert.Equal("Idle", (await fixture.Collection.GetStatusAsync()).State);
    }

    [Fact]
    public async Task LookupPagesOnlyTheCurrentRosterAndDoesNotMutateOwnership()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var previous = new RegulatoryDataSnapshot
        {
            State = "Nevada", Status = RegulatorySnapshotStatuses.Approved,
            ParserVersion = NevadaNredBulkImportService.ParserVersion, RetrievedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        var current = new RegulatoryDataSnapshot
        {
            State = "Nevada", Status = RegulatorySnapshotStatuses.Approved,
            ParserVersion = NevadaNredBulkImportService.ParserVersion, RetrievedAtUtc = DateTime.UtcNow
        };
        db.RegulatoryDataSnapshots.AddRange(previous, current);
        db.RegulatoryLicensees.Add(new() { SnapshotId = previous.Id, State = "Nevada", LicenseNumber = "OLD", Name = "Previous" });
        db.RegulatoryLicensees.AddRange(Enumerable.Range(1, 53).Select(number => new RegulatoryLicensee
        {
            SnapshotId = current.Id, State = "Nevada", LicenseNumber = $"S.{number:D5}",
            Name = $"Synthetic {number:D3}", LicenseType = "Salesperson"
        }));
        await db.SaveChangesAsync();
        var browser = new NevadaBulkBrowseService(fixture.Services.GetRequiredService<IServiceScopeFactory>());
        var first = await browser.GetAsync(new());
        var last = await browser.GetAsync(new(Page: 100));
        Assert.Equal(53, first!.Total);
        Assert.Equal(50, first.Records.Count);
        Assert.Equal(1, last!.Page);
        Assert.Equal(3, last.Records.Count);
        Assert.Equal("S.00051", last.Records[0].Number);
        Assert.Equal(current.Id, last.SnapshotId);
        Assert.Empty((await browser.GetAsync(new(NameOrLicense: "OLD")))!.Records);
        Assert.Empty(await db.UserRegulatoryLicenseLinks.ToListAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => browser.GetAsync(new(Type: "Unknown type")));
    }

    [Fact]
    public async Task RecollectionAfterRestoreReactivatesTheValidatedVersionWithoutDuplicatingIt()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        fixture.Handler.SourceArchive = await CreateSourceAsync(fixture.Root, "first", "First");
        await using var collection = fixture.NewNevadaCollection();
        await collection.CollectAsync();
        var first = (await collection.GetStatusAsync()).SnapshotId;
        fixture.Handler.SourceArchive = await CreateSourceAsync(fixture.Root, "second", "Second");
        await collection.CollectAsync();
        var second = (await collection.GetStatusAsync()).SnapshotId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.RegulatoryDataSnapshots.SingleAsync(snapshot => snapshot.Id == first)).ActivatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await collection.CollectAsync();
        await using var check = fixture.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(second, await RegulatoryRoster.Published(checkDb.RegulatoryDataSnapshots)
            .Select(snapshot => (Guid?)snapshot.Id).FirstAsync());
        Assert.Equal(2, await checkDb.RegulatoryDataSnapshots.CountAsync());
    }

    private static async Task<string> CreateSourceAsync(string root, string folder, string name)
    {
        var path = Path.Combine(root, folder);
        Directory.CreateDirectory(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var file in NevadaNredBulkImportService.Definitions)
        {
            using var archive = ZipFile.Open(Path.Combine(path, file.Name), ZipArchiveMode.Create);
            var sheet = archive.CreateEntry("xl/worksheets/sheet1.xml");
            await using var stream = sheet.Open();
            var headers = new[] { file.Name == "NRED-ACTIVE-HOA.xlsx" ? "SOS#" : "License No", "Name", "Company" };
            string[] values = ["S.00001", name, "Synthetic company"];
            XElement Row(string[] fields, int row) => new(ns + "row", fields.Select((value, index) =>
                new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + index)}{row}"),
                    new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value)))));
            var document = new XDocument(new XElement(ns + "worksheet",
                new XElement(ns + "sheetData", Row(headers, 1), Row(values, 2))));
            await document.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
        }
        return path;
    }
}
