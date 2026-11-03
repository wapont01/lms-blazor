using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class MontanaRreBulkRosterProvider : IBulkRosterProvider
{
    public bool SupportsDownload => false;
    public string State => MontanaRreBulkImportService.State;
    public string ArchiveFolder => "montana-bulk";
    public string ParserVersion => MontanaRreBulkImportService.ParserVersion;
    public string Layout => "Manually exported Realty Regulation CSV; CAPTCHA portal; all source rows retained, course and offering approvals are evidence only";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => MontanaRreBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null)
    {
        if (!Files.Contains(file)) throw new InvalidDataException("Unknown Montana export.");
        return Task.FromResult(MontanaRreBulkImportService.CheckFile(path, ct, progress));
    }
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        MontanaRreBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct,
        Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<MontanaRreBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}
