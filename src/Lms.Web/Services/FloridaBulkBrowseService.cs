using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed record FloridaBulkBrowseQuery(
    string Category = "", string Qualification = "", string County = "", string City = "",
    string NameOrLicense = "", string Status = "", int Page = 0);

public sealed record FloridaBulkBrowseRecord(
    string Category, string Name, string LicenseNumber, string Rank, string Status,
    string Expiration, string City, string County, string AddressState);

public sealed record FloridaBulkBrowseResult(
    Guid SnapshotId, int Total, int Page, IReadOnlyList<FloridaBulkBrowseRecord> Records);

public sealed class FloridaBulkBrowseService(FloridaBulkCollectionService collection)
{
    public const int PageSize = 50;
    private static readonly string[] Files =
    [
        "REALESTATE2501LICENSE_1.csv", "RealEstateSchoolLicense.csv",
        "RealEstateCorpLicense.csv", "lic64appr.csv"
    ];

    public async Task<FloridaBulkBrowseResult?> GetAsync(FloridaBulkBrowseQuery query, CancellationToken ct)
    {
        if (query.Page < 0 || query.Page > 100_000)
            throw new ArgumentException("Bulk page must be between zero and 100,000.", nameof(query));
        var status = await collection.GetStatusAsync(ct);
        if (status.SnapshotId is not { } snapshotId) return null;
        var paths = new List<(string Name, string Path)>();
        foreach (var name in Files)
        {
            var path = await collection.GetExportPathAsync(name, ct)
                ?? throw new IOException("The completed bulk archive is missing a license file.");
            paths.Add((name, path));
        }
        if ((await collection.GetStatusAsync(ct)).SnapshotId != snapshotId)
            throw new InvalidOperationException("A newer bulk collection completed while opening the archive. Search again.");
        var records = new List<FloridaBulkBrowseRecord>(PageSize);
        var total = 0;
        var skip = (long)query.Page * PageSize;
        foreach (var (name, path) in paths)
        {
            var appraisal = name == "lic64appr.csv";
            if (!string.IsNullOrWhiteSpace(query.Category)
                && (query.Category == "appraisal") != appraisal) continue;
            using var reader = new StreamReader(path);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = FloridaBulkCsv.ReadFields(line, appraisal ? 20 : 21);
                var definition = FloridaBulkImportService.Definitions.Single(file => file.Name == name);
                var imported = FloridaBulkImportService.ParseRow(definition, fields);
                if (imported is null) continue;
                var category = appraisal ? "appraisal" : fields[0].Split(' ', 2)[0];
                if (category is "2506" or "2507") continue;
                if (!Matches(category, query.Category)) continue;
                var rank = appraisal ? fields[1] : fields[3];
                var prefix = rank.Split(' ', 2)[0];
                var number = imported.LicenseNumber;
                var record = new FloridaBulkBrowseRecord(category,
                    appraisal ? fields[2] : fields[1], number, rank,
                    string.Join(", ", new[] { fields[appraisal ? 13 : 12], fields[appraisal ? 14 : 13] }.Where(value => value.Length > 0)),
                    fields[appraisal ? 17 : 16], fields[appraisal ? 8 : 7],
                    fields[appraisal ? 11 : 10], fields[appraisal ? 9 : 8]);
                if (!Matches(prefix, query.Qualification)
                    || !Matches(record.County, query.County)
                    || !Matches(record.City, query.City)
                    || !Contains(record.Status, query.Status)
                    || !(Contains(record.Name, query.NameOrLicense) || Contains(record.LicenseNumber, query.NameOrLicense)))
                    continue;
                if (total >= skip && records.Count < PageSize) records.Add(record);
                total++;
            }
        }
        return new(snapshotId, total, query.Page, records);
    }

    private static bool Matches(string value, string filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Trim().Equals(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string value, string filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
