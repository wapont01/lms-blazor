using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lms.Web.Services;

public class FloridaBulkCollectionOptions
{
    public bool WeeklyEnabled { get; set; } = true;
    public int RefreshDays { get; set; } = 7;
    public string? ArchiveDirectory { get; set; }
    public bool AlertsEnabled { get; set; } = true;
    public bool AlertEmailEnabled { get; set; } = true;
    public int StaleAfterDays { get; set; } = 8;
}

public sealed record FloridaBulkFileStatus(string Name, string State, long Rows, long Bytes)
{
    public long? ExpectedBytes { get; init; }
    public long? ExpectedRows { get; init; }
}
public sealed record FloridaBulkCollectionStatus(string State, string? Error, DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc, DateTime? NextRunAtUtc, Guid? SnapshotId, IReadOnlyList<FloridaBulkFileStatus> Files)
{
    public DateTime? FinishedAtUtc { get; init; }
    public long StagedRecords { get; init; }
    public bool ScheduledRun { get; init; }
    public DateTime? LastScheduledFailureAtUtc { get; init; }
    public DateTime? LastScheduledSuccessAtUtc { get; init; }
    public Guid? LastScheduledSuccessSnapshotId { get; init; }
    /// <summary>The roster that was current before the run that produced <see cref="SnapshotId"/>.</summary>
    public Guid? PreviousSnapshotId { get; init; }
}

public class BulkCollectionService : IAsyncDisposable
{
    private sealed record SavedStatus(FloridaBulkCollectionStatus Status, DateTime? LastSuccessUtc,
        string? LastArchive, DateTime? RetryAfterUtc);

    private readonly IServiceScopeFactory scopes;
    private readonly IHttpClientFactory clients;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger logger;
    private readonly IBulkRosterProvider provider;
    private readonly FloridaBulkCollectionOptions options;
    private readonly BulkCollectionScheduleStore? schedules;
    private readonly string root;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private Task? running;
    private CancellationTokenSource? currentRun;
    private bool cancelRequested;
    private Guid? runPreviousSnapshotId;
    private SavedStatus saved;

