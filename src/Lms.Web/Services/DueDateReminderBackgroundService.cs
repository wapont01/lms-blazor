using Lms.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Web.Services;

/// <summary>
/// Background service that periodically processes course due-date reminders.
/// Sends notifications to learners when courses are due soon or overdue.
/// </summary>
public class DueDateReminderBackgroundService : BackgroundService
{
    private readonly ILogger<DueDateReminderBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBackgroundJobMonitor _monitor;

    // Run every 6 hours
    private static readonly TimeSpan PollingInterval = TimeSpan.FromHours(6);

    public DueDateReminderBackgroundService(
        ILogger<DueDateReminderBackgroundService> logger,
        IServiceProvider serviceProvider,
        IBackgroundJobMonitor monitor)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _monitor = monitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DueDateReminderBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollingInterval, stoppingToken);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var dueDateReminderService = scope.ServiceProvider.GetRequiredService<IDueDateReminderService>();
                    var result = await dueDateReminderService.ProcessAllRemindersAsync();

                    _logger.LogInformation(
                        "Processed course reminders: DueSoonSent={DueSoonSent}, OverdueSent={OverdueSent}, CourseStartSent={CourseStartSent}, EnrollmentClosingSent={EnrollmentClosingSent}, AssessmentDeadlineSent={AssessmentDeadlineSent}, TotalProcessed={Processed}",
                        result.DueSoonSent,
                        result.OverdueSent,
                        result.CourseStartSent,
                        result.EnrollmentClosingSent,
                        result.AssessmentDeadlineSent,
                        result.TotalProcessed);

                    ((BackgroundJobMonitor)_monitor).RecordRunSuccess(result);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("DueDateReminderBackgroundService is stopping.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DueDateReminderBackgroundService while processing reminders.");
                ((BackgroundJobMonitor)_monitor).RecordRunFailure(ex.Message);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DueDateReminderBackgroundService is stopping.");
        await base.StopAsync(cancellationToken);
    }
}
