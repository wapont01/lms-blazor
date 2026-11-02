using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed record TexasBulkBrowseQuery(string Type = "", string County = "", string NameOrLicense = "",
    string Status = "", int Page = 0);
public sealed record TexasBulkBrowseResult(Guid SnapshotId, int Total, int Page, IReadOnlyList<TexasBulkRecord> Records);

public sealed class TexasBulkBrowseService(TexasBulkCollectionService collection)
{
    public const int PageSize = 50;
    public async Task<TexasBulkBrowseResult?> GetAsync(TexasBulkBrowseQuery query, CancellationToken ct)
    {
        if (query.Page is < 0 or > 100_000 || query.Type.Length > 40 || query.County.Length > 50
            || query.NameOrLicense.Length > 100 || query.Status.Length > 40)
            throw new ArgumentException("Invalid Texas bulk search filters.", nameof(query));
        var status = await collection.GetStatusAsync(ct);
        if (status.SnapshotId is not { } id) return null;
        var path = await collection.GetExportPathAsync(TexasBulkImportService.Definitions[0].Name, ct)
            ?? throw new IOException("The completed Texas archive is missing its statewide file.");
        var records = await Task.Run(() => TexasBulkImportService.ReadRecords(path, ct), ct);
        if ((await collection.GetStatusAsync(ct)).SnapshotId != id)
            throw new InvalidOperationException("A newer Texas collection completed. Search again.");
        var matched = records.Where(row => Exact(row.Type, query.Type) && Exact(row.County, query.County)
            && Contains(row.Status, query.Status)
            && (Contains(row.Name, query.NameOrLicense) || Contains(row.Number, query.NameOrLicense))).ToList();
        var page = Math.Min(query.Page, Math.Max(0, (matched.Count - 1) / PageSize));
        return new(id, matched.Count, page, matched.Skip(page * PageSize).Take(PageSize).ToList());
    }
    private static bool Exact(string value, string filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Equals(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string value, string filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
