using Lms.Domain.Entities;

namespace Lms.Application.Services;

public static class RegulatoryRoster
{
    public static bool HasBulkProvider(string state) => state is "Florida" or "Texas" or "California" or "Colorado" or "Nevada" or "New York" or "Arizona" or "Connecticut" or "Oregon" or "Delaware" or "Virginia" or "Illinois" or "Michigan" or "West Virginia" or "Maine" or "Vermont" or "Montana" or "Idaho" or "Kentucky" or "Wyoming" or "Rhode Island";
    public static string BulkParser(string state) => state switch
    {
        "Florida" => FloridaBulkImportService.ParserVersion,
        "Texas" => TexasBulkImportService.ParserVersion,
        "California" => CaliforniaBulkImportService.ParserVersion,
        "Colorado" => ColoradoDoraBulkImportService.ParserVersion,
        "Nevada" => NevadaNredBulkImportService.ParserVersion,
        "New York" => NewYorkDosBulkImportService.ParserVersion,
        "Arizona" => ArizonaAdreBulkImportService.ParserVersion,
        "Connecticut" => ConnecticutDcpBulkImportService.ParserVersion,
        "Oregon" => OregonOreaBulkImportService.ParserVersion,
        "Delaware" => DelawareDprBulkImportService.ParserVersion,
        "Illinois" => IllinoisIdfprBulkImportService.ParserVersion,
        "Virginia" => VirginiaDporBulkImportService.ParserVersion,
        "Michigan" => MichiganLaraBulkImportService.ParserVersion,
        "Maine" => MaineAlmsBulkImportService.ParserVersion,
        "Vermont" => VermontOprBulkImportService.ParserVersion,
        "Montana" => MontanaRreBulkImportService.ParserVersion,
        "Idaho" => IdahoDoplBulkImportService.ParserVersion,
        "Kentucky" => KentuckyOopBulkImportService.ParserVersion,
        "Wyoming" => WyomingWrecBulkImportService.ParserVersion,
        "Rhode Island" => RhodeIslandDbrBulkImportService.ParserVersion,
        "West Virginia" => WestVirginiaRecBulkImportService.ParserVersion,
        _ => throw new InvalidOperationException("No validated bulk provider is configured for this state.")
    };
    public static IOrderedQueryable<RegulatoryDataSnapshot> Published(IQueryable<RegulatoryDataSnapshot> snapshots) =>
        snapshots.Where(snapshot => snapshot.Status == RegulatorySnapshotStatuses.Approved)
            .OrderByDescending(snapshot => snapshot.State == "Florida" || snapshot.State == "Texas" || snapshot.State == "California" || snapshot.State == "Colorado" ||             snapshot.State == "Nevada" || snapshot.State == "New York" || snapshot.State == "Arizona" || snapshot.State == "Connecticut" || snapshot.State == "Oregon" || snapshot.State == "Delaware" || snapshot.State == "Virginia" || snapshot.State == "Illinois" || snapshot.State == "Michigan" || snapshot.State == "West Virginia" || snapshot.State == "Maine" || snapshot.State == "Vermont" || snapshot.State == "Montana" || snapshot.State == "Idaho" || snapshot.State == "Kentucky" || snapshot.State == "Wyoming" || snapshot.State == "Rhode Island" ? snapshot.ActivatedAtUtc ?? snapshot.RetrievedAtUtc : snapshot.RetrievedAtUtc)
            .ThenByDescending(snapshot => snapshot.ImportedAtUtc).ThenBy(snapshot => snapshot.Id);
}
