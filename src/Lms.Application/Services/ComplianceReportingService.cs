using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IComplianceReportingService
{
    Task<List<CourseCompletionReportRow>> GetPendingCourseCompletionReportsAsync();
    Task<bool> MarkCourseCompletionReportSubmittedAsync(Guid enrollmentId, Guid actorUserId, string actorEmail);
    Task<List<MonthlySummaryReportRow>> GetMonthlySummaryReportAsync(int year, int month);
    Task<List<CheatingIncidentReportRow>> GetPendingCheatingIncidentReportsAsync();
    Task<bool> MarkCheatingIncidentReportedAsync(Guid proctoringSessionId, Guid actorUserId, string actorEmail);
    Task<EducationDirectorOrientationStatus> GetEducationDirectorOrientationStatusAsync();
    Task MarkEducationDirectorOrientationViewedAsync(Guid actorUserId, string actorEmail);
    Task<List<InstructorComplianceRow>> GetInstructorComplianceStatusAsync();
    Task<bool> MarkInstructorHireReportedAsync(Guid staffMemberId, Guid actorUserId, string actorEmail);
    Task<bool> RecordInstructorObservationAsync(Guid staffMemberId, Guid actorUserId, string actorEmail);
    Task<List<RetentionCategorySummary>> GetRecordRetentionSummaryAsync();
    Task<CourseSessionScheduleResult> ScheduleCourseSessionAsync(Guid courseId, DateTime scheduledStartUtc, DateTime scheduledEndUtc, Guid actorUserId, string actorEmail);
    Task<List<ScheduledCourseSessionRow>> GetScheduledCourseSessionsAsync();
    Task<List<PendingScheduleNotificationRow>> GetPendingScheduleNotificationsAsync();
    Task<bool> MarkScheduleNotificationSentAsync(Guid sessionId, Guid actorUserId, string actorEmail);
    Task<bool> RescheduleCourseSessionAsync(Guid sessionId, DateTime newStartUtc, DateTime newEndUtc, Guid actorUserId, string actorEmail);
    Task<bool> CancelCourseSessionAsync(Guid sessionId, Guid actorUserId, string actorEmail);
    Task<List<PendingScheduleChangeReportRow>> GetPendingScheduleChangeReportsAsync();
    Task<bool> MarkScheduleChangeReportedAsync(Guid sessionId, Guid actorUserId, string actorEmail);
    Task<ComplianceReminderRunResult> ProcessComplianceRemindersAsync();
}

public sealed record CourseCompletionReportRow(
    Guid EnrollmentId,
    string StudentLegalName,
    string StudentEmail,
    string? StudentPhone,
    Guid StudentUniqueId,
    string? BrokerLicenseNumber,
    DateTime CompletedAtUtc,
    DateTime DueAtUtc,
    bool IsOverdue,
    string ProviderName,
    string? ProviderLicenseNumber,
    string CourseTitle,
    string? CommissionCourseNumber,
    string? InstructorName,
    string? InstructorLicenseNumber);

public sealed record EducationDirectorOrientationStatus(
    string EducationDirectorName,
    DateTime? DesignatedAtUtc,
    DateTime? ViewedAtUtc,
    DateTime? DueAtUtc,
    bool IsOverdue);

public sealed record InstructorComplianceRow(
    Guid StaffMemberId,
    string Name,
    DateTime AddedAtUtc,
    DateTime HireReportDueAtUtc,
    DateTime? HireReportedAtUtc,
    bool IsHireReportOverdue,
    DateTime? LastObservationAtUtc,
    DateTime NextObservationDueAtUtc,
    bool IsObservationOverdue);

public sealed record MonthlySummaryReportRow(
    Guid CourseId,
    string CourseTitle,
    string? InstructorName,
    int EnrollmentCount,
    int CompletionCount,
    int ExamFailCount);

public sealed record CheatingIncidentReportRow(
    Guid ProctoringSessionId,
    string StudentName,
    string StudentEmail,
    string CourseTitle,
    string? CommissionCourseNumber,
    string ProctorName,
    string? Notes,
    DateTime IncidentDetectedAtUtc,
    DateTime DueAtUtc,
    bool IsOverdue);

public sealed record RetentionCategorySummary(
    string CategoryName,
    int RecordCount,
    DateTime? OldestRecordAtUtc,
    int RetentionYears,
    bool HasRecordsEligibleForArchivalReview);