    public BulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
        IHostApplicationLifetime lifetime, IWebHostEnvironment environment,
        FloridaBulkCollectionOptions options, ILogger logger, IBulkRosterProvider provider,
        BulkCollectionScheduleStore? schedules = null)
    {
        this.scopes = scopes;
        this.clients = clients;
        this.lifetime = lifetime;
        this.logger = logger;
        this.options = options;
        this.provider = provider;
        this.schedules = schedules;
        if (this.options.RefreshDays is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(options), "RefreshDays must be 1–365.");
        if (this.options.StaleAfterDays is < 1 or > 365)
            throw new ArgumentOutOfRangeException(nameof(options), "StaleAfterDays must be 1-365.");
        root = Path.GetFullPath(string.IsNullOrWhiteSpace(this.options.ArchiveDirectory)
            ? Path.Combine(environment.ContentRootPath, "App_Data", provider.ArchiveFolder)
            : Path.Combine(environment.ContentRootPath, this.options.ArchiveDirectory));
        Directory.CreateDirectory(root);
        saved = new(new("Idle", null, null, null, null, null, Array.Empty<FloridaBulkFileStatus>()), null, null, null);
        var statusPath = Path.Combine(root, "status.json");
        if (File.Exists(statusPath))
        {
            try
            {
                saved = JsonSerializer.Deserialize<SavedStatus>(File.ReadAllText(statusPath))
                    ?? throw new InvalidDataException("Empty persisted bulk status.");
                if (saved.Status is null || saved.Status.Files is null
                    || saved.LastArchive is { } lastArchive && !Path.GetFullPath(lastArchive).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid persisted bulk status.");
                saved = saved with { Status = saved.Status with { Files = Array.AsReadOnly(saved.Status.Files.ToArray()) } };
                if (saved.Status.State is "Downloading" or "Validating" or "Importing" or "Running" or "Stopping")
                    saved = saved with { Status = saved.Status with { State = "Interrupted", Error = "The application stopped before this run completed.",
                        LastScheduledFailureAtUtc = saved.Status.ScheduledRun
                            ? saved.Status.StartedAtUtc : saved.Status.LastScheduledFailureAtUtc },
                        RetryAfterUtc = DateTime.UtcNow.AddHours(24) };
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
            {
                logger.LogError(ex, "Cannot read {State} bulk status", provider.State);
                saved = saved with { Status = saved.Status with { State = "Failed", Error = "Persisted collection status is unreadable; check server logs." },
                    RetryAfterUtc = DateTime.UtcNow.AddHours(24) };
            }
        }
        if (schedules is not null)
        {
            var schedule = schedules.GetOrCreate(provider.State, options.WeeklyEnabled, options.RefreshDays,
                saved.LastSuccessUtc);
            ApplyScheduleOptions(schedule);
        }
        saved = saved with { Status = WithSchedule(saved.Status) };
        Persist();
    }

    public string State => provider.State;
    public bool SupportsDownload => provider.SupportsDownload;

    public async Task StartAsync(CancellationToken cancellationToken = default, bool scheduled = false)
    {
        if (!provider.SupportsDownload)
            throw new InvalidOperationException($"{provider.State} requires an administrator-supplied export after CAPTCHA.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureAvailable();
            Begin(scheduled);
            // The request token governs accepting a run, not the lifetime of an accepted background job.
            var token = currentRun!.Token;
            running = Task.Run(() => RunAsync(null, token), CancellationToken.None);
        }
        finally { gate.Release(); }
    }

    public async Task ImportArchiveAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        await RunAndWaitAsync(Path.GetFullPath(archivePath), cancellationToken);
    }

    public Task CollectAsync(CancellationToken cancellationToken = default) => RunAndWaitAsync(null, cancellationToken);

    private async Task RunAndWaitAsync(string? archivePath, CancellationToken cancellationToken)
    {
        if (archivePath is null && !provider.SupportsDownload)
            throw new InvalidOperationException($"{provider.State} requires an administrator-supplied export after CAPTCHA.");
        Task task;
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureAvailable();
            Begin();
            var runToken = currentRun!.Token;
            running = task = Task.Run(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runToken);
                await RunAsync(archivePath, linked.Token, propagateFailure: true);
            }, CancellationToken.None);
        }
        finally { gate.Release(); }
        await task;
    }

    public async Task<FloridaBulkCollectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return WithSchedule(saved.Status); }
        finally { gate.Release(); }
    }

    public async Task<BulkCollectionSchedule> GetScheduleAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (schedules is null) throw new InvalidOperationException("Persistent bulk schedules are not configured.");
            return schedules.GetOrCreate(provider.State, options.WeeklyEnabled, options.RefreshDays, saved.LastSuccessUtc);
        }
        finally { gate.Release(); }
    }

    public async Task SaveScheduleAsync(bool enabled, int interval, string unit, string localTime,
        string timeZoneId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (schedules is null) throw new InvalidOperationException("Persistent bulk schedules are not configured.");
            var schedule = schedules.Save(provider.State, enabled, interval, unit, localTime, timeZoneId);
            ApplyScheduleOptions(schedule);
            saved = saved with { Status = WithSchedule(saved.Status) };
            Persist();
        }
        finally { gate.Release(); }
    }

    public async Task CancelCollectionAsync(CancellationToken cancellationToken = default)
    {
        Task? task;
        await gate.WaitAsync(cancellationToken);
        try
        {
            task = running;
            if (task is null || task.IsCompleted) return;
            cancelRequested = true;
            saved = saved with { Status = saved.Status with { State = "Stopping" } };
            Persist();
            currentRun!.Cancel();
        }
        finally { gate.Release(); }
        try { await task.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    public async Task<string?> GetExportPathAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (fileName != "manifest.json" && !provider.Files.Any(d => d.Name == fileName))
            throw new ArgumentException("Unknown bulk export filename.", nameof(fileName));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (saved.LastArchive is null) return null;
            var relative = fileName == "manifest.json" ? fileName
                : provider.Files.Single(d => d.Name == fileName).RelativePath;
            var path = FloridaBulkImportService.ArchiveFile(saved.LastArchive, relative);
            return File.Exists(path) ? path : null;
        }
        finally { gate.Release(); }
    }

    internal async Task<bool> IsDueAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var nextRun = WithSchedule(saved.Status).NextRunAtUtc;
            return options.WeeklyEnabled && !lifetime.ApplicationStopping.IsCancellationRequested && !stopping.IsCancellationRequested
                && (running is null || running.IsCompleted)
                && (saved.RetryAfterUtc is null || saved.RetryAfterUtc <= DateTime.UtcNow)
                && nextRun is { } dueAt && dueAt <= DateTime.UtcNow;
        }
        finally { gate.Release(); }
    }

    private void EnsureAvailable()
    {
        if (stopping.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested)
            throw new InvalidOperationException("The application is stopping.");
        if (running is { IsCompleted: false }) throw new InvalidOperationException("A bulk collection is already running.");
    }

    private void Begin(bool scheduled = false)
    {
        currentRun?.Dispose();
        currentRun = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        cancelRequested = false;
        runPreviousSnapshotId = saved.Status.SnapshotId;
        saved = saved with { Status = WithSchedule(new("Running", null, DateTime.UtcNow, saved.LastSuccessUtc, null, saved.Status.SnapshotId,
            Array.AsReadOnly(provider.Files.Select(d => new FloridaBulkFileStatus(d.Name, "Waiting", 0, 0)).ToArray()))
            {
                ScheduledRun = scheduled,
                LastScheduledFailureAtUtc = saved.Status.LastScheduledFailureAtUtc,
                LastScheduledSuccessAtUtc = saved.Status.LastScheduledSuccessAtUtc,
                LastScheduledSuccessSnapshotId = saved.Status.LastScheduledSuccessSnapshotId,
                PreviousSnapshotId = saved.Status.PreviousSnapshotId
            }) };
        Persist();
    }

    private FloridaBulkCollectionStatus WithSchedule(FloridaBulkCollectionStatus status)
    {
        var schedule = schedules?.GetOrCreate(provider.State, options.WeeklyEnabled, options.RefreshDays,
            saved.LastSuccessUtc);
        var nextRun = schedule is null
            ? saved.LastSuccessUtc?.AddDays(options.RefreshDays) ?? DateTime.UtcNow
            : schedule.NextRunAtUtc;
        if (saved.RetryAfterUtc is { } retry && (nextRun is null || retry > nextRun))
            nextRun = retry;
        return status with { NextRunAtUtc = (schedule?.Enabled ?? options.WeeklyEnabled) ? nextRun : null };
    }

    private void ApplyScheduleOptions(BulkCollectionSchedule schedule)
    {
        var days = schedule.Unit switch
        {
            "Days" => schedule.Interval,
            "Weeks" => Math.Min(schedule.Interval * 7, 365),
            "Months" => Math.Min(schedule.Interval * 30, 365),
            _ => options.RefreshDays
        };
        options.WeeklyEnabled = schedule.Enabled;
        options.RefreshDays = days;
        options.StaleAfterDays = Math.Clamp(days + 1, 1, 365);
    }

    private void Persist()
    {
        var path = Path.Combine(root, "status.json");
        var next = Path.Combine(root, "status.next.json");
        File.WriteAllText(next, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(next, path, true);
                break;
            }
            catch (Exception exception) when (attempt < 3 && exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "{State} status replacement blocked; retry {Attempt} of 3",
                    provider.State, attempt + 1);
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }

    private async Task UpdateAsync(string state, string? name = null, string? fileState = null, long rows = 0, long bytes = 0,
        long? expectedBytes = null, long? expectedRows = null, long? stagedRecords = null)
    {
        await gate.WaitAsync();
        try
        {
            saved = saved with { Status = saved.Status with { State = cancelRequested ? "Stopping" : state,
                StagedRecords = stagedRecords ?? saved.Status.StagedRecords,
                Files = name is null ? saved.Status.Files : Array.AsReadOnly(saved.Status.Files.Select(f =>
                    f.Name == name ? new FloridaBulkFileStatus(name, fileState ?? state, rows, bytes)
                        { ExpectedBytes = expectedBytes ?? f.ExpectedBytes, ExpectedRows = expectedRows ?? f.ExpectedRows } : f).ToArray()) } };
            Persist();
        }
        finally { gate.Release(); }
    }

    private async Task RunAsync(string? importPath, CancellationToken token, bool propagateFailure = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.ApplicationStopping);
        token = linked.Token;
        string? work = null;
        try
        {
            work = Path.Combine(root, DateTime.UtcNow.ToString("yyyy-MM-dd"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            IReadOnlyList<FloridaBulkManifestEntry> entries;
            if (importPath is null)
            {
                var downloaded = new List<FloridaBulkManifestEntry>();
                foreach (var definition in provider.Files)
                    downloaded.Add(await DownloadAsync(work, definition, token));
                entries = downloaded.AsReadOnly();
            }
            else
            {
                await UpdateAsync("Validating");
                entries = await provider.ValidateAsync(importPath, token,
                    (name, rows, bytes) => UpdateAsync("Validating", name, "Validating archive", rows, bytes).GetAwaiter().GetResult());
                foreach (var entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    var target = Path.Combine(work, entry.File);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var source = File.OpenRead(FloridaBulkImportService.ArchiveFile(importPath, entry.File));
                    await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    await source.CopyToAsync(destination, token);
                    await UpdateAsync("Validating", Path.GetFileName(entry.File), "Archived import (not downloaded)", entry.Rows, entry.Bytes);
                }
                // Retain the original acquisition evidence, even when the source archive also contains regional files.
                File.Copy(FloridaBulkImportService.ArchiveFile(importPath, "manifest.json"), Path.Combine(work, "original-manifest.json"));
            }
            await File.WriteAllTextAsync(Path.Combine(work, "manifest.json"), JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }), token);
            await File.WriteAllTextAsync(Path.Combine(work, "layout.json"), JsonSerializer.Serialize(new
            {
                parser = provider.ParserVersion,
                state = provider.State,
                layout = provider.Layout,
                files = provider.Files,
                sourcePublicationDate = (string?)null,
                importedArchive = importPath is not null
            }), token);
            await UpdateAsync("Validating");
            await provider.ValidateAsync(work, token,
                (name, rows, bytes) => UpdateAsync("Validating", name, "Validating", rows, bytes,
                    expectedRows: entries.Single(e => Path.GetFileName(e.File) == name).Rows).GetAwaiter().GetResult());
            await UpdateAsync("Importing");
            await using var scope = scopes.CreateAsyncScope();
            var id = await provider.ImportAsync(scope.ServiceProvider, work, token,
                (name, rows, bytes) => UpdateAsync("Validating", name, "Validating", rows, bytes,
                    expectedRows: entries.Single(e => Path.GetFileName(e.File) == name).Rows).GetAwaiter().GetResult(),
                records => UpdateAsync("Importing", stagedRecords: records).GetAwaiter().GetResult());
            var completed = DateTime.UtcNow;
            DateTime? nextRunAtUtc = options.WeeklyEnabled ? completed.AddDays(options.RefreshDays) : null;
            if (schedules is not null)
            {
                var schedule = schedules.MarkCompleted(schedules.GetOrCreate(provider.State, options.WeeklyEnabled,
                    options.RefreshDays, saved.LastSuccessUtc), completed);
                nextRunAtUtc = schedule.Enabled ? schedule.NextRunAtUtc : null;
            }
            await File.WriteAllTextAsync(Path.Combine(work, "completion.json"), JsonSerializer.Serialize(new { snapshotId = id, completedAtUtc = completed }), CancellationToken.None);
            await gate.WaitAsync();
            try
            {
                saved = new(saved.Status with { State = "Completed", Error = null, CompletedAtUtc = completed, FinishedAtUtc = completed, SnapshotId = id,
                    PreviousSnapshotId = runPreviousSnapshotId,
                    LastScheduledSuccessAtUtc = saved.Status.ScheduledRun ? completed : saved.Status.LastScheduledSuccessAtUtc,
                    LastScheduledSuccessSnapshotId = saved.Status.ScheduledRun ? id : saved.Status.LastScheduledSuccessSnapshotId,
                    NextRunAtUtc = nextRunAtUtc,
                    Files = Array.AsReadOnly(entries.Select(e => new FloridaBulkFileStatus(Path.GetFileName(e.File), importPath is null ? "Downloaded and validated" : "Imported archive (not downloaded)", e.Rows, e.Bytes)
                        { ExpectedBytes = e.Bytes, ExpectedRows = e.Rows }).ToArray()) },
                    completed, work, null);
                Persist();
            }
            finally { gate.Release(); }
        }
        catch (Exception ex)
        {
            var cancelled = ex is OperationCanceledException && token.IsCancellationRequested;
            if (cancelled) logger.LogInformation("{State} bulk collection cancelled; archive {Archive}", provider.State, work);
            else logger.LogError(ex, "{State} bulk collection failed; archive {Archive}", provider.State, work);
            var retryAfter = cancelRequested ? DateTime.UtcNow.AddDays(options.RefreshDays) : DateTime.UtcNow.AddHours(24);
            await gate.WaitAsync();
            try
            {
                saved = saved with { Status = saved.Status with { State = cancelled ? "Cancelled" : "Failed",
                    LastScheduledFailureAtUtc = !cancelled && saved.Status.ScheduledRun
                        ? saved.Status.StartedAtUtc : saved.Status.LastScheduledFailureAtUtc,
                    Error = cancelled ? "Collection stopped. No partial snapshot was published. The last completed archive is retained." : ex.Message + (options.WeeklyEnabled && !token.IsCancellationRequested
                        ? $" Automatic retry will not run before {retryAfter:yyyy-MM-dd HH:mm} UTC; downloads are limited to three attempts per file."
                        : ""), CompletedAtUtc = saved.LastSuccessUtc, FinishedAtUtc = DateTime.UtcNow }, RetryAfterUtc = retryAfter };
                Persist();
            }
            finally { gate.Release(); }
            if (propagateFailure) throw;
        }
    }

    private async Task<FloridaBulkManifestEntry> DownloadAsync(string archive, FloridaBulkFileDefinition definition, CancellationToken token)
    {
        var path = Path.Combine(archive, definition.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var attempt = 1; ; attempt++)
        {
            await UpdateAsync("Downloading", definition.Name, $"Downloading (attempt {attempt}/3)");
            try
            {
                if (File.Exists(path)) File.Delete(path);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestTimeout.CancelAfter(TimeSpan.FromMinutes(20));
                using var client = clients.CreateClient(provider.State + "Bulk");
                DateTimeOffset? sourceLastModifiedUtc = null;
                string? sourceETag = null;
                long? reportedBytes = null;
                if (provider is IBulkRosterFileDownloader downloader)
                {
                    var download = await downloader.DownloadFileAsync(client, path, definition, requestTimeout.Token,
                        (bytes, expectedBytes) => UpdateAsync("Downloading", definition.Name,
                            $"Downloading (attempt {attempt}/3)", bytes: bytes, expectedBytes: expectedBytes).GetAwaiter().GetResult());
                    reportedBytes = download.Bytes;
                    sourceLastModifiedUtc = download.SourceLastModifiedUtc;
                    sourceETag = download.SourceETag;
                }
                else
                {
                    using var response = await client.GetAsync(definition.Url, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
                    // Named client must disable redirects, so even an official response cannot redirect the archive fetch to another host.
                    if (response.RequestMessage?.RequestUri?.AbsoluteUri != definition.Url)
                        throw new InvalidDataException("Official download redirected away from the allowlisted URL.");
                    response.EnsureSuccessStatusCode();
                    var expectedBytes = response.Content.Headers.ContentLength;
                    await UpdateAsync("Downloading", definition.Name, $"Downloading (attempt {attempt}/3)", expectedBytes: expectedBytes);
                    await using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await using var source = await response.Content.ReadAsStreamAsync(requestTimeout.Token);
                    var buffer = new byte[81920];
                    long bytes = 0;
                    var lastReport = DateTime.UtcNow;
                    int read;
                    while ((read = await source.ReadAsync(buffer, requestTimeout.Token)) > 0)
                    {
                        bytes += read;
                        if (bytes > 1024L * 1024 * 1024) throw new InvalidDataException("Roster file exceeds the 1 GiB safety limit.");
                        await destination.WriteAsync(buffer.AsMemory(0, read), requestTimeout.Token);
                        if (DateTime.UtcNow - lastReport >= TimeSpan.FromSeconds(1))
                        {
                            await UpdateAsync("Downloading", definition.Name, $"Downloading (attempt {attempt}/3)",
                                bytes: bytes, expectedBytes: expectedBytes);
                            lastReport = DateTime.UtcNow;
                        }
                    }
                    if (response.Content.Headers.ContentLength is { } expected && expected != bytes)
                        throw new InvalidDataException("Incomplete official roster download.");
                    await UpdateAsync("Downloading", definition.Name, "Checking downloaded rows", bytes: bytes, expectedBytes: expectedBytes);
                    sourceLastModifiedUtc = response.Content.Headers.LastModified;
                    sourceETag = response.Headers.ETag?.ToString();
                }
                await using var file = File.OpenRead(path);
                if (reportedBytes is { } downloadedBytes && downloadedBytes != file.Length)
                    throw new InvalidDataException("Incomplete paged official roster download.");
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token)).ToLowerInvariant();
                var rows = await provider.CheckFileAsync(path, definition, token,
                    count => UpdateAsync("Downloading", definition.Name, "Checking downloaded rows", count, file.Length).GetAwaiter().GetResult());
                var publishedRows = await provider.GetPublishedRowsAsync(clients, definition, token);
                if (publishedRows is { } publishedCount && publishedCount != rows)
                    throw new InvalidDataException("Downloaded rows do not match the official statewide source count. The source may have changed; retry the collection.");
                await UpdateAsync("Downloading", definition.Name, "Downloaded", rows, file.Length);
                return new(definition.RelativePath, rows, file.Length, hash, definition.Url, DateTimeOffset.UtcNow,
                    "official HTTP download", new() { [definition.Columns.ToString()] = rows })
                    { Parsing = provider.Layout, Encoding = definition.Encoding,
                        SourceLastModifiedUtc = sourceLastModifiedUtc, SourceETag = sourceETag,
                        SourceRowCount = publishedRows, SourceRowCountUrl = provider.GetRowCountUrl(definition),
                        SourceRowCountVerifiedAtUtc = publishedRows.HasValue ? DateTimeOffset.UtcNow : null };
            }
            catch (Exception ex) when (attempt < 3 && !token.IsCancellationRequested
                && (ex is HttpRequestException { StatusCode: null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError }
                    || ex is TaskCanceledException))
            {
                await UpdateAsync("Downloading", definition.Name, $"Retry {attempt}/3: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), token);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        stopping.Cancel();
        Task? task;
        await gate.WaitAsync(cancellationToken);
        try { task = running; }
        finally { gate.Release(); }
        if (task is not null)
        {
            try { await task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogDebug(ex, "{State} bulk run had already reported a failure before shutdown", provider.State); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        currentRun?.Dispose();
        stopping.Dispose();
        gate.Dispose();
    }
}

public class BulkRefreshBackgroundService(BulkCollectionService collection,
    ILogger logger, IServiceScopeFactory? scopes = null, string state = "Florida") : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                try
                {
                    if (scopes is not null)
                    {
                        try
                        {
                            await using var scope = scopes.CreateAsyncScope();
                            var alerts = state == "Florida"
                                ? scope.ServiceProvider.GetRequiredService<FloridaBulkAlertService>()
                                : new FloridaBulkAlertService(
                                    scope.ServiceProvider.GetRequiredService<Lms.Application.Data.ApplicationDbContext>(),
                                    scope.ServiceProvider.GetRequiredService<IEmailService>(),
                                    scope.ServiceProvider.GetRequiredService<IAuditLogService>(),
                                    state switch
                                    {
                                        "Texas" => scope.ServiceProvider.GetRequiredService<TexasBulkCollectionOptions>(),
                                        "California" => scope.ServiceProvider.GetRequiredService<CaliforniaBulkCollectionOptions>(),
                                        "Colorado" => scope.ServiceProvider.GetRequiredService<ColoradoDoraBulkCollectionOptions>(),
                                        "Nevada" => scope.ServiceProvider.GetRequiredService<NevadaBulkCollectionOptions>(),
                                        "New York" => scope.ServiceProvider.GetRequiredService<NewYorkBulkCollectionOptions>(),
                                        "Arizona" => scope.ServiceProvider.GetRequiredService<ArizonaBulkCollectionOptions>(),
                                        "Connecticut" => scope.ServiceProvider.GetRequiredService<ConnecticutBulkCollectionOptions>(),
                                        "Oregon" => scope.ServiceProvider.GetRequiredService<OregonBulkCollectionOptions>(),
                                        "Delaware" => scope.ServiceProvider.GetRequiredService<DelawareBulkCollectionOptions>(),
                                        "Illinois" => scope.ServiceProvider.GetRequiredService<IllinoisBulkCollectionOptions>(),
                                        "Virginia" => scope.ServiceProvider.GetRequiredService<VirginiaBulkCollectionOptions>(),
                                        "Michigan" => scope.ServiceProvider.GetRequiredService<MichiganBulkCollectionOptions>(),
                                        "West Virginia" => scope.ServiceProvider.GetRequiredService<WestVirginiaBulkCollectionOptions>(),
                                        "Maine" => scope.ServiceProvider.GetRequiredService<MaineBulkCollectionOptions>(),
                                        "Vermont" => scope.ServiceProvider.GetRequiredService<VermontBulkCollectionOptions>(),
                                        _ => throw new InvalidOperationException($"Unknown bulk collection alert state '{state}'.")
                                    },
                                    scope.ServiceProvider.GetRequiredService<ILogger<FloridaBulkAlertService>>(), state: state);
                            var schedule = await collection.GetScheduleAsync(stoppingToken);
                            await alerts.CheckAsync(await collection.GetStatusAsync(stoppingToken), stoppingToken, schedule);
                        }

                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception exception) { logger.LogError(exception, "{State} collection alert check failed; retrying in one minute", state); }
                    }
                    if (await collection.IsDueAsync(stoppingToken))
                    {
                        try { await collection.StartAsync(stoppingToken, scheduled: true); }
                        catch (InvalidOperationException) { } // An administrator may have started the same job between checks.
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError(ex, "{State} weekly scheduler could not accept a run; checking again in one minute", state); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "{State} weekly scheduler stopped unexpectedly", state); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await collection.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}

public sealed class FloridaBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
        IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<FloridaBulkCollectionOptions> options,
        ILogger<FloridaBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
        : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new FloridaBulkRosterProvider(), schedules);

    public sealed class TexasBulkCollectionOptions : FloridaBulkCollectionOptions;

    public sealed class TexasBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
        IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<TexasBulkCollectionOptions> options,
        ILogger<TexasBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
        : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new TexasBulkRosterProvider(), schedules);

    public sealed class FloridaBulkRefreshBackgroundService(FloridaBulkCollectionService collection,
        ILogger<FloridaBulkRefreshBackgroundService> logger, IServiceScopeFactory? scopes = null)
        : BulkRefreshBackgroundService(collection, logger, scopes);

