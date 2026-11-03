using Lms.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Web.Services;

/// <summary>
/// Precomputes bulk roster statistics so the admin statistics page does not wait on large snapshot comparisons.
/// Runs are cheap once cached because results are keyed by snapshot IDs.
/// </summary>
public sealed class RegulatoryStatisticsWarmupService(
    IServiceScopeFactory scopeFactory,
    ILogger<RegulatoryStatisticsWarmupService> logger) : BackgroundService
{
    private static readonly string[] BulkStates = ["Florida", "Texas", "California", "Colorado", "Nevada", "New York", "Arizona", "Connecticut", "Oregon", "Delaware", "Virginia", "Illinois"];
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var state in BulkStates.Where(RegulatoryRoster.HasBulkProvider))
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<RegulatoryStatisticsService>()
                            .GetAsync(state, bulkOnly: true);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Could not precompute regulatory statistics for {State}.", state);
                    }
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
