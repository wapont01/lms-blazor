using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Lms.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class FloridaBulkCollectionServiceTests
{
    [Fact]
    public async Task ScheduledCompletionEvidencePersistsAcrossManualRunsFailuresAndRestart()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        fixture.Handler.SourceArchive = await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root);
        await fixture.Collection.StartAsync(scheduled: true);
        var status = await fixture.Collection.GetStatusAsync();
        for (var attempt = 0; attempt < 150 && status.State != "Completed" && status.State != "Failed"; attempt++)
        {
            await Task.Delay(100);
            status = await fixture.Collection.GetStatusAsync();
        }
        Assert.Equal("Completed", status.State);
        Assert.Equal(status.CompletedAtUtc, status.LastScheduledSuccessAtUtc);
        Assert.Equal(status.SnapshotId, status.LastScheduledSuccessSnapshotId);
        await fixture.Collection.CollectAsync();
        var manual = await fixture.Collection.GetStatusAsync();
        Assert.False(manual.ScheduledRun);
        Assert.Equal(status.LastScheduledSuccessAtUtc, manual.LastScheduledSuccessAtUtc);
        await File.WriteAllTextAsync(Path.Combine(fixture.Handler.SourceArchive,
            FloridaBulkImportService.Definitions[0].RelativePath), "Invalid");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Collection.CollectAsync());
        await using var restarted = fixture.NewCollection();
        var persisted = await restarted.GetStatusAsync();
        Assert.Equal("Failed", persisted.State);
        Assert.Equal(status.LastScheduledSuccessAtUtc, persisted.LastScheduledSuccessAtUtc);
        Assert.Equal(status.SnapshotId, persisted.LastScheduledSuccessSnapshotId);
    }

    [Fact]
    public async Task OverdueWeeklyRefreshPublishesChangedVersionAndPreservesBothRosters()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(true);
        var archive = await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root);
        await fixture.Collection.ImportArchiveAsync(archive);
        var previous = (await fixture.Collection.GetStatusAsync()).SnapshotId;
        await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root, "Inactive");
        fixture.Handler.SourceArchive = archive;
        var path = Path.Combine(fixture.Root, "stored", "status.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["LastSuccessUtc"] = DateTime.UtcNow.AddDays(-8);
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await using var restarted = fixture.NewCollection();
        using var scheduler = new FloridaBulkRefreshBackgroundService(restarted,
            fixture.Services.GetRequiredService<ILogger<FloridaBulkRefreshBackgroundService>>());
        await scheduler.StartAsync(CancellationToken.None);
        FloridaBulkCollectionStatus status = await restarted.GetStatusAsync();
        for (var attempt = 0; attempt < 100 && (status.State != "Completed" || status.SnapshotId == previous); attempt++)
        {
            await Task.Delay(100);
            status = await restarted.GetStatusAsync();
        }
        Assert.Equal("Completed", status.State);
        Assert.NotEqual(previous, status.SnapshotId);
        Assert.Equal(6, fixture.Handler.Calls);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.RegulatoryDataSnapshots.CountAsync());
        Assert.Equal(2, await db.RegulatoryDataSnapshots.CountAsync(s => s.Status == RegulatorySnapshotStatuses.Approved));
        var current = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots).FirstAsync();
        Assert.Equal(status.SnapshotId, current.Id);
        Assert.Equal("Current, Inactive", await db.RegulatoryLicensees.Where(r => r.SnapshotId == current.Id && r.LicenseNumber == "BK12").Select(r => r.LicenseStatus).SingleAsync());
        var stats = await new RegulatoryStatisticsService(db).GetAsync("Florida", bulkOnly: true);
        Assert.Equal(previous, stats.Baseline!.Id);
        Assert.Equal(current.Id, stats.Incoming!.Id);
        Assert.Contains(stats.Records, record => record.Classification == "Status changed");
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AdminCancellationRetainsLastSuccessAndAllowsAnotherRunWithoutShuttingDown()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(true);
        await fixture.Collection.ImportArchiveAsync(await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root));
        var before = await fixture.Collection.GetStatusAsync();
        var export = await fixture.Collection.GetExportPathAsync("manifest.json");
        await fixture.Collection.StartAsync();
        await fixture.Handler.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Collection.CancelCollectionAsync();
        var stopped = await fixture.Collection.GetStatusAsync();
        Assert.Equal("Cancelled", stopped.State);
        Assert.Equal(before.SnapshotId, stopped.SnapshotId);
        Assert.Equal(before.CompletedAtUtc, stopped.CompletedAtUtc);
        Assert.NotNull(stopped.FinishedAtUtc);
        Assert.Equal(export, await fixture.Collection.GetExportPathAsync("manifest.json"));
        Assert.Contains("No partial snapshot", stopped.Error);
        Assert.True(stopped.NextRunAtUtc >= DateTime.UtcNow.AddDays(7).AddSeconds(-5));
        await fixture.Collection.StartAsync();
        await fixture.Collection.CancelCollectionAsync();
        Assert.Equal("Cancelled", (await fixture.Collection.GetStatusAsync()).State);
        await fixture.Collection.ImportArchiveAsync(await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root));
        Assert.Equal("Completed", (await fixture.Collection.GetStatusAsync()).State);
    }

    [Fact]
    public async Task DownloadReportsReceivedBytesAgainstSourceContentLengthBeforeCancellation()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        fixture.Handler.SimulateTransfer = true;
        await fixture.Collection.StartAsync();
        await fixture.Handler.Transferred.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // The next read begins only after the coordinator has recorded the preceding chunk.
        var progress = Assert.Single((await fixture.Collection.GetStatusAsync()).Files, f => f.Name == "REALESTATE2501LICENSE_1.csv");
        Assert.Equal(128, progress.Bytes);
        Assert.Equal(1024, progress.ExpectedBytes);
        await fixture.Collection.CancelCollectionAsync();
        Assert.Equal("Cancelled", (await fixture.Collection.GetStatusAsync()).State);
    }

    [Fact]
    public async Task BrowseUsesFullArchiveExactCitiesCombinedFiltersAndValidatedLicenseNumbers()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        var source = await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root);
        await fixture.Collection.ImportArchiveAsync(source);
        var browser = new FloridaBulkBrowseService(fixture.Collection);
        var all = await browser.GetAsync(new(), CancellationToken.None);
        Assert.Equal(4, all!.Total);
        var person = await browser.GetAsync(new(Category: "2501", Qualification: "BK",
            County: " dade ", City: " miami ", NameOrLicense: "BK12", Status: "active"), CancellationToken.None);
        Assert.Equal(1, person!.Total);
        Assert.Equal("BK12", Assert.Single(person.Records).LicenseNumber);
        Assert.Equal(0, (await browser.GetAsync(new(City: "MAIN"), CancellationToken.None))!.Total);
        var appraisal = await browser.GetAsync(new(Category: "appraisal", County: "23", City: "MIAMI"), CancellationToken.None);
        Assert.Equal("RD360", Assert.Single(appraisal!.Records).LicenseNumber);
        Assert.Equal("C, A", appraisal.Records[0].Status);
        var secondPage = await browser.GetAsync(new(Page: 1), CancellationToken.None);
        Assert.Equal(4, secondPage!.Total);
        Assert.Empty(secondPage.Records);
        await Assert.ThrowsAsync<ArgumentException>(() => browser.GetAsync(new(Page: -1), CancellationToken.None));
    }

    [Fact]
    public async Task ArchiveImportSurvivesRestartSchedulesFromCompletionAndRetainsLastExportAfterFailure()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        var source = await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root);
        await fixture.Collection.ImportArchiveAsync(source);
        var status = await fixture.Collection.GetStatusAsync();
        Assert.Equal("Completed", status.State);
        Assert.Equal(status.CompletedAtUtc!.Value.AddDays(7), status.NextRunAtUtc);
        Assert.Equal(6, status.Files.Count);
        Assert.Equal(0, fixture.Handler.Calls);
        var export = await fixture.Collection.GetExportPathAsync("REALESTATE2501LICENSE_1.csv");
        Assert.NotNull(export);
        Assert.True(File.Exists(export));
        Assert.All(status.Files, file => Assert.Contains("not downloaded", file.State));
        await using var restarted = fixture.NewCollection();
        var restored = await restarted.GetStatusAsync();
        Assert.Equal(status.SnapshotId, restored.SnapshotId);
        Assert.Equal(status.NextRunAtUtc, restored.NextRunAtUtc);
        Assert.Equal(export, await restarted.GetExportPathAsync("REALESTATE2501LICENSE_1.csv"));
        await Assert.ThrowsAnyAsync<Exception>(() => restarted.ImportArchiveAsync(Path.Combine(fixture.Root, "missing")));
        Assert.Equal("Failed", (await restarted.GetStatusAsync()).State);
        Assert.Equal(export, await restarted.GetExportPathAsync("REALESTATE2501LICENSE_1.csv"));
        Assert.Equal(status.NextRunAtUtc, (await restarted.GetStatusAsync()).NextRunAtUtc);
        await Assert.ThrowsAsync<ArgumentException>(() => restarted.GetExportPathAsync("../manifest.json"));
    }

    [Fact]
    public async Task StartReturnsBeforeDownloadDuplicateIsRejectedAndStoppingCancelsTheRun()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        await fixture.Collection.StartAsync();
        await fixture.Handler.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Downloading", (await fixture.Collection.GetStatusAsync()).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Collection.StartAsync());
        await fixture.Collection.StopAsync();
        Assert.Equal("Cancelled", (await fixture.Collection.GetStatusAsync()).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Collection.StartAsync());
        Assert.Equal(1, fixture.Handler.Calls);
    }

    [Fact]
    public async Task DisabledWeeklySchedulerNeverAutopopulatesAndShowsNoNextRun()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(false);
        using var scheduler = new FloridaBulkRefreshBackgroundService(fixture.Collection,
            fixture.Services.GetRequiredService<ILogger<FloridaBulkRefreshBackgroundService>>());
        await scheduler.StartAsync(CancellationToken.None);
        Assert.Equal("Idle", (await fixture.Collection.GetStatusAsync()).State);
        Assert.Null((await fixture.Collection.GetStatusAsync()).NextRunAtUtc);
        Assert.Equal(0, fixture.Handler.Calls);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InterruptedStatusIsTransparentAndDoesNotErasePreviousSuccess()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        var source = await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root);
        await fixture.Collection.ImportArchiveAsync(source);
        var before = await fixture.Collection.GetStatusAsync();
        var statusPath = Path.Combine(fixture.Root, "stored", "status.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(statusPath))!;
        json["Status"]!["State"] = "Importing";
        await File.WriteAllTextAsync(statusPath, json.ToJsonString());
        await using var restarted = fixture.NewCollection();
        var status = await restarted.GetStatusAsync();
        Assert.Equal("Interrupted", status.State);
        Assert.Contains("stopped", status.Error);
        Assert.Equal(before.NextRunAtUtc, status.NextRunAtUtc);
        Assert.NotNull(await restarted.GetExportPathAsync("manifest.json"));
    }

    [Fact]
    public async Task EnabledWeeklySchedulerDoesNotRefetchARecentlyCompletedArchive()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync(true);
        await fixture.Collection.ImportArchiveAsync(await FloridaBulkTestFixture.CreateArchiveAsync(fixture.Root));
        await using var restarted = fixture.NewCollection();
        using var scheduler = new FloridaBulkRefreshBackgroundService(restarted,
            fixture.Services.GetRequiredService<ILogger<FloridaBulkRefreshBackgroundService>>());
        await scheduler.StartAsync(CancellationToken.None);
        Assert.Equal("Completed", (await restarted.GetStatusAsync()).State);
        Assert.Equal(0, fixture.Handler.Calls);
        await scheduler.StopAsync(CancellationToken.None);
    }
}

