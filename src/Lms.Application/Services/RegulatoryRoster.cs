using Lms.Domain.Entities;

namespace Lms.Application.Services;

public static class RegulatoryRoster
{
    public static bool HasBulkProvider(string state) => state is "Florida" or "Texas" or "California" or "Colorado" or "Nevada" or "New York";
    public static string BulkParser(string state) => state switch
    {
        "Florida" => FloridaBulkImportService.ParserVersion,
        "Texas" => TexasBulkImportService.ParserVersion,
        "California" => CaliforniaBulkImportService.ParserVersion,
        "Colorado" => ColoradoDoraBulkImportService.ParserVersion,
        "Nevada" => NevadaNredBulkImportService.ParserVersion,
        "New York" => NewYorkDosBulkImportService.ParserVersion,
        _ => throw new InvalidOperationException("No validated bulk provider is configured for this state.")
    };
    public static IOrderedQueryable<RegulatoryDataSnapshot> Published(IQueryable<RegulatoryDataSnapshot> snapshots) =>
        snapshots.Where(snapshot => snapshot.Status == RegulatorySnapshotStatuses.Approved)
            .OrderByDescending(snapshot => snapshot.State == "Florida" || snapshot.State == "Texas" || snapshot.State == "California" || snapshot.State == "Colorado" || snapshot.State == "Nevada" || snapshot.State == "New York"
                ? snapshot.ActivatedAtUtc ?? snapshot.RetrievedAtUtc : snapshot.RetrievedAtUtc)
            .ThenByDescending(snapshot => snapshot.ImportedAtUtc).ThenBy(snapshot => snapshot.Id);
}
