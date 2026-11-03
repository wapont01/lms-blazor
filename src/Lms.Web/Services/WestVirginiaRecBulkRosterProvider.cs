using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class WestVirginiaRecBulkRosterProvider : IBulkRosterProvider
{
    public string State => WestVirginiaRecBulkImportService.State;
    public string ArchiveFolder => "west-virginia-bulk";
    public string ParserVersion => WestVirginiaRecBulkImportService.ParserVersion;
    public string Layout => "WVREC active salesperson, associate-broker and broker Open XML rosters; published company identifiers";
    public IReadOnlyList<FloridaBulkFileDefinition> Files => WestVirginiaRecBulkImportService.Definitions;
    public string? RowCountUrl => null;
    public Task<long?> GetPublishedRowsAsync(IHttpClientFactory clients, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CheckFileAsync(string path, FloridaBulkFileDefinition file, CancellationToken ct, Action<long>? progress = null) =>
        Task.FromResult(WestVirginiaRecBulkImportService.CheckFile(path, file.Name, ct, progress));
    public Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateAsync(string root, CancellationToken ct, Action<string, long, long>? progress) =>
        WestVirginiaRecBulkImportService.ValidateArchiveAsync(root, ct, progress);
    public Task<Guid> ImportAsync(IServiceProvider services, string root, CancellationToken ct, Action<string, long, long>? progress, Action<long>? staged) =>
        services.GetRequiredService<WestVirginiaRecBulkImportService>().ImportArchiveAsync(root, ct, progress, staged);
}
