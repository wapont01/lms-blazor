using Lms.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Web.Services;

/// <summary>
/// Background service that periodically pushes admin notifications for overdue compliance-reporting deadlines
/// (completion reports, cheating incidents, education-director orientation, instructor hire/observation, course scheduling).
/// </summary>
public class ComplianceReminderBackgroundService : BackgroundService
{
    private readonly ILogger<ComplianceReminderBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;

    private static readonly TimeSpan PollingInterval = TimeSpan.FromHours(6);

    public ComplianceReminderBackgroundService(ILogger<ComplianceReminderBackgroundService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ComplianceReminderBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollingInterval, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var complianceReportingService = scope.ServiceProvider.GetRequiredService<IComplianceReportingService>();
                var result = await complianceReportingService.ProcessComplianceRemindersAsync();

                _logger.LogInformation("Processed compliance reminders: NotificationsSent={NotificationsSent}", result.NotificationsSent);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ComplianceReminderBackgroundService is stopping.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in ComplianceReminderBackgroundService while processing reminders.");
            }
        }
    }
}
