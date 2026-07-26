using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IDueDateReminderService
{
    Task<List<LearnerDueDateRow>> GetLearnerDueDatesAsync(Guid learnerUserId, bool upcomingOnly = false, int take = 10);
    Task<bool> SetDueDateAsync(Guid learnerUserId, Guid courseId, DateTime dueAtUtc, Guid actorUserId, string actorEmail);
    Task<DueDateReminderRunResult> ProcessRemindersAsync(DateTime? nowUtc = null);
    Task<CourseReminderRunResult> ProcessAllRemindersAsync(DateTime? nowUtc = null);
}

public sealed record LearnerDueDateRow(Guid CourseId, string CourseTitle, DateTime DueAtUtc, bool IsOverdue, int DaysRemaining);
public sealed record DueDateReminderRunResult(int DueSoonSent, int OverdueSent, int Processed);
public sealed record CourseReminderRunResult(int DueSoonSent, int OverdueSent, int CourseStartSent, int EnrollmentClosingSent, int AssessmentDeadlineSent, int TotalProcessed);

public class DueDateReminderService : IDueDateReminderService
{
    private static readonly TimeSpan DueSoonWindow = TimeSpan.FromDays(3);
    private static readonly TimeSpan CourseStartWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan EnrollmentClosingWindow = TimeSpan.FromDays(3);
    private static readonly TimeSpan AssessmentDeadlineWindow = TimeSpan.FromDays(1);

    private const string ReminderType_DueSoon = "due-soon";
    private const string ReminderType_Overdue = "overdue";
    private const string ReminderType_CourseStart = "course-start";
    private const string ReminderType_EnrollmentClosing = "enrollment-closing";
    private const string ReminderType_AssessmentDeadline = "assessment-deadline";

    private readonly ApplicationDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;

    public DueDateReminderService(
        ApplicationDbContext dbContext,
        INotificationService notificationService,
        IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
    }

    public async Task<List<LearnerDueDateRow>> GetLearnerDueDatesAsync(Guid learnerUserId, bool upcomingOnly = false, int take = 10)
    {
        var now = DateTime.UtcNow;
        var safeTake = Math.Clamp(take, 1, 50);

        var query = _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.Course)
            .Where(enrollment =>
                enrollment.UserAccountId == learnerUserId &&
                enrollment.DueAtUtc.HasValue &&
                !enrollment.Completed);

        if (upcomingOnly)
        {
            query = query.Where(enrollment => enrollment.DueAtUtc!.Value >= now);
        }

        var rows = await query
            .OrderBy(enrollment => enrollment.DueAtUtc)
            .Take(safeTake)
            .ToListAsync();