internal sealed class FloridaBulkTestFixture : IAsyncDisposable
{
    public string Root { get; }
    public ServiceProvider Services { get; }
    public FloridaBulkCollectionService Collection { get; }
    public BlockingBulkHandler Handler { get; }
    private readonly bool weeklyEnabled;

    private FloridaBulkTestFixture(string root, ServiceProvider services, BlockingBulkHandler handler, bool enabled)
    {
        Root = root;
        Services = services;
        Handler = handler;
        weeklyEnabled = enabled;
        Collection = NewCollection();
    }

    public FloridaBulkCollectionService NewCollection() => new(Services.GetRequiredService<IServiceScopeFactory>(),
        new TestHttpFactory(Handler), new TestLifetime(), new TestEnvironment(Root),
        Options.Create(new FloridaBulkCollectionOptions { ArchiveDirectory = "stored", WeeklyEnabled = weeklyEnabled }),
        Services.GetRequiredService<ILogger<FloridaBulkCollectionService>>());

    public TexasBulkCollectionService NewTexasCollection() => new(Services.GetRequiredService<IServiceScopeFactory>(),
        new TestHttpFactory(Handler, "TexasBulk"), new TestLifetime(), new TestEnvironment(Root),
        Options.Create(new TexasBulkCollectionOptions { ArchiveDirectory = "texas-stored", WeeklyEnabled = weeklyEnabled }),
        Services.GetRequiredService<ILogger<TexasBulkCollectionService>>());