public sealed class TexasBulkRefreshBackgroundService(TexasBulkCollectionService collection,
    ILogger<TexasBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Texas");

public sealed class CaliforniaBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class CaliforniaBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<CaliforniaBulkCollectionOptions> options,
    ILogger<CaliforniaBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new CaliforniaBulkRosterProvider(), schedules);

public sealed class CaliforniaBulkRefreshBackgroundService(CaliforniaBulkCollectionService collection,
    ILogger<CaliforniaBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "California");

public sealed class ColoradoDoraBulkCollectionOptions : FloridaBulkCollectionOptions
{
    // Downloads and imports the full DORA dataset when no Colorado baseline exists; tests turn this off.
    public bool BaselineBootstrapEnabled { get; set; } = true;
}

public sealed class NevadaBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class NevadaBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<NevadaBulkCollectionOptions> options,
    ILogger<NevadaBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new NevadaBulkRosterProvider(), schedules);

public sealed class NevadaBulkRefreshBackgroundService(NevadaBulkCollectionService collection,
    ILogger<NevadaBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Nevada");

public sealed class NewYorkBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class NewYorkBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<NewYorkBulkCollectionOptions> options,
    ILogger<NewYorkBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new NewYorkDosBulkRosterProvider(), schedules);

public sealed class NewYorkBulkRefreshBackgroundService(NewYorkBulkCollectionService collection,
    ILogger<NewYorkBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "New York");