public sealed record CourseSessionScheduleResult(Guid SessionId, List<string> Warnings);

public sealed record ScheduledCourseSessionRow(
    Guid SessionId,
    string CourseTitle,
    DateTime ScheduledStartAtUtc,
    DateTime ScheduledEndAtUtc,
    bool CommissionNotified);

public sealed record PendingScheduleNotificationRow(
    Guid SessionId,
    string CourseTitle,
    DateTime ScheduledStartAtUtc,
    DateTime DueAtUtc,
    bool IsOverdue);

public sealed record PendingScheduleChangeReportRow(
    Guid SessionId,
    string CourseTitle,
    DateTime ChangedAtUtc,
    DateTime DueAtUtc,
    bool IsOverdue);

public sealed record ComplianceReminderRunResult(int NotificationsSent);

public sealed class ComplianceReportingService : IComplianceReportingService
{
    private const int CompletionReportDueDays = 7;
    private const int CheatingIncidentReportDueDays = 10;
    private const int OrientationVideoDueDays = 30;
    private const int HireReportDueDays = 10;
    private const int ObservationIntervalDays = 365;
    private const int RecordRetentionYears = 3;
    private const int ScheduleNoticeDueDays = 10;
    private const int ScheduleChangeWindowDays = 5;
    private const int ScheduleChangeReportDueHours = 24;
    private const int MaxDailySessionHours = 8;

    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly ISchoolProfileService _schoolProfileService;
    private readonly INotificationService? _notificationService;

