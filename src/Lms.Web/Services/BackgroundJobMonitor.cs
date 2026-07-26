using Lms.Application.Services;
using Microsoft.Extensions.Hosting;

namespace Lms.Web.Services;

/// <summary>
/// Tracks state and metrics for the background reminder service.
/// </summary>
public interface IBackgroundJobMonitor
{
    BackgroundJobStatus GetStatus();
    Task<BackgroundJobHealthCheckResult> HealthCheckAsync();
}

public sealed record BackgroundJobStatus(
    string ServiceName,
    bool IsRunning,
    DateTime? LastRunTime,
    DateTime? NextScheduledRunTime,
    long TotalRunsCompleted,
    long TotalRunsFailed,
    CourseReminderRunResult? LastRunResult,
    string? LastErrorMessage);

public sealed record BackgroundJobHealthCheckResult(
    bool IsHealthy,
    string Status,
    Dictionary<string, object> Metrics);

public class BackgroundJobMonitor : IBackgroundJobMonitor
{
    private DateTime? _lastRunTime;
    private DateTime? _nextScheduledRunTime;
    private long _totalRunsCompleted;
    private long _totalRunsFailed;
    private CourseReminderRunResult? _lastRunResult;
    private string? _lastErrorMessage;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromHours(6);
    private readonly ILogger<BackgroundJobMonitor> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public BackgroundJobMonitor(
        ILogger<BackgroundJobMonitor> logger,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _lifetime = lifetime;
        _nextScheduledRunTime = DateTime.UtcNow.Add(_pollingInterval);
    }

    public BackgroundJobStatus GetStatus()
    {
        return new BackgroundJobStatus(
            ServiceName: "DueDateReminderBackgroundService",
            IsRunning: !_lifetime.ApplicationStopping.IsCancellationRequested,
            LastRunTime: _lastRunTime,
            NextScheduledRunTime: _nextScheduledRunTime,
            TotalRunsCompleted: _totalRunsCompleted,
            TotalRunsFailed: _totalRunsFailed,
            LastRunResult: _lastRunResult,
            LastErrorMessage: _lastErrorMessage);
    }

    public Task<BackgroundJobHealthCheckResult> HealthCheckAsync()
    {
        var isHealthy = !_lifetime.ApplicationStopping.IsCancellationRequested;
        var minutesSinceLastRun = _lastRunTime.HasValue
            ? (int)(DateTime.UtcNow - _lastRunTime.Value).TotalMinutes
            : -1;

        var status = isHealthy
            ? "Healthy"
            : "Stopped";

        var metrics = new Dictionary<string, object>
        {
            { "ServiceName", "DueDateReminderBackgroundService" },
            { "IsRunning", isHealthy },
            { "LastRunTime", _lastRunTime?.ToString("O") ?? "Never" },
            { "NextScheduledRunTime", _nextScheduledRunTime?.ToString("O") ?? "Unknown" },
            { "MinutesSinceLastRun", minutesSinceLastRun },
            { "TotalRunsCompleted", _totalRunsCompleted },
            { "TotalRunsFailed", _totalRunsFailed },
            { "LastRunDueSoonSent", _lastRunResult?.DueSoonSent ?? 0 },
            { "LastRunOverdueSent", _lastRunResult?.OverdueSent ?? 0 },
            { "LastRunCourseStartSent", _lastRunResult?.CourseStartSent ?? 0 },
            { "LastRunEnrollmentClosingSent", _lastRunResult?.EnrollmentClosingSent ?? 0 },
            { "LastRunAssessmentDeadlineSent", _lastRunResult?.AssessmentDeadlineSent ?? 0 }
        };

        if (!string.IsNullOrEmpty(_lastErrorMessage))
        {
            metrics["LastErrorMessage"] = _lastErrorMessage;
        }

        return Task.FromResult(new BackgroundJobHealthCheckResult(isHealthy, status, metrics));
    }

    public void RecordRunSuccess(CourseReminderRunResult result)
    {
        _lastRunTime = DateTime.UtcNow;
        _nextScheduledRunTime = _lastRunTime.Value.Add(_pollingInterval);
        _totalRunsCompleted++;
        _lastRunResult = result;
        _lastErrorMessage = null;

        _logger.LogInformation(
            "Background job completed successfully at {RunTime}. Next scheduled: {NextRun}",
            _lastRunTime,
            _nextScheduledRunTime);
    }

    public void RecordRunFailure(string errorMessage)
    {
        _lastRunTime = DateTime.UtcNow;
        _nextScheduledRunTime = _lastRunTime.Value.Add(_pollingInterval);
        _totalRunsFailed++;
        _lastErrorMessage = errorMessage;

        _logger.LogError(
            "Background job failed at {RunTime}. Error: {Error}. Next scheduled: {NextRun}",
            _lastRunTime,
            errorMessage,
            _nextScheduledRunTime);
    }
}