public sealed class ArizonaBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class ArizonaBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<ArizonaBulkCollectionOptions> options,
    ILogger<ArizonaBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new ArizonaAdreBulkRosterProvider(), schedules);

public sealed class ArizonaBulkRefreshBackgroundService(ArizonaBulkCollectionService collection,
    ILogger<ArizonaBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Arizona");

public sealed class ConnecticutBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class ConnecticutBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<ConnecticutBulkCollectionOptions> options,
    ILogger<ConnecticutBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new ConnecticutDcpBulkRosterProvider(), schedules);

public sealed class ConnecticutBulkRefreshBackgroundService(ConnecticutBulkCollectionService collection,
    ILogger<ConnecticutBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Connecticut");

public sealed class OregonBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class OregonBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<OregonBulkCollectionOptions> options,
    ILogger<OregonBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new OregonOreaBulkRosterProvider(), schedules);

public sealed class OregonBulkRefreshBackgroundService(OregonBulkCollectionService collection,
    ILogger<OregonBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Oregon");

public sealed class DelawareBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class DelawareBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<DelawareBulkCollectionOptions> options,
    ILogger<DelawareBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new DelawareDprBulkRosterProvider(), schedules);

public sealed class DelawareBulkRefreshBackgroundService(DelawareBulkCollectionService collection,
    ILogger<DelawareBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Delaware");

public sealed class IllinoisBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class MichiganBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class MaineBulkCollectionOptions : FloridaBulkCollectionOptions;
public sealed class VermontBulkCollectionOptions : FloridaBulkCollectionOptions;
public sealed class VermontBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<VermontBulkCollectionOptions> options,
    ILogger<VermontBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new VermontOprBulkRosterProvider(), schedules);