    public CaliforniaBulkCollectionService NewCaliforniaCollection() => new(Services.GetRequiredService<IServiceScopeFactory>(),
        new TestHttpFactory(Handler, "CaliforniaBulk"), new TestLifetime(), new TestEnvironment(Root),
        Options.Create(new CaliforniaBulkCollectionOptions { ArchiveDirectory = "california-stored", WeeklyEnabled = weeklyEnabled }),
        Services.GetRequiredService<ILogger<CaliforniaBulkCollectionService>>());

    public NevadaBulkCollectionService NewNevadaCollection() => new(Services.GetRequiredService<IServiceScopeFactory>(),
        new TestHttpFactory(Handler, "NevadaBulk"), new TestLifetime(), new TestEnvironment(Root),
        Options.Create(new NevadaBulkCollectionOptions { ArchiveDirectory = "nevada-stored", WeeklyEnabled = weeklyEnabled }),
        Services.GetRequiredService<ILogger<NevadaBulkCollectionService>>());

    public static async Task<FloridaBulkTestFixture> CreateAsync(bool weeklyEnabled = true)
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), ".florida-bulk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root, "test.db")};Pooling=False"));
        services.AddScoped<FloridaBulkImportService>();
        services.AddScoped<TexasBulkImportService>();
        services.AddScoped<CaliforniaBulkImportService>();
        services.AddScoped<NevadaNredBulkImportService>();
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
        return new(root, provider, new BlockingBulkHandler(), weeklyEnabled);
    }

    public static async Task<string> CreateArchiveAsync(string root, string secondaryStatus = "Active")
    {
        var archive = Path.Combine(root, "input");
        Directory.CreateDirectory(archive);
        var entries = new List<FloridaBulkManifestEntry>();
        foreach (var d in FloridaBulkImportService.Definitions)
        {
            string[] fields = d.Columns switch
            {
                12 => ["BK", "Broker", "BK12", "Person", "1 MAIN ST", "", "MIAMI FL 33101", "09/30/2027", "123", "CE", "14", "06/30/2025"],
                20 => ["64", "RD", "Appraiser", "", "", "1 MAIN ST", "", "", "MIAMI", "FL", "33101", "23", "0000360", "C", "A", "01/25/1994", "09/13/2000", "11/30/2028", "", "1"],
                _ => ["2501 Real Estate Broker or Sales", "Person Example", "", "BK Broker", "1 MAIN ST", "", "", "MIAMI", "FL", "33101", "Dade", "12",
                    "Current", secondaryStatus, "09-NOV-99", "24-JAN-08", "30-SEP-27", "BK12", "", "Firm Example", "34"]
            };
            if (d.Name == "RealEstateCorpLicense.csv")
            {
                fields[0] = "2502 Real Estate Corporation"; fields[1] = "Firm Example"; fields[3] = "CQ RE Corp."; fields[11] = "34"; fields[17] = "CQ34";
            }
            if (d.Name == "RealEstateSchoolLicense.csv")
            {
                fields[0] = "2505 Real Estate Instructor"; fields[1] = "Instructor Example"; fields[3] = "ZH RE Instructor"; fields[17] = "ZH12";
            }
            var path = Path.Combine(archive, d.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes("\"" + string.Join("\",\"", fields) + "\"\n");
            await File.WriteAllBytesAsync(path, bytes);
            entries.Add(new(d.RelativePath, 1, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), d.Url,
                new DateTimeOffset(2026, 10, 31, 15, 26, 50, TimeSpan.Zero), "synthetic archive", new() { [d.Columns.ToString()] = 1 }));
        }
        await File.WriteAllTextAsync(Path.Combine(archive, "manifest.json"), JsonSerializer.Serialize(entries));
        return archive;
    }

    public async ValueTask DisposeAsync()
    {
        await Collection.DisposeAsync();
        await Services.DisposeAsync();
        Handler.Dispose();
        Directory.Delete(Root, true);
        var parent = Path.GetDirectoryName(Root)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }

    private sealed class TestHttpFactory(BlockingBulkHandler handler, string expectedName = "FloridaBulk") : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(expectedName, name);
            return new HttpClient(handler, false);
        }
    }
    private sealed class TestLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal sealed class BlockingBulkHandler : HttpMessageHandler
{
    public int PublishedRows { get; set; } = 3;
    public string? SourceArchive { get; set; }
    public int Calls;
    public bool SimulateTransfer { get; set; }
    public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Transferred { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        Requested.TrySetResult();
        if (SourceArchive is not null)
        {
            if (request.RequestUri!.AbsoluteUri == TexasBulkImportService.RowCountUrl)
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent($"[{{\"count\":\"{PublishedRows}\"}}]")
                };
            var definition = FloridaBulkImportService.Definitions.Concat(TexasBulkImportService.Definitions).Concat(CaliforniaBulkImportService.Definitions)
                .Concat(NevadaNredBulkImportService.Definitions)
                .Single(d => d.Url == request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(SourceArchive, definition.RelativePath), cancellationToken))
            };
        }
        if (SimulateTransfer)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new PartialDownloadStream(Transferred))
            };
            response.Content.Headers.ContentLength = 1024;
            return response;
        }
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The synthetic handler must never download.");
    }

    private sealed class PartialDownloadStream(TaskCompletionSource transferred) : Stream
    {
        private bool sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1024;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sent)
            {
                await Task.Delay(1100, cancellationToken);
                buffer.Span[..128].Fill((byte)'x');
                sent = true;
                return 128;
            }
            transferred.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
