using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Web.Services;

public sealed class FloridaBulkAlertService(ApplicationDbContext db, IEmailService email,
    IAuditLogService audit, FloridaBulkCollectionOptions options, ILogger<FloridaBulkAlertService> logger,
    TimeProvider? clock = null, string state = "Florida")
{
    public async Task CheckAsync(FloridaBulkCollectionStatus status, CancellationToken ct = default,
        BulkCollectionSchedule? schedule = null)
    {
        if (!options.AlertsEnabled || !(schedule?.Enabled ?? options.WeeklyEnabled)) return;
        if (schedule is not null && schedule.State != state)
            throw new ArgumentException("The alert schedule must belong to the same state.", nameof(schedule));
        if (options.StaleAfterDays is < 1 or > 365)
            throw new InvalidOperationException($"{state}Bulk:StaleAfterDays must be between 1 and 365.");
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var nextRun = status.NextRunAtUtc ?? schedule?.NextRunAtUtc;
        var reminderKey = nextRun is { } next && next > now && next - now <= TimeSpan.FromHours(24)
            ? $"upcoming:{next.Ticks}:{schedule?.TimeZoneId ?? "UTC"}" : null;
        if (reminderKey is not null && nextRun is { } due)
            await NotifyAsync(reminderKey, $"{state} roster update scheduled",
                $"The next automatic {state} roster collection is scheduled for {LocalTime(due, schedule)} "
                + $"({due:yyyy-MM-dd HH:mm} UTC). This is a reminder within 24 hours of the scheduled update. "
                + "Review or change the schedule at /admin/regulatory-schedules. This is a collection schedule, not a confirmed regulator publication date.",
                now, ct);
        var successAt = status.LastScheduledSuccessAtUtc
            ?? (status.ScheduledRun && status.State == "Completed" ? status.CompletedAtUtc : null);
        var successSnapshot = status.LastScheduledSuccessSnapshotId
            ?? (status.ScheduledRun && status.State == "Completed" ? status.SnapshotId : null);
        if (successAt is { } completed && successSnapshot is { } snapshotId)
            await NotifyAsync($"success:{completed.Ticks}", $"{state} scheduled roster update completed",
                $"The scheduled {state} roster collection completed successfully at {LocalTime(completed, schedule)} "
                + $"({completed:yyyy-MM-dd HH:mm} UTC). Validated snapshot {snapshotId} is available. "
                + "Identical source files may reuse an existing snapshot; completion does not necessarily mean license records changed. "
                + "Review the roster and comparison at /admin/regulatory-statistics.", now, ct);
        var failureAt = status.LastScheduledFailureAtUtc
            ?? (status.ScheduledRun && status.State == "Failed" ? status.StartedAtUtc : null);
        if (failureAt is { } started)
            await NotifyAsync($"failure:{started.Ticks}", $"{state} scheduled collection failed",
                $"A scheduled {state} bulk collection started at {started:yyyy-MM-dd HH:mm} UTC failed. The current roster remains available. "
                + "Review the collection error and retry schedule at /admin/regulatory-statistics.", now, ct);
        if (status.CompletedAtUtc is { } last && now >= last.AddDays(options.StaleAfterDays))
            await NotifyAsync($"stale:{last.Ticks}", $"{state} roster needs a refresh",
                $"The last successful {state} collection was {last:yyyy-MM-dd HH:mm} UTC, over {options.StaleAfterDays} days ago. "
                + "The existing roster remains available. Review collection status at /admin/regulatory-statistics.", now, ct);
        await DeliverPendingAsync(now, reminderKey, ct);
    }

    private static string LocalTime(DateTime utc, BulkCollectionSchedule? schedule)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule?.TimeZoneId ?? "UTC");
        return $"{TimeZoneInfo.ConvertTimeFromUtc(utc, zone):yyyy-MM-dd HH:mm} ({zone.Id})";
    }

    private Guid NotificationId(string eventKey, Guid adminId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{state.ToLowerInvariant()}-bulk:{eventKey}:{adminId}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private async Task NotifyAsync(string eventKey, string title, string message, DateTime now, CancellationToken ct)
    {
        var admins = await db.UserAccounts.AsNoTracking().Where(user => user.IsActive && user.Role == "Admin").ToListAsync(ct);
        if (admins.Count == 0) logger.LogWarning("{State} bulk alert has no active administrator recipients: {Title}", state, title);
        foreach (var admin in admins)
        {
            ct.ThrowIfCancellationRequested();
            var id = NotificationId(eventKey, admin.Id);
            if (!await db.SystemNotifications.AnyAsync(notification => notification.Id == id, ct))
            {
                db.SystemNotifications.Add(new SystemNotification
                {
                    Id = id, RecipientUserId = admin.Id, Category = "regulatory-collection",
                    Title = title, Message = message, CreatedAt = now
                });
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task DeliverPendingAsync(DateTime now, string? reminderKey, CancellationToken ct)
    {
        if (!options.AlertEmailEnabled) return;
        Guid[] reminderIds = [];
        if (reminderKey is not null)
        {
            var recipientIds = await db.UserAccounts.AsNoTracking()
                .Where(user => user.IsActive && user.Role == "Admin").Select(user => user.Id).ToListAsync(ct);
            reminderIds = recipientIds.Select(id => NotificationId(reminderKey, id)).ToArray();
        }
        var pending = await db.SystemNotifications.AsNoTracking()
            .Where(notification => notification.Category == "regulatory-collection"
                && notification.RecipientUser != null && notification.RecipientUser.IsActive
                && notification.RecipientUser.Role == "Admin" && notification.Title.StartsWith(state + " ")
                && (notification.Title != state + " roster update scheduled" || reminderIds.Contains(notification.Id))
                && !db.AuditLogs.Any(log => log.TargetId == notification.Id && log.Action == "regulatory-alert.email-sent")
                && !db.AuditLogs.Any(log => log.TargetId == notification.Id
                    && log.Action == "regulatory-alert.email-attempt" && log.CreatedAt > now.AddHours(-1)))
            .Include(notification => notification.RecipientUser)
            .OrderBy(notification => notification.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var notification in pending)
        {
            ct.ThrowIfCancellationRequested();
            var id = notification.Id;
            var admin = notification.RecipientUser!;
            var deliveries = db.AuditLogs.AsNoTracking().Where(log => log.TargetId == id
                && (log.Action == "regulatory-alert.email-sent" || log.Action == "regulatory-alert.email-attempt"));
            if (await deliveries.AnyAsync(log => log.Action == "regulatory-alert.email-sent", ct)
                || await deliveries.AnyAsync(log => log.CreatedAt > now.AddHours(-1), ct)) continue;
            db.AuditLogs.Add(new AuditLog
            {
                Action = "regulatory-alert.email-attempt", TargetType = "SystemNotification",
                TargetId = id, CreatedAt = now, Details = $"Recipient={admin.Id}"
            });
            await db.SaveChangesAsync(ct);
            bool sent;
            try
            {
                sent = await email.SendAsync(new EmailMessage(admin.Email, notification.Title,
                    $"<h1>{WebUtility.HtmlEncode(notification.Title)}</h1><p>{WebUtility.HtmlEncode(notification.Message)}</p>"));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "{State} alert email transport failed for administrator {AdminId}", state, admin.Id);
                sent = false;
            }
            await audit.WriteAsync(null, null, sent ? "regulatory-alert.email-sent" : "regulatory-alert.email-failed",
                "SystemNotification", id, $"Recipient={admin.Id}");
            if (!sent) logger.LogError("{State} alert email was not delivered to administrator {AdminId}; retrying after one hour", state, admin.Id);
        }
    }
}