public sealed class VermontBulkRefreshBackgroundService(VermontBulkCollectionService collection,
    ILogger<VermontBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Vermont");
public sealed class MaineBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<MaineBulkCollectionOptions> options,
    ILogger<MaineBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new MaineAlmsBulkRosterProvider(), schedules);
public sealed class MaineBulkRefreshBackgroundService(MaineBulkCollectionService collection,
    ILogger<MaineBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Maine");
public sealed class MichiganBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<MichiganBulkCollectionOptions> options,
    ILogger<MichiganBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new MichiganLaraBulkRosterProvider(), schedules);
public sealed class MichiganBulkRefreshBackgroundService(MichiganBulkCollectionService collection,
    ILogger<MichiganBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Michigan");

public sealed class WestVirginiaBulkCollectionOptions : FloridaBulkCollectionOptions;
public sealed class WestVirginiaBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<WestVirginiaBulkCollectionOptions> options,
    ILogger<WestVirginiaBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new WestVirginiaRecBulkRosterProvider(), schedules);
public sealed class WestVirginiaBulkRefreshBackgroundService(WestVirginiaBulkCollectionService collection,
    ILogger<WestVirginiaBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "West Virginia");

public sealed class IllinoisBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<IllinoisBulkCollectionOptions> options,
    ILogger<IllinoisBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new IllinoisIdfprBulkRosterProvider(), schedules);

