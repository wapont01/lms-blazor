using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Web.Services;

public sealed record CaliforniaBulkBrowseQuery(string Type = "", string NameOrLicense = "", string Status = "", int Page = 1);
public sealed record CaliforniaBulkBrowseResult(Guid SnapshotId, int Total, int Page, IReadOnlyList<RegulatoryLicensee> Records,
    IReadOnlyList<CaliforniaBrokerAssociateRelationship> AssociateRelationships);

public sealed class CaliforniaBulkBrowseService(CaliforniaBulkCollectionService collection, IServiceScopeFactory scopes)
{
    public const int PageSize = 50;
    public async Task<CaliforniaBulkBrowseResult?> GetAsync(CaliforniaBulkBrowseQuery query, CancellationToken ct)
    {
        if (query.Page is < 1 or > 100000 || query.Type.Length > 40 || query.NameOrLicense.Length > 100 || query.Status.Length > 40)
            throw new ArgumentException("Invalid California roster filters.", nameof(query));
        var status = await collection.GetStatusAsync(ct);
        if (status.SnapshotId is not { } id) return null;
        var sourcePath = await collection.GetExportPathAsync("broker_associates_list.xls", ct)
            ?? throw new InvalidOperationException("California source archive is unavailable.");
        if ((await collection.GetStatusAsync(ct)).SnapshotId != id)
            throw new InvalidOperationException("California roster changed during search. Retry.");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var records = db.RegulatoryLicensees.AsNoTracking().Where(r => r.State == "California" && r.SnapshotId == id);
        if (!string.IsNullOrWhiteSpace(query.Type)) records = records.Where(r => r.LicenseType == query.Type);
        var text = query.NameOrLicense.Trim().ToUpperInvariant();
        if (text.Length > 0) records = records.Where(r => r.Name.ToUpper().Contains(text) || r.LicenseNumber.Contains(text));
        var sourceStatus = query.Status.Trim().ToUpperInvariant();
        if (sourceStatus.Length > 0) records = records.Where(r => r.LicenseStatus != null && r.LicenseStatus.ToUpper().Contains(sourceStatus));
        var count = await records.CountAsync(ct);
        var page = Math.Clamp(query.Page, 1, Math.Max(1, (count + PageSize - 1) / PageSize));
        var result = await records.OrderBy(r => r.Name).ThenBy(r => r.LicenseNumber).ThenBy(r => r.LicenseType)
            .ThenBy(r => r.RelatedLicenseNumber).ThenBy(r => r.Id).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var relationships = CaliforniaBulkImportService.ReadAssociateRelationships(Path.GetDirectoryName(sourcePath)!,
            result.Select(r => r.LicenseNumber).ToHashSet(StringComparer.Ordinal), ct);
        return new(id, count, page, result, relationships);
    }
}
