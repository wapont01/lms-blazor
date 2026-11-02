using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lms.Web.Services;

public sealed class RegulatoryCollectionOptions
{
    public string[] EnabledFloridaLicenseTypes { get; set; } = ["2501", "2505"];
    public Dictionary<string, string> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        if (EnabledFloridaLicenseTypes is null || EnabledFloridaLicenseTypes.Any(code =>
            !FloridaCollectionFilters.LicenseTypes.Any(type => type.Code == code)))
            throw new InvalidOperationException("Enabled Florida license types must use official DBPR category codes.");
        foreach (var (state, address) in Sources)
        {
            if (string.IsNullOrWhiteSpace(state) || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || !uri.IsLoopback || uri.Scheme != Uri.UriSchemeHttp
                || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Regulatory collection sources must be state names mapped to loopback HTTP root URLs.");
        }
    }
}

public sealed record RegulatoryCollectionStatus
{
    public required string State { get; init; }
    public required Guid SnapshotId { get; init; }
    public required string Scope { get; init; }
    public required string[] ExcludedGeographies { get; init; }
    public required string StageStatus { get; init; }
    public required DateTimeOffset Started { get; init; }
    public required DateTimeOffset Updated { get; init; }
    public DateTimeOffset? LastExportAt { get; init; }
    public required int PagesSaved { get; init; }
    public required int RawSearchEntries { get; init; }
    public required int DistinctEntries { get; init; }
    public required int DistinctNumberedLicenses { get; init; }
    public required int StagedLicenses { get; init; }
    public required int PartitionsComplete { get; init; }
    public required int PartitionsTotal { get; init; }
    public string? County { get; init; }
    public string? Category { get; init; }
    public int? Page { get; init; }
    public string? Error { get; init; }
    public required CollectionProfession[] Professionals { get; init; }
    public required CollectionPartition[] Partitions { get; init; }

    [JsonIgnore]
    public CollectionPartition? CurrentSearch => Partitions.FirstOrDefault(partition =>
        string.Equals(partition.County, County, StringComparison.OrdinalIgnoreCase)
        && string.Equals(partition.Category, Category, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public int? CountyTotal
    {
        get
        {
            var categories = Professionals.Select(row => row.CategoryCode).Where(code => code is not null).Distinct().Count();
            return categories > 0 && PartitionsTotal % categories == 0 ? PartitionsTotal / categories : null;
        }
    }
}

public sealed record CollectionProfession(
    string Profession, string? CategoryCode, int Licenses, int CurrentActive, int CurrentInactive,
    int OtherStatuses, Dictionary<string, int> Statuses, int CountiesComplete, string Coverage);

public sealed record CollectionPartition(
    string Category, string County, int Page, int TotalEntries, int Pages, bool Complete)
{
    // DBPR returns 50 entries per page; the collector verifies every saved page.
    [JsonIgnore]
    public long DbprEntriesSaved => Math.Min((long)Page * 50, TotalEntries);
}

public sealed record CollectedLicenseSample(
    string Name, string LicenseNumber, string Rank, string Status, string Expires, string County, string SourceUrl,
    string? Category = null, CollectionAddress[]? Addresses = null);

public sealed record CollectionAddress(string Type, string Address);

public sealed record RegulatoryCollectionData(RegulatoryCollectionStatus Status, IReadOnlyList<CollectedLicenseSample> Sample);

public interface IRegulatoryCollectionClient
{
    bool IsConfigured(string state);
    Task<RegulatoryCollectionData> GetAsync(string state, CancellationToken ct);
    Task<HttpResponseMessage> OpenExportAsync(string state, string resource, CancellationToken ct);
}

public sealed class RegulatoryCollectionClient(HttpClient http, RegulatoryCollectionOptions options) : IRegulatoryCollectionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public bool IsConfigured(string state) => options.Sources.ContainsKey(state);

    public async Task<RegulatoryCollectionData> GetAsync(string state, CancellationToken ct)
    {
        var source = Source(state);
        var status = await http.GetFromJsonAsync<RegulatoryCollectionStatus>(new Uri(source, "status.json"), JsonOptions, ct)
            ?? throw new JsonException("Collection status was empty.");
        if (status.SnapshotId == Guid.Empty || status.Updated == default || status.Started == default
            || string.IsNullOrWhiteSpace(status.State) || string.IsNullOrWhiteSpace(status.Scope) || string.IsNullOrWhiteSpace(status.StageStatus)
            || status.Professionals is null || status.Partitions is null || status.ExcludedGeographies is null
            || status.PartitionsTotal <= 0 || status.PartitionsComplete < 0 || status.PartitionsComplete > status.PartitionsTotal
            || status.PagesSaved < 0 || status.RawSearchEntries < 0 || status.DistinctEntries < 0
            || status.DistinctNumberedLicenses < 0 || status.StagedLicenses < 0
            || status.Partitions.Any(row => row is null || string.IsNullOrWhiteSpace(row.County) || string.IsNullOrWhiteSpace(row.Category)
                || row.Page < 0 || row.Pages < 0 || row.TotalEntries < 0)
            || status.Professionals.Any(row => row is null || string.IsNullOrWhiteSpace(row.Profession)
                || row.Statuses is null || row.Licenses < 0 || row.CountiesComplete < 0
                || row.CurrentActive < 0 || row.CurrentInactive < 0 || row.OtherStatuses < 0
                || row.CurrentActive + row.CurrentInactive + row.OtherStatuses != row.Licenses
                || row.Statuses.Values.Any(count => count < 0) || row.Statuses.Values.Sum() != row.Licenses)
            || status.Professionals.Sum(row => row.Licenses) != status.DistinctNumberedLicenses)
            throw new JsonException("Collection status counters or metadata are invalid.");
        var sample = await http.GetFromJsonAsync<CollectedLicenseSample[]>(new Uri(source, "sample.json"), JsonOptions, ct)
            ?? throw new JsonException("Collection sample was empty.");
        if (sample.Any(row => row is null || string.IsNullOrWhiteSpace(row.LicenseNumber) || string.IsNullOrWhiteSpace(row.Name)))
            throw new JsonException("Collection sample records are invalid.");
        return new(status, sample);
    }

    public Task<HttpResponseMessage> OpenExportAsync(string state, string resource, CancellationToken ct)
    {
        if (resource is not ("licenses.csv" or "licenses.json" or "entries.json"))
            throw new ArgumentException("Unknown collection export.", nameof(resource));
        return http.GetAsync(new Uri(Source(state), resource), HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private Uri Source(string state) => options.Sources.TryGetValue(state, out var address)
        ? new Uri(address) : throw new InvalidOperationException("No live collection source is configured for this state.");
}
