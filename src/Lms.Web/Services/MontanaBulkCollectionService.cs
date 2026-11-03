using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class MontanaBulkCollectionService(
    IServiceScopeFactory scopes, IHttpClientFactory clients, IHostApplicationLifetime lifetime,
    IWebHostEnvironment environment, IConfiguration configuration, ILogger<MontanaBulkCollectionService> logger)
    : BulkCollectionService(scopes, clients, lifetime, environment,
        new FloridaBulkCollectionOptions { WeeklyEnabled = false, ArchiveDirectory = configuration["MontanaBulk:ArchiveDirectory"] },
        logger, new MontanaRreBulkRosterProvider())
{
    public const long MaxUploadBytes = 25 * 1024 * 1024;
    private readonly string contentRoot = environment.ContentRootPath;

    public async Task ImportFileAsync(Stream source, DateTimeOffset acquiredAt, CancellationToken ct = default)
    {
        if (acquiredAt == default || acquiredAt > DateTimeOffset.UtcNow)
            throw new InvalidDataException("A valid past acquisition time is required.");
        var archiveRoot = configuration["MontanaBulk:ArchiveDirectory"] is { Length: > 0 } configured
            ? Path.GetFullPath(Path.Combine(contentRoot, configured))
            : Path.Combine(contentRoot, "App_Data", "montana-bulk");
        var root = Path.Combine(archiveRoot, "uploads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, MontanaRreBulkImportService.FileName);
        try
        {
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaxUploadBytes) throw new InvalidDataException("Montana CSV exceeds the 25 MiB limit.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            var rows = MontanaRreBulkImportService.CheckFile(path, ct);
            await using var file = File.OpenRead(path);
            var entry = new FloridaBulkManifestEntry(MontanaRreBulkImportService.FileName, rows, file.Length,
                Convert.ToHexString(await SHA256.HashDataAsync(file, ct)), MontanaRreBulkImportService.SourcePage,
                acquiredAt, "Administrator-supplied official CSV after CAPTCHA; acquisition time supplied by administrator.",
                new() { ["10"] = rows })
            {
                Parsing = "CSV with quoted fields; exact Montana headers and ten columns required."
            };
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"),
                JsonSerializer.Serialize(new[] { entry }), ct);
            await ImportArchiveAsync(root, ct);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