    public ComplianceReportingService(ApplicationDbContext dbContext, IAuditLogService auditLogService, ISchoolProfileService schoolProfileService, INotificationService? notificationService = null)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _schoolProfileService = schoolProfileService;
        _notificationService = notificationService;
    }

    public async Task<List<CourseCompletionReportRow>> GetPendingCourseCompletionReportsAsync()
    {
        var profile = await _schoolProfileService.GetAsync();
        var nowUtc = DateTime.UtcNow;

        var pending = await _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.UserAccount)
            .Include(enrollment => enrollment.Course)
            .Where(enrollment => enrollment.Completed
                && enrollment.CompletedAtUtc != null
                && enrollment.CompletionReportSubmittedAtUtc == null)
            .OrderBy(enrollment => enrollment.CompletedAtUtc)
            .ToListAsync();

        var instructorIds = pending
            .Where(enrollment => enrollment.Course?.OwnerInstructorId != null)
            .Select(enrollment => enrollment.Course!.OwnerInstructorId!.Value)
            .Distinct()
            .ToList();

        var instructors = instructorIds.Count == 0
            ? new Dictionary<Guid, (string? Name, string? LicenseNumber)>()
            : await _dbContext.UserAccounts
                .AsNoTracking()
                .Where(user => instructorIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => (Name: (string?)(user.LegalName ?? user.DisplayName), user.LicenseNumber));

        var rows = new List<CourseCompletionReportRow>(pending.Count);
        foreach (var enrollment in pending)
        {
            var completedAtUtc = enrollment.CompletedAtUtc!.Value;
            var dueAtUtc = completedAtUtc.AddDays(CompletionReportDueDays);
            var instructorInfo = enrollment.Course?.OwnerInstructorId is Guid instructorId && instructors.TryGetValue(instructorId, out var found)
                ? found
                : (Name: profile.PrimaryInstructorName, LicenseNumber: profile.InstructorLicenseNumber);

            rows.Add(new CourseCompletionReportRow(
                enrollment.Id,
                enrollment.UserAccount?.LegalName ?? enrollment.UserAccount?.DisplayName ?? "Unknown Student",
                enrollment.UserAccount?.Email ?? "unknown@lms.local",
                enrollment.UserAccount?.PhoneNumber,
                enrollment.UserAccountId,
                enrollment.UserAccount?.LicenseNumber,
                completedAtUtc,
                dueAtUtc,
                dueAtUtc < nowUtc,
                profile.LegalName,
                profile.ProviderLicenseNumber,
                enrollment.Course?.Title ?? "Unknown Course",
                enrollment.Course?.CommissionCourseNumber,
                instructorInfo.Name,
                instructorInfo.LicenseNumber));
        }

        return rows;
    }

    public async Task<bool> MarkCourseCompletionReportSubmittedAsync(Guid enrollmentId, Guid actorUserId, string actorEmail)
    {
        var enrollment = await _dbContext.Enrollments.FirstOrDefaultAsync(existing => existing.Id == enrollmentId);
        if (enrollment is null || enrollment.CompletionReportSubmittedAtUtc is not null)
        {
            return false;
        }

        enrollment.CompletionReportSubmittedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.completion-report.submitted", "Enrollment", enrollment.Id, $"CourseId={enrollment.CourseId}");
        return true;
    }

    public async Task<List<MonthlySummaryReportRow>> GetMonthlySummaryReportAsync(int year, int month)
    {
        var periodStartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndUtc = periodStartUtc.AddMonths(1);

        var enrollmentsInPeriod = await _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.Course)
            .Where(enrollment => enrollment.EnrolledAt >= periodStartUtc && enrollment.EnrolledAt < periodEndUtc)
            .ToListAsync();

        var completionsInPeriod = await _dbContext.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.CompletedAtUtc != null
                && enrollment.CompletedAtUtc >= periodStartUtc
                && enrollment.CompletedAtUtc < periodEndUtc)
            .GroupBy(enrollment => enrollment.CourseId)
            .Select(group => new { CourseId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.CourseId, item => item.Count);

        var examFailsInPeriod = await _dbContext.ExamAttempts
            .AsNoTracking()
            .Include(attempt => attempt.CourseExam)
            .Where(attempt => !attempt.Passed
                && attempt.SubmittedAt >= periodStartUtc
                && attempt.SubmittedAt < periodEndUtc)
            .GroupBy(attempt => attempt.CourseExam!.CourseId)
            .Select(group => new { CourseId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.CourseId, item => item.Count);

        var courseIds = enrollmentsInPeriod
            .Where(enrollment => enrollment.Course?.OwnerInstructorId != null)
            .Select(enrollment => enrollment.Course!.OwnerInstructorId!.Value)
            .Distinct()
            .ToList();

        var instructorNames = courseIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.UserAccounts
                .AsNoTracking()
                .Where(user => courseIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.LegalName ?? user.DisplayName);

        return enrollmentsInPeriod
            .GroupBy(enrollment => new { enrollment.CourseId, Title = enrollment.Course?.Title ?? "Unknown Course", InstructorId = enrollment.Course?.OwnerInstructorId })
            .Select(group => new MonthlySummaryReportRow(
                group.Key.CourseId,
                group.Key.Title,
                group.Key.InstructorId is Guid instructorId && instructorNames.TryGetValue(instructorId, out var name) ? name : null,
                group.Count(),
                completionsInPeriod.TryGetValue(group.Key.CourseId, out var completionCount) ? completionCount : 0,
                examFailsInPeriod.TryGetValue(group.Key.CourseId, out var failCount) ? failCount : 0))
            .OrderBy(row => row.CourseTitle)
            .ToList();
    }

    public async Task<List<CheatingIncidentReportRow>> GetPendingCheatingIncidentReportsAsync()
    {
        var nowUtc = DateTime.UtcNow;

        var sessions = await _dbContext.ExamProctoringSessions
            .AsNoTracking()
            .Include(session => session.UserAccount)
            .Include(session => session.Course)
            .Where(session => session.SecurityIncidentReported && session.CommissionReportedAtUtc == null)
            .OrderBy(session => session.ExpiresAtUtc)
            .ToListAsync();

        return sessions
            .Select(session =>
            {
                var incidentDetectedAtUtc = session.ExpiresAtUtc;
                var dueAtUtc = incidentDetectedAtUtc.AddDays(CheatingIncidentReportDueDays);
                return new CheatingIncidentReportRow(
                    session.Id,
                    session.UserAccount?.LegalName ?? session.UserAccount?.DisplayName ?? "Unknown Student",
                    session.UserAccount?.Email ?? "unknown@lms.local",
                    session.Course?.Title ?? "Unknown Course",
                    session.Course?.CommissionCourseNumber,
                    session.ProctorName,
                    session.SecurityIncidentNotes,
                    incidentDetectedAtUtc,
                    dueAtUtc,
                    dueAtUtc < nowUtc);
            })
            .ToList();
    }

    public async Task<bool> MarkCheatingIncidentReportedAsync(Guid proctoringSessionId, Guid actorUserId, string actorEmail)
    {
        var session = await _dbContext.ExamProctoringSessions.FirstOrDefaultAsync(existing => existing.Id == proctoringSessionId);
        if (session is null || !session.SecurityIncidentReported || session.CommissionReportedAtUtc is not null)
        {
            return false;
        }

        session.CommissionReportedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.cheating-incident.reported", "ExamProctoringSession", session.Id, $"CourseId={session.CourseId};Learner={session.UserAccountId}");
        return true;
    }

    public async Task<EducationDirectorOrientationStatus> GetEducationDirectorOrientationStatusAsync()
    {
        var profile = await _schoolProfileService.GetAsync();
        var dueAtUtc = profile.EducationDirectorDesignatedAtUtc?.AddDays(OrientationVideoDueDays);

        return new EducationDirectorOrientationStatus(
            profile.EducationDirectorName,
            profile.EducationDirectorDesignatedAtUtc,
            profile.EducationDirectorOrientationVideoViewedAtUtc,
            dueAtUtc,
            profile.EducationDirectorOrientationVideoViewedAtUtc is null && dueAtUtc is not null && dueAtUtc < DateTime.UtcNow);
    }

    public async Task MarkEducationDirectorOrientationViewedAsync(Guid actorUserId, string actorEmail)
    {
        var profile = await _dbContext.SchoolProfiles.FirstAsync();
        profile.EducationDirectorOrientationVideoViewedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.education-director-orientation.viewed", "SchoolProfile", profile.Id, profile.EducationDirectorName);
    }

    public async Task<List<InstructorComplianceRow>> GetInstructorComplianceStatusAsync()
    {
        var nowUtc = DateTime.UtcNow;

        var instructors = await _dbContext.SchoolStaffMembers
            .AsNoTracking()
            .Where(staff => staff.Role == SchoolStaffRoles.Instructor)
            .OrderBy(staff => staff.Name)
            .ToListAsync();

        return instructors
            .Select(staff =>
            {
                var hireReportDueAtUtc = staff.AddedAtUtc.AddDays(HireReportDueDays);
                var nextObservationDueAtUtc = (staff.LastObservationAtUtc ?? staff.AddedAtUtc).AddDays(ObservationIntervalDays);
                return new InstructorComplianceRow(
                    staff.Id,
                    staff.Name,
                    staff.AddedAtUtc,
                    hireReportDueAtUtc,
                    staff.HireReportedToCommissionAtUtc,
                    staff.HireReportedToCommissionAtUtc is null && hireReportDueAtUtc < nowUtc,
                    staff.LastObservationAtUtc,
                    nextObservationDueAtUtc,
                    nextObservationDueAtUtc < nowUtc);
            })
            .ToList();
    }

    public async Task<bool> MarkInstructorHireReportedAsync(Guid staffMemberId, Guid actorUserId, string actorEmail)
    {
        var staff = await _dbContext.SchoolStaffMembers.FirstOrDefaultAsync(existing => existing.Id == staffMemberId);
        if (staff is null || staff.HireReportedToCommissionAtUtc is not null)
        {
            return false;
        }

        staff.HireReportedToCommissionAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.instructor-hire-report.submitted", "SchoolStaffMember", staff.Id, staff.Name);
        return true;
    }

    public async Task<bool> RecordInstructorObservationAsync(Guid staffMemberId, Guid actorUserId, string actorEmail)
    {
        var staff = await _dbContext.SchoolStaffMembers.FirstOrDefaultAsync(existing => existing.Id == staffMemberId);
        if (staff is null)
        {
            return false;
        }

        staff.LastObservationAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.instructor-observation.recorded", "SchoolStaffMember", staff.Id, staff.Name);
        return true;
    }

    public async Task<List<RetentionCategorySummary>> GetRecordRetentionSummaryAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var retentionCutoffUtc = nowUtc.AddYears(-RecordRetentionYears);

        var enrollmentCount = await _dbContext.Enrollments.CountAsync();
        var oldestEnrollmentAtUtc = await _dbContext.Enrollments.OrderBy(e => e.EnrolledAt).Select(e => (DateTime?)e.EnrolledAt).FirstOrDefaultAsync();

        var attemptCount = await _dbContext.ExamAttempts.CountAsync();
        var oldestAttemptAtUtc = await _dbContext.ExamAttempts.OrderBy(a => a.StartedAt).Select(a => (DateTime?)a.StartedAt).FirstOrDefaultAsync();

        var proctoringCount = await _dbContext.ExamProctoringSessions.CountAsync();
        var oldestProctoringAtUtc = await _dbContext.ExamProctoringSessions.OrderBy(s => s.IdentityVerifiedAtUtc).Select(s => (DateTime?)s.IdentityVerifiedAtUtc).FirstOrDefaultAsync();

        var disclosureCount = await _dbContext.PolicyDisclosureAcknowledgments.CountAsync();
        var oldestDisclosureAtUtc = await _dbContext.PolicyDisclosureAcknowledgments.OrderBy(d => d.AcknowledgedAtUtc).Select(d => (DateTime?)d.AcknowledgedAtUtc).FirstOrDefaultAsync();

        return
        [
            new RetentionCategorySummary("Enrollment & Attendance Records", enrollmentCount, oldestEnrollmentAtUtc, RecordRetentionYears, oldestEnrollmentAtUtc is not null && oldestEnrollmentAtUtc < retentionCutoffUtc),
            new RetentionCategorySummary("Exam Attempt Records", attemptCount, oldestAttemptAtUtc, RecordRetentionYears, oldestAttemptAtUtc is not null && oldestAttemptAtUtc < retentionCutoffUtc),
            new RetentionCategorySummary("Proctoring Session Records", proctoringCount, oldestProctoringAtUtc, RecordRetentionYears, oldestProctoringAtUtc is not null && oldestProctoringAtUtc < retentionCutoffUtc),
            new RetentionCategorySummary("Policy Disclosure Acknowledgments", disclosureCount, oldestDisclosureAtUtc, RecordRetentionYears, oldestDisclosureAtUtc is not null && oldestDisclosureAtUtc < retentionCutoffUtc)
        ];
    }

    public async Task<CourseSessionScheduleResult> ScheduleCourseSessionAsync(Guid courseId, DateTime scheduledStartUtc, DateTime scheduledEndUtc, Guid actorUserId, string actorEmail)
    {
        var course = await _dbContext.Courses.AsNoTracking().FirstOrDefaultAsync(existing => existing.Id == courseId)
            ?? throw new InvalidOperationException("Course not found.");

        var warnings = new List<string>();
        if (!course.IsPrelicensingOrPostlicensing && IsWithinJuneBlackout(scheduledStartUtc))
        {
            warnings.Add("Scheduled start falls within the June 11-30 continuing-education blackout window (21 NCAC 58H .0404).");
        }

        if (scheduledEndUtc - scheduledStartUtc > TimeSpan.FromHours(MaxDailySessionHours))
        {
            warnings.Add($"Session duration exceeds the {MaxDailySessionHours}-hour daily maximum (21 NCAC 58H .0404).");
        }

        var session = new CourseSession
        {
            CourseId = courseId,
            ScheduledStartAtUtc = scheduledStartUtc,
            ScheduledEndAtUtc = scheduledEndUtc
        };

        _dbContext.CourseSessions.Add(session);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.course-session.scheduled", "CourseSession", session.Id, $"CourseId={courseId};Start={scheduledStartUtc:O}");

        return new CourseSessionScheduleResult(session.Id, warnings);
    }

    public async Task<List<ScheduledCourseSessionRow>> GetScheduledCourseSessionsAsync()
    {
        var sessions = await _dbContext.CourseSessions
            .AsNoTracking()
            .Include(session => session.Course)
            .Where(session => !session.IsCancelled)
            .OrderBy(session => session.ScheduledStartAtUtc)
            .ToListAsync();

        return sessions
            .Select(session => new ScheduledCourseSessionRow(
                session.Id,
                session.Course?.Title ?? "Unknown Course",
                session.ScheduledStartAtUtc,
                session.ScheduledEndAtUtc,
                session.CommissionNotifiedAtUtc is not null))
            .ToList();
    }

    public async Task<List<PendingScheduleNotificationRow>> GetPendingScheduleNotificationsAsync()
    {
        var nowUtc = DateTime.UtcNow;

        var sessions = await _dbContext.CourseSessions
            .AsNoTracking()
            .Include(session => session.Course)
            .Where(session => !session.IsCancelled && session.CommissionNotifiedAtUtc == null)
            .OrderBy(session => session.ScheduledStartAtUtc)
            .ToListAsync();

        return sessions
            .Select(session =>
            {
                var dueAtUtc = session.ScheduledStartAtUtc.AddDays(-ScheduleNoticeDueDays);
                return new PendingScheduleNotificationRow(session.Id, session.Course?.Title ?? "Unknown Course", session.ScheduledStartAtUtc, dueAtUtc, dueAtUtc < nowUtc);
            })
            .ToList();
    }

    public async Task<bool> MarkScheduleNotificationSentAsync(Guid sessionId, Guid actorUserId, string actorEmail)
    {
        var session = await _dbContext.CourseSessions.FirstOrDefaultAsync(existing => existing.Id == sessionId);
        if (session is null || session.CommissionNotifiedAtUtc is not null)
        {
            return false;
        }

        session.CommissionNotifiedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.course-session.notice-sent", "CourseSession", session.Id, $"CourseId={session.CourseId}");
        return true;
    }

    public async Task<bool> RescheduleCourseSessionAsync(Guid sessionId, DateTime newStartUtc, DateTime newEndUtc, Guid actorUserId, string actorEmail)
    {
        var session = await _dbContext.CourseSessions.FirstOrDefaultAsync(existing => existing.Id == sessionId);
        if (session is null || session.IsCancelled)
        {
            return false;
        }

        FlagPendingChangeIfLate(session);
        session.ScheduledStartAtUtc = newStartUtc;
        session.ScheduledEndAtUtc = newEndUtc;

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.course-session.rescheduled", "CourseSession", session.Id, $"NewStart={newStartUtc:O}");
        return true;
    }

    public async Task<bool> CancelCourseSessionAsync(Guid sessionId, Guid actorUserId, string actorEmail)
    {
        var session = await _dbContext.CourseSessions.FirstOrDefaultAsync(existing => existing.Id == sessionId);
        if (session is null || session.IsCancelled)
        {
            return false;
        }

        FlagPendingChangeIfLate(session);
        session.IsCancelled = true;

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.course-session.cancelled", "CourseSession", session.Id, $"CourseId={session.CourseId}");
        return true;
    }

    // Changes/cancellations within 5 days of the original start must be reported to the Commission within 24 hours.
    private static void FlagPendingChangeIfLate(CourseSession session)
    {
        if ((session.ScheduledStartAtUtc - DateTime.UtcNow).TotalDays <= ScheduleChangeWindowDays)
        {
            session.PendingChangeAtUtc = DateTime.UtcNow;
            session.ScheduleChangeReportedAtUtc = null;
        }
    }

    public async Task<List<PendingScheduleChangeReportRow>> GetPendingScheduleChangeReportsAsync()
    {
        var nowUtc = DateTime.UtcNow;

        var sessions = await _dbContext.CourseSessions
            .AsNoTracking()
            .Include(session => session.Course)
            .Where(session => session.PendingChangeAtUtc != null && session.ScheduleChangeReportedAtUtc == null)
            .OrderBy(session => session.PendingChangeAtUtc)
            .ToListAsync();

        return sessions
            .Select(session =>
            {
                var dueAtUtc = session.PendingChangeAtUtc!.Value.AddHours(ScheduleChangeReportDueHours);
                return new PendingScheduleChangeReportRow(session.Id, session.Course?.Title ?? "Unknown Course", session.PendingChangeAtUtc.Value, dueAtUtc, dueAtUtc < nowUtc);
            })
            .ToList();
    }

    public async Task<bool> MarkScheduleChangeReportedAsync(Guid sessionId, Guid actorUserId, string actorEmail)
    {
        var session = await _dbContext.CourseSessions.FirstOrDefaultAsync(existing => existing.Id == sessionId);
        if (session is null || session.PendingChangeAtUtc is null || session.ScheduleChangeReportedAtUtc is not null)
        {
            return false;
        }

        session.ScheduleChangeReportedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "compliance.course-session.change-reported", "CourseSession", session.Id, $"CourseId={session.CourseId}");
        return true;
    }

    private static bool IsWithinJuneBlackout(DateTime startUtc)
    {
        return startUtc.Month == 6 && startUtc.Day >= 11 && startUtc.Day <= 30;
    }

    public async Task<ComplianceReminderRunResult> ProcessComplianceRemindersAsync()
    {
        if (_notificationService is null)
        {
            return new ComplianceReminderRunResult(0);
        }

        var adminIds = await _dbContext.UserAccounts
            .AsNoTracking()
            .Where(user => user.Role == "Admin" && user.IsActive)
            .Select(user => user.Id)
            .ToListAsync();

        if (adminIds.Count == 0)
        {
            return new ComplianceReminderRunResult(0);
        }

        var sentCount = 0;

        var completionReports = await GetPendingCourseCompletionReportsAsync();
        sentCount += await NotifyForCategoryAsync(
            "completion-report",
            completionReports.Where(row => row.IsOverdue).Select(row => (row.EnrollmentId, $"Course completion report overdue for {row.StudentLegalName} — {row.CourseTitle} (due {row.DueAtUtc:yyyy-MM-dd}).")),
            adminIds);

        var cheatingIncidents = await GetPendingCheatingIncidentReportsAsync();
        sentCount += await NotifyForCategoryAsync(
            "cheating-incident",
            cheatingIncidents.Where(row => row.IsOverdue).Select(row => (row.ProctoringSessionId, $"Cheating incident report overdue for {row.StudentName} — {row.CourseTitle} (due {row.DueAtUtc:yyyy-MM-dd}).")),
            adminIds);

        var orientation = await GetEducationDirectorOrientationStatusAsync();
        if (orientation.IsOverdue && orientation.DueAtUtc.HasValue && orientation.DesignatedAtUtc.HasValue)
        {
            // Derive a stable-but-designation-specific key so a later re-designation can trigger a fresh reminder.
            var designationKey = new Guid(0, 0, 0, BitConverter.GetBytes(orientation.DesignatedAtUtc.Value.Ticks));
            sentCount += await NotifyForCategoryAsync(
                "education-director-orientation",
                [(designationKey, $"Education director orientation video overdue for {orientation.EducationDirectorName} (due {orientation.DueAtUtc.Value:yyyy-MM-dd}).")],
                adminIds);
        }

        var instructorRows = await GetInstructorComplianceStatusAsync();
        sentCount += await NotifyForCategoryAsync(
            "instructor-hire-report",
            instructorRows.Where(row => row.IsHireReportOverdue).Select(row => (row.StaffMemberId, $"Instructor hire report overdue for {row.Name} (due {row.HireReportDueAtUtc:yyyy-MM-dd}).")),
            adminIds);

        sentCount += await NotifyForCategoryAsync(
            "instructor-observation",
            instructorRows.Where(row => row.IsObservationOverdue).Select(row => (row.StaffMemberId, $"Annual instructor observation overdue for {row.Name} (due {row.NextObservationDueAtUtc:yyyy-MM-dd}).")),
            adminIds);

        var scheduleNotifications = await GetPendingScheduleNotificationsAsync();
        sentCount += await NotifyForCategoryAsync(
            "schedule-notification",
            scheduleNotifications.Where(row => row.IsOverdue).Select(row => (row.SessionId, $"Commission scheduling notice overdue for {row.CourseTitle} (due {row.DueAtUtc:yyyy-MM-dd}).")),
            adminIds);

        var scheduleChangeReports = await GetPendingScheduleChangeReportsAsync();
        sentCount += await NotifyForCategoryAsync(
            "schedule-change-report",
            scheduleChangeReports.Where(row => row.IsOverdue).Select(row => (row.SessionId, $"Schedule change report overdue for {row.CourseTitle} (due {row.DueAtUtc:yyyy-MM-dd}).")),
            adminIds);

        return new ComplianceReminderRunResult(sentCount);
    }

    private async Task<int> NotifyForCategoryAsync(string category, IEnumerable<(Guid EntityId, string Message)> items, List<Guid> adminIds)
    {
        var sent = 0;
        foreach (var (entityId, message) in items)
        {
            var alreadySent = await _dbContext.ComplianceReminderLogs.AnyAsync(log => log.Category == category && log.EntityId == entityId);
            if (alreadySent)
            {
                continue;
            }

            foreach (var adminId in adminIds)
            {
                await _notificationService!.CreateAsync(adminId, "compliance", "Compliance deadline overdue", message);
            }

            _dbContext.ComplianceReminderLogs.Add(new ComplianceReminderLog { Category = category, EntityId = entityId });
            sent++;
        }

        if (sent > 0)
        {
            await _dbContext.SaveChangesAsync();
        }

        return sent;
    }
}
