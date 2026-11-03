using Lms.Application.Services;

namespace Lms.Web.Services;

/// <summary>Reads each state's last completed collection run from its bulk collection status.</summary>
public sealed class BulkCollectionLastRunSource(IEnumerable<BulkCollectionService> collections) : IRegulatoryLastRunSource
{
    public async Task<RegulatoryLastRun?> GetAsync(string state, CancellationToken cancellationToken = default)
    {
        var collection = collections.FirstOrDefault(service =>
            string.Equals(service.State, state, StringComparison.OrdinalIgnoreCase));
        if (collection is null) return null;
        var status = await collection.GetStatusAsync(cancellationToken);
        return status.SnapshotId is { } snapshotId ? new(status.PreviousSnapshotId, snapshotId) : null;
    }
}