public sealed class IllinoisBulkRefreshBackgroundService(IllinoisBulkCollectionService collection,
    ILogger<IllinoisBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Illinois");

public sealed class VirginiaBulkCollectionOptions : FloridaBulkCollectionOptions;

public sealed class VirginiaBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<VirginiaBulkCollectionOptions> options,
    ILogger<VirginiaBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new VirginiaDporBulkRosterProvider(), schedules);

public sealed class VirginiaBulkRefreshBackgroundService(VirginiaBulkCollectionService collection,
    ILogger<VirginiaBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Virginia");

public sealed class ColoradoDoraBulkCollectionService(IServiceScopeFactory scopes, IHttpClientFactory clients,
    IHostApplicationLifetime lifetime, IWebHostEnvironment environment, IOptions<ColoradoDoraBulkCollectionOptions> options,
    ILogger<ColoradoDoraBulkCollectionService> logger, BulkCollectionScheduleStore? schedules = null)
    : BulkCollectionService(scopes, clients, lifetime, environment, options.Value, logger, new ColoradoDoraBulkRosterProvider(), schedules);

public sealed class ColoradoDoraBulkRefreshBackgroundService(ColoradoDoraBulkCollectionService collection,
    ILogger<ColoradoDoraBulkRefreshBackgroundService> logger, IServiceScopeFactory scopes)
    : BulkRefreshBackgroundService(collection, logger, scopes, "Colorado");

public sealed class ColoradoDoraBaselineBootstrapService(ColoradoDoraBulkCollectionService collection,
    IServiceScopeFactory scopes, IOptions<ColoradoDoraBulkCollectionOptions> options,
    ILogger<ColoradoDoraBaselineBootstrapService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BaselineBootstrapEnabled) return;
        await Task.Yield();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Lms.Application.Data.ApplicationDbContext>();
            var hasBaseline = await db.RegulatoryDataSnapshots.AnyAsync(snapshot =>
                snapshot.State == "Colorado" && snapshot.ParserVersion == ColoradoDoraBulkImportService.ParserVersion
                && snapshot.Status == Lms.Domain.Entities.RegulatorySnapshotStatuses.Approved, stoppingToken);
            if (hasBaseline) return;

            var schedule = await collection.GetScheduleAsync(stoppingToken);
            await collection.SaveScheduleAsync(schedule.Enabled, schedule.Interval, schedule.Unit,
                schedule.LocalTime, schedule.TimeZoneId, stoppingToken);
            logger.LogInformation("Collecting the initial Colorado DORA baseline.");
            await collection.CollectAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Initial Colorado DORA baseline collection failed; admin collection status contains the error.");
        }
    }
}
