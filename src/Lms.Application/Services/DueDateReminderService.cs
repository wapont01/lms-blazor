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
    Task<LicenseComplianceRunResult> ProcessLicenseComplianceRemindersAsync(DateTime? nowUtc = null);
}

public sealed record LearnerDueDateRow(Guid CourseId, string CourseTitle, DateTime DueAtUtc, bool IsOverdue, int DaysRemaining);
public sealed record DueDateReminderRunResult(int DueSoonSent, int OverdueSent, int Processed);
public sealed record LicenseComplianceRunResult(int Sent, int Processed);
public sealed record CourseReminderRunResult(int DueSoonSent, int OverdueSent, int CourseStartSent, int EnrollmentClosingSent, int ExamDeadlineSent, int TotalProcessed, int LicenseComplianceSent = 0);

public class DueDateReminderService : IDueDateReminderService
{
    private static readonly TimeSpan DueSoonWindow = TimeSpan.FromDays(3);

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
        var result = await ProcessRemindersAsync(nowUtc);
        var licenseResult = await ProcessLicenseComplianceRemindersAsync(nowUtc);
        return new CourseReminderRunResult(
            result.DueSoonSent,
            result.OverdueSent,
            CourseStartSent: 0,
            EnrollmentClosingSent: 0,
            ExamDeadlineSent: 0,
            result.Processed,
            licenseResult.Sent);
    }

    public async Task<LicenseComplianceRunResult> ProcessLicenseComplianceRemindersAsync(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var reminderWindow = now.AddDays(30);
        var users = await _dbContext.UserAccounts
            .Where(user => user.IsActive)
            .ToListAsync();
        var linkedLicensees = await _dbContext.UserRegulatoryLicenseLinks
            .AsNoTracking()
            .Include(link => link.RegulatoryLicensee)
            .Where(link => link.RegulatoryLicensee != null)
            .ToDictionaryAsync(link => link.UserAccountId, link => link.RegulatoryLicensee!);

        var candidates = users.SelectMany(user => BuildLicenseMilestones(user, linkedLicensees.GetValueOrDefault(user.Id), now))
            .Where(milestone => milestone.DueAtUtc <= reminderWindow)
            .ToList();

        var sent = 0;
        foreach (var milestone in candidates)
        {
            var alreadySent = await _dbContext.LicenseComplianceReminders.AnyAsync(reminder =>
                reminder.UserAccountId == milestone.UserId &&
                reminder.ReminderType == milestone.ReminderType &&
                reminder.DueAtUtc == milestone.DueAtUtc);

            if (alreadySent)
            {
                continue;
            }

            var overdue = milestone.DueAtUtc <= now;
            var recipients = await GetComplianceRecipientsAsync(milestone.UserId);
            foreach (var recipientId in recipients)
            {
                var reminderTitle = milestone.IsAuthoritative
                    ? overdue ? "License requirement overdue" : "License requirement approaching"
                    : overdue ? "Estimated license date passed" : "Estimated license date approaching";
                var reminderMessage = milestone.IsAuthoritative
                    ? overdue
                        ? $"{milestone.Description} for {milestone.SubjectDisplayName} was due on {milestone.DueAtUtc:yyyy-MM-dd}."
                        : $"{milestone.Description} for {milestone.SubjectDisplayName} is due on {milestone.DueAtUtc:yyyy-MM-dd}."
                    : overdue
                        ? $"Estimated reminder only: {milestone.Description} for {milestone.SubjectDisplayName} was estimated for {milestone.DueAtUtc:yyyy-MM-dd}; confirm the official date before treating this as overdue."
                        : $"Soft reminder only: {milestone.Description} for {milestone.SubjectDisplayName} is estimated for {milestone.DueAtUtc:yyyy-MM-dd}; confirm the official date before taking compliance action.";
                await _notificationService.CreateAsync(
                    recipientId,
                    "license-compliance",
                    reminderTitle,
                    reminderMessage);
            }

            var offerings = await GetStateAwareOfferingsAsync(milestone);
            if (offerings.Count > 0)
            {
                var courseTitles = string.Join(", ", offerings.Take(3).Select(course => course.Title));
                var additionalCourseCount = offerings.Count - 3;
                foreach (var recipientId in recipients)
                {
                    await _notificationService.CreateAsync(
                        recipientId,
                        "course-offering",
                        "State-matched courses available",
                        $"Courses matching {milestone.Description.ToLowerInvariant()} for {milestone.SubjectDisplayName} are available: {courseTitles}{(additionalCourseCount > 0 ? $" and {additionalCourseCount} more" : string.Empty)}.");
                }
            }

            _dbContext.LicenseComplianceReminders.Add(new LicenseComplianceReminder
            {
                UserAccountId = milestone.UserId,
                ReminderType = milestone.ReminderType,
                DueAtUtc = milestone.DueAtUtc,
                SentAtUtc = now
            });
            sent++;
        }

        await _dbContext.SaveChangesAsync();
        return new LicenseComplianceRunResult(sent, candidates.Count);
    }

    private async Task<List<Course>> GetStateAwareOfferingsAsync(LicenseMilestone milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone.State) || string.IsNullOrWhiteSpace(milestone.RequiredComplianceType))
        {
            return new List<Course>();
        }

        var normalizedState = milestone.State.Trim().ToUpperInvariant();
        return await _dbContext.Courses
            .AsNoTracking()
            .Where(course =>
                course.IsPublished &&
                !course.IsArchived &&
                course.ComplianceType == milestone.RequiredComplianceType &&
                (string.IsNullOrWhiteSpace(course.State) || course.State == "General" || course.State.ToUpper() == normalizedState))
            .OrderBy(course => course.Title)
            .ToListAsync();
    }

    private static IEnumerable<LicenseMilestone> BuildLicenseMilestones(UserAccount user, RegulatoryLicensee? linkedLicensee, DateTime asOfUtc)
    {
        var isNorthCarolina = LicenseEducationRules.IsNorthCarolina(user.State);
        var isFlorida = string.Equals(user.State, "Florida", StringComparison.OrdinalIgnoreCase)
            || string.Equals(user.State, "FL", StringComparison.OrdinalIgnoreCase);
        var isNorthCarolinaProvisional = string.Equals(user.LicenseType, "PB", StringComparison.OrdinalIgnoreCase)
            || (user.LicenseStatus?.Contains("provisional", StringComparison.OrdinalIgnoreCase) ?? false);
        var initialLicensureDate = user.InitialLicensureDate
            ?? linkedLicensee?.LicenseIssuedDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var renewalDueDate = user.LicenseRenewalDueDate
            ?? linkedLicensee?.LicenseRenewalDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var renewalDateSource = user.LicenseRenewalDueDate.HasValue
            ? user.LicenseRenewalDueDateSource
            : renewalDueDate.HasValue ? LicenseDateProvenance.Authoritative : LicenseDateProvenance.Unknown;
        var hasCatalogPolicy = StateLicenseRenewalPolicyCatalog.TryResolve(
            user.State, user.LicenseType ?? linkedLicensee?.LicenseLevel, out var renewalPolicy);

        if (initialLicensureDate.HasValue && user.PostlicensingCompletedAt is null && hasCatalogPolicy)
        {
            // Catalog-driven state (e.g. Georgia salesperson): enforce postlicensing only when the policy says so.
            if (renewalPolicy!.HasPostlicensingDeadline)
            {
                yield return new LicenseMilestone(
                    user.Id,
                    "postlicensing-deadline",
                    initialLicensureDate.Value.AddMonths(12),
                    $"{renewalPolicy.State} {renewalPolicy.LicenseCategory} postlicensing education requirement",
                    user.State,
                    CourseComplianceTypes.Postlicensing,
                    user.DisplayName);
            }
        }
        else if (initialLicensureDate.HasValue && user.PostlicensingCompletedAt is null &&
            ((!isNorthCarolina || isNorthCarolinaProvisional) || isFlorida))
        {
            var postlicensingDueDate = isFlorida && renewalDueDate.HasValue
                ? renewalDueDate.Value
                : initialLicensureDate.Value.AddMonths(18);
            yield return new LicenseMilestone(
                user.Id,
                "postlicensing-deadline",
                postlicensingDueDate,
                isFlorida
                    ? "Florida first-renewal postlicensing education requirement"
                    : isNorthCarolina ? "North Carolina 90-hour postlicensing education requirement" : "Your 90-hour postlicensing education requirement",
                user.State,
                CourseComplianceTypes.Postlicensing,
                user.DisplayName);
        }

        var continuingEducationDueDate = user.ContinuingEducationDueDate;
        if (!continuingEducationDueDate.HasValue && isFlorida && user.PostlicensingCompletedAt.HasValue)
        {
            continuingEducationDueDate = renewalDueDate;
        }
        if (!continuingEducationDueDate.HasValue && isNorthCarolina && user.PostlicensingCompletedAt.HasValue)
        {
            continuingEducationDueDate = LicenseEducationRules.GetNorthCarolinaCeDueDate(asOfUtc);
        }

        if (continuingEducationDueDate.HasValue)
        {
            yield return new LicenseMilestone(
                user.Id,
                "continuing-education-deadline",
                continuingEducationDueDate.Value,
                isNorthCarolina ? "North Carolina annual continuing education requirement" : "Your continuing education requirement",
                user.State,
                CourseComplianceTypes.ContinuingEducation,
                user.DisplayName);
        }
        else if (hasCatalogPolicy)
        {
            // Policy-driven CE cycle: hard only when the renewal date came from an authoritative source.
            var cycleIsAuthoritative = renewalDueDate.HasValue && LicenseDateProvenance.IsAuthoritative(renewalDateSource);
            var cycleDueDate = renewalDueDate;
            if (!cycleDueDate.HasValue && initialLicensureDate.HasValue)
            {
                var projected = initialLicensureDate.Value.AddYears(renewalPolicy!.RenewalCycleYears);
                while (projected < asOfUtc)
                {
                    projected = projected.AddYears(renewalPolicy.RenewalCycleYears);
                }

                cycleDueDate = projected;
            }

            if (cycleDueDate.HasValue)
            {
                var requiredHours = renewalPolicy!.ContinuingEducation.TotalHours;
                yield return new LicenseMilestone(
                    user.Id,
                    cycleIsAuthoritative ? "state-ce-cycle-deadline-authoritative" : "state-ce-cycle-deadline-derived",
                    cycleDueDate.Value,
                    $"{renewalPolicy.State} {renewalPolicy.LicenseCategory} continuing education requirement ({requiredHours:0.#} hours per {renewalPolicy.RenewalCycleYears}-year cycle)",
                    user.State,
                    CourseComplianceTypes.ContinuingEducation,
                    user.DisplayName,
                    cycleIsAuthoritative);
            }
        }

        if (renewalDueDate.HasValue && !string.Equals(renewalDateSource, LicenseDateProvenance.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            yield return new LicenseMilestone(
                user.Id,
                LicenseDateProvenance.IsAuthoritative(renewalDateSource)
                    ? "license-renewal-deadline-authoritative"
                    : "license-renewal-deadline-derived",
                renewalDueDate.Value,
                "Your annual broker license renewal",
                user.State,
                CourseComplianceTypes.ContinuingEducation,
                user.DisplayName,
                LicenseDateProvenance.IsAuthoritative(renewalDateSource));
        }

        if (user.IsBicEligible && user.BicUpdateCourseDueDate.HasValue)
        {
            yield return new LicenseMilestone(
                user.Id,
                "bic-update-course-deadline",
                user.BicUpdateCourseDueDate.Value,
                "Your BIC Update Course and elective CE requirement",
                user.State,
                CourseComplianceTypes.ContinuingEducation,
                user.DisplayName);
        }
        else if (!user.IsBicEligible && user.BrokerageExperienceStartDate.HasValue && user.BicCourseCompletedAt is null)
        {
            yield return new LicenseMilestone(
                user.Id,
                "bic-readiness-review",
                user.BrokerageExperienceStartDate.Value.AddYears(2),
                "Your BIC eligibility experience milestone; review the 12-hour BIC course and application",
                user.State,
                null,
                user.DisplayName);
        }
    }

    private async Task<List<Guid>> GetComplianceRecipientsAsync(Guid subjectUserId)
    {
        var recipientIds = new HashSet<Guid> { subjectUserId };
        var brokerIds = await _dbContext.BrokerLearnerAssignments
            .Where(assignment => assignment.LearnerUserId == subjectUserId)
            .Join(_dbContext.UserAccounts.Where(user => user.IsActive), assignment => assignment.BrokerUserId, user => user.Id, (_, user) => user.Id)
            .ToListAsync();
        recipientIds.UnionWith(brokerIds);

        var adminIds = await _dbContext.UserAccounts
            .Where(user => user.IsActive && user.Role == "Admin")
            .Select(user => user.Id)
            .ToListAsync();
        recipientIds.UnionWith(adminIds);
        return recipientIds.ToList();
    }

    private sealed record LicenseMilestone(
        Guid UserId,
        string ReminderType,
        DateTime DueAtUtc,
        string Description,
        string? State,
        string? RequiredComplianceType,
        string SubjectDisplayName,
        bool IsAuthoritative = true);
}
