using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Lms.Web.Services;

public sealed record NevadaBulkBrowseQuery(string Type = "", string NameOrLicense = "", int Page = 0);
public sealed record NevadaBulkBrowseRow(string Name, string Number, string? Type, string? Status,
    DateOnly? Expires, string? Company);
public sealed record NevadaBulkBrowseResult(Guid SnapshotId, int Total, int Page, IReadOnlyList<NevadaBulkBrowseRow> Records);

public sealed class NevadaBulkBrowseService(IServiceScopeFactory scopes)
{
    public const int PageSize = 50;

    public async Task<NevadaBulkBrowseResult?> GetAsync(NevadaBulkBrowseQuery query, CancellationToken ct = default)
    {
        if (query.Page is < 0 or > 100_000 || query.Type.Length > 40 || query.NameOrLicense.Length > 100
            || query.Type.Length > 0 && !NevadaNredBulkImportService.AllLicenseTypes.Contains(query.Type))
            throw new ArgumentException("Invalid Nevada license lookup filters.", nameof(query));
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var published = RegulatoryRoster.Published(db.RegulatoryDataSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.State == "Nevada" && snapshot.ParserVersion == NevadaNredBulkImportService.ParserVersion));
        var snapshotId = await published.Select(snapshot => (Guid?)snapshot.Id).FirstOrDefaultAsync(ct);
        if (snapshotId is null) return null;
        var rows = db.RegulatoryLicensees.AsNoTracking().Where(row => row.SnapshotId == snapshotId);
        if (query.Type.Length > 0) rows = rows.Where(row => row.LicenseType == query.Type);
        var search = query.NameOrLicense.Trim().ToUpperInvariant();
        if (search.Length > 0)
            rows = rows.Where(row => row.Name.ToUpper().Contains(search) || row.LicenseNumber.ToUpper().Contains(search));
        var total = await rows.CountAsync(ct);
        var page = Math.Min(query.Page, Math.Max(0, (total - 1) / PageSize));
        var records = await rows.OrderBy(row => row.Name).ThenBy(row => row.LicenseNumber).ThenBy(row => row.Id)
            .Skip(page * PageSize).Take(PageSize)
            .Select(row => new NevadaBulkBrowseRow(row.Name, row.LicenseNumber, row.LicenseType,
                row.LicenseStatus, row.LicenseRenewalDate, row.RelatedLicenseName)).ToListAsync(ct);
        if (await published.Select(snapshot => (Guid?)snapshot.Id).FirstOrDefaultAsync(ct) != snapshotId)
            throw new InvalidOperationException("The current Nevada roster changed during lookup. Search again.");
        return new(snapshotId.Value, total, page, records);
    }
}