        return rows.Select(enrollment =>
        {
            var dueAt = enrollment.DueAtUtc!.Value;
            var daysRemaining = (int)Math.Ceiling((dueAt - now).TotalDays);
            return new LearnerDueDateRow(
                enrollment.CourseId,
                enrollment.Course?.Title ?? "Unknown Course",
                dueAt,
                dueAt < now,
                daysRemaining);
        }).ToList();
    }

    public async Task<bool> SetDueDateAsync(Guid learnerUserId, Guid courseId, DateTime dueAtUtc, Guid actorUserId, string actorEmail)
    {
        var enrollment = await _dbContext.Enrollments
            .FirstOrDefaultAsync(existing => existing.UserAccountId == learnerUserId && existing.CourseId == courseId);

        if (enrollment is null)
        {
            return false;
        }

        enrollment.DueAtUtc = DateTime.SpecifyKind(dueAtUtc, DateTimeKind.Utc);
        enrollment.DueSoonReminderSentAt = null;
        enrollment.OverdueReminderSentAt = null;

        await _dbContext.SaveChangesAsync();

        await _auditLogService.WriteAsync(
            actorUserId,
            actorEmail,
            "enrollment.due-date.updated",
            "Enrollment",
            enrollment.Id,
            $"CourseId={courseId};LearnerUserId={learnerUserId};DueAtUtc={enrollment.DueAtUtc:O}");

        return true;
    }

    public async Task<DueDateReminderRunResult> ProcessRemindersAsync(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var dueSoonBoundary = now.Add(DueSoonWindow);

        var candidates = await _dbContext.Enrollments
            .Include(enrollment => enrollment.Course)
            .Where(enrollment =>
                enrollment.DueAtUtc.HasValue &&
                !enrollment.Completed)
            .ToListAsync();

        var dueSoonSent = 0;
        var overdueSent = 0;

        foreach (var enrollment in candidates)
        {
            var dueAt = enrollment.DueAtUtc!.Value;

            if (dueAt <= now)
            {
                if (!enrollment.OverdueReminderSentAt.HasValue)
                {
                    await _notificationService.CreateAsync(
                        enrollment.UserAccountId,
                        "due-date",
                        "Course is overdue",
                        $"Your course '{enrollment.Course?.Title ?? "course"}' is overdue. Due date was {dueAt:yyyy-MM-dd}." );

                    enrollment.OverdueReminderSentAt = now;
                    overdueSent++;
                }

                continue;
            }

            if (dueAt <= dueSoonBoundary && !enrollment.DueSoonReminderSentAt.HasValue)
            {
                await _notificationService.CreateAsync(
                    enrollment.UserAccountId,
                    "due-date",
                    "Course due date approaching",
                    $"Your course '{enrollment.Course?.Title ?? "course"}' is due on {dueAt:yyyy-MM-dd}." );

                enrollment.DueSoonReminderSentAt = now;
                dueSoonSent++;
            }
        }

        await _dbContext.SaveChangesAsync();

        return new DueDateReminderRunResult(dueSoonSent, overdueSent, candidates.Count);
    }

    public async Task<CourseReminderRunResult> ProcessAllRemindersAsync(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var dueSoonBoundary = now.Add(DueSoonWindow);
        var courseStartBoundary = now.Add(CourseStartWindow);
        var enrollmentClosingBoundary = now.Add(EnrollmentClosingWindow);
        var assessmentDeadlineBoundary = now.Add(AssessmentDeadlineWindow);

        var enrollments = await _dbContext.Enrollments
            .Include(e => e.Course)
            .Include(e => e.Reminders)
            .Where(e => !e.Completed)
            .ToListAsync();

        var dueSoonSent = 0;
        var overdueSent = 0;
        var courseStartSent = 0;
        var enrollmentClosingSent = 0;
        var assessmentDeadlineSent = 0;

        foreach (var enrollment in enrollments)
        {
            // Process due-date reminders
            if (enrollment.DueAtUtc.HasValue)
            {
                var dueAt = enrollment.DueAtUtc.Value;
                if (dueAt <= now && !enrollment.OverdueReminderSentAt.HasValue)
                {
                    await CreateReminder(enrollment, ReminderType_Overdue, "Course is overdue");
                    enrollment.OverdueReminderSentAt = now;
                    overdueSent++;
                }
                else if (dueAt <= dueSoonBoundary && !enrollment.DueSoonReminderSentAt.HasValue)
                {
                    await CreateReminder(enrollment, ReminderType_DueSoon, "Course due date approaching");
                    enrollment.DueSoonReminderSentAt = now;
                    dueSoonSent++;
                }
            }

            // Process course start reminders
            if (enrollment.Course?.StartsAtUtc.HasValue == true)
            {
                var startAt = enrollment.Course.StartsAtUtc.Value;
                if (startAt > now && startAt <= courseStartBoundary && !HasReminder(enrollment, ReminderType_CourseStart))
                {
                    await CreateReminder(enrollment, ReminderType_CourseStart, "Course is starting soon");
                    courseStartSent++;
                }
            }

            // Process enrollment closing reminders
            if (enrollment.Course?.EnrollmentClosesAtUtc.HasValue == true)
            {
                var enrollmentClosesAt = enrollment.Course.EnrollmentClosesAtUtc.Value;
                if (enrollmentClosesAt > now && enrollmentClosesAt <= enrollmentClosingBoundary && !HasReminder(enrollment, ReminderType_EnrollmentClosing))
                {
                    await CreateReminder(enrollment, ReminderType_EnrollmentClosing, "Enrollment period ending soon");
                    enrollmentClosingSent++;
                }
            }

            // Process assessment deadline reminders
            var requiredAssessment = await _dbContext.CourseAssessments
                .Where(a => a.CourseId == enrollment.CourseId && a.IsRequired)
                .FirstOrDefaultAsync();

            if (requiredAssessment != null && !HasReminder(enrollment, ReminderType_AssessmentDeadline))
            {
                // Check if learner hasn't passed the assessment
                var hasPassed = await _dbContext.AssessmentAttempts
                    .Where(a => a.CourseAssessmentId == requiredAssessment.Id && 
                                a.UserAccountId == enrollment.UserAccountId &&
                                a.ScorePercent >= requiredAssessment.PassPercent)
                    .AnyAsync();

                if (!hasPassed)
                {
                    await CreateReminder(enrollment, ReminderType_AssessmentDeadline, "Assessment required for course completion");
                    assessmentDeadlineSent++;
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        return new CourseReminderRunResult(
            dueSoonSent,
            overdueSent,
            courseStartSent,
            enrollmentClosingSent,
            assessmentDeadlineSent,
            enrollments.Count);
    }

    private bool HasReminder(Enrollment enrollment, string reminderType)
    {
        return enrollment.Reminders.Any(r => r.ReminderType == reminderType);
    }

    private async Task CreateReminder(Enrollment enrollment, string reminderType, string title)
    {
        var message = reminderType switch
        {
            ReminderType_DueSoon => $"Your course '{enrollment.Course?.Title ?? "course"}' is due soon.",
            ReminderType_Overdue => $"Your course '{enrollment.Course?.Title ?? "course"}' is overdue.",
            ReminderType_CourseStart => $"Your course '{enrollment.Course?.Title ?? "course"}' is starting soon.",
            ReminderType_EnrollmentClosing => $"Enrollment for '{enrollment.Course?.Title ?? "course"}' is closing soon.",
            ReminderType_AssessmentDeadline => $"You need to complete the assessment for '{enrollment.Course?.Title ?? "course"}'.",
            _ => "Course reminder"
        };

        await _notificationService.CreateAsync(enrollment.UserAccountId, "course-reminder", title, message);

        _dbContext.CourseReminders.Add(new CourseReminder
        {
            EnrollmentId = enrollment.Id,
            ReminderType = reminderType,
            Message = message,
            SentAt = DateTime.UtcNow
        });
    }
}
