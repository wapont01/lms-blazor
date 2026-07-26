using System.Security.Cryptography;
using System.Text;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lms.Application.Services;

public interface IEnrollmentService
{
    Task<bool> IsEnrolledAsync(Guid userId, Guid courseId);
    Task EnrollAsync(Guid userId, Guid courseId);
    Task<List<Enrollment>> GetEnrollmentsForLearnerAsync(Guid userId);
    Task<Enrollment?> GetEnrollmentAsync(Guid userId, Guid courseId);
    Task<List<Course>> GetAvailableCoursesForLearnerAsync(Guid userId);
    Task<List<Course>> GetBrokerPurchasedCoursesAsync(Guid brokerUserId);
    Task<List<Course>> GetBrokerPurchasableCoursesForLearnerAsync(Guid brokerUserId, Guid learnerUserId);
    Task UpdateProgressAsync(Guid userId, Guid courseId, decimal progressPercent);
    Task<Dictionary<Guid, bool>> GetLessonCompletionMapAsync(Guid userId, Guid courseId);
    Task SetLessonCompletionAsync(Guid userId, Guid lessonId, bool completed);
    Task<List<LearnerCertificateRow>> GetCertificatesForLearnerAsync(Guid userId);
    Task<CertificateComplianceSummary> GetCertificateComplianceSummaryAsync(Guid? brokerUserId = null);
    Task<List<CertificateComplianceRow>> GetCertificateComplianceRowsAsync(Guid? brokerUserId = null);
    Task<CertificateDownloadPayload?> VerifyCertificateAsync(string certificateNumber, string verificationCode);
    Task<CertificateDownloadPayload?> GetCertificateDownloadPayloadAsync(Guid certificateId, Guid requesterUserId, bool isPrivileged);
    Task<byte[]?> GenerateCertificatePdfAsync(Guid certificateId, Guid requesterUserId, bool isPrivileged);
    Task<bool> RenewCertificateAsync(Guid certificateId, Guid actorUserId, string actorEmail);
    Task<BrokerDashboardSummary> GetBrokerSummaryAsync(Guid brokerUserId);
    Task<List<BrokerAssignedLearnerRow>> GetAssignedLearnersAsync(Guid brokerUserId);
    Task<List<BrokerLearnerRow>> GetBrokerLearnerRowsAsync(Guid brokerUserId);
    Task<List<AssessmentBlockedLearnerRow>> GetAssessmentBlockedLearnersAsync(Guid brokerUserId);
    Task<bool> AssignLearnerToBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid actorUserId, string actorEmail, string? reason = null);
    Task<bool> UnassignLearnerFromBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid actorUserId, string actorEmail, string? reason = null);
    Task<EnrollmentLifecycleResult> EnrollLearnerByBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid courseId, Guid actorUserId, string actorEmail);
    Task<EnrollmentLifecycleResult> UnenrollLearnerByBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid courseId, Guid actorUserId, string actorEmail);
    Task<EnrollmentLifecycleResult> TransferEnrollmentByBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid fromCourseId, Guid toCourseId, Guid actorUserId, string actorEmail);
    Task<List<EnrollmentLifecycleResult>> BulkEnrollLearnersByBrokerAsync(Guid brokerUserId, IReadOnlyCollection<Guid> learnerUserIds, Guid courseId, Guid actorUserId, string actorEmail);
    Task<EnrollmentLifecycleResult> AdminEnrollLearnerAsync(Guid learnerUserId, Guid courseId, Guid actorUserId, string actorEmail, string reason);
    Task<EnrollmentLifecycleResult> AdminRemoveEnrollmentAsync(Guid learnerUserId, Guid courseId, Guid actorUserId, string actorEmail, string reason);
    Task<EnrollmentLifecycleResult> AdminTransferEnrollmentAsync(Guid learnerUserId, Guid fromCourseId, Guid toCourseId, Guid actorUserId, string actorEmail, string reason);
    Task<List<LearnerLogItem>> GetLearnerLogsAsync(Guid learnerUserId, Guid? brokerUserId = null);
    Task<List<BrokerEnrollmentReportRow>> GetBrokerEnrollmentReportAsync(Guid brokerUserId, DateTime? fromUtc = null, DateTime? toUtc = null);
}

public sealed record BrokerDashboardSummary(int Learners, int ActiveEnrollments, int CompletedEnrollments, int AtRiskEnrollments);
public sealed record BrokerAssignedLearnerRow(Guid LearnerId, string LearnerName, string LearnerEmail, int ActiveEnrollments);
public sealed record BrokerLearnerRow(string LearnerName, string LearnerEmail, string CourseTitle, decimal ProgressPercent, bool Completed, DateTime EnrolledAt);
public sealed record AssessmentBlockedLearnerRow(string LearnerDisplay, string CourseTitle, decimal? LatestScorePercent, string Reason);
public sealed record LearnerCertificateRow(Guid CertificateId, Guid CourseId, string CourseTitle, string CertificateNumber, string VerificationCode, DateTime IssuedAt, DateTime ExpiresAt, string Status);
public sealed record CertificateComplianceSummary(int TotalCertificates, int ActiveCertificates, int ExpiringSoonCertificates, int ExpiredCertificates, int RevokedCertificates);
public sealed record CertificateComplianceRow(Guid CertificateId, string LearnerName, string LearnerEmail, string CourseTitle, string CertificateNumber, string VerificationCode, DateTime IssuedAt, DateTime ExpiresAt, string Status, bool CanRenew);
public sealed record CertificateDownloadPayload(Guid CertificateId, string CertificateNumber, string VerificationCode, string LearnerName, string LearnerEmail, string CourseTitle, DateTime IssuedAt, DateTime ExpiresAt, string Status, bool IsRevoked, string? RevocationReason);
public sealed record EnrollmentLifecycleResult(Guid LearnerUserId, Guid CourseId, bool Succeeded, string Action, string Message);
public sealed record LearnerLogItem(DateTime TimestampUtc, string Type, string Title, string Details);
public sealed record BrokerEnrollmentReportRow(string LearnerName, string LearnerEmail, string CourseTitle, DateTime EnrolledAt, decimal ProgressPercent, bool Completed);

public class EnrollmentService : IEnrollmentService
{
    private const string LearnerPurchaseSource = "LearnerPurchase";
    private const string BrokerSponsoredSource = "BrokerSponsored";
    private const string NotRequiredConsent = "NotRequired";
    private const string ApprovedConsent = "Approved";

    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly IAssessmentService _assessmentService;

    public EnrollmentService(ApplicationDbContext dbContext, IAuditLogService auditLogService, IAssessmentService assessmentService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _assessmentService = assessmentService;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<bool> IsEnrolledAsync(Guid userId, Guid courseId)
    {
        return await _dbContext.Enrollments.AnyAsync(enrollment =>
            enrollment.UserAccountId == userId && enrollment.CourseId == courseId);
    }

    public async Task EnrollAsync(Guid userId, Guid courseId)
    {
        var userExists = await _dbContext.UserAccounts.AnyAsync(user => user.Id == userId);
        if (!userExists)
        {
            throw new InvalidOperationException("Your session is out of date. Please sign in again.");
        }

        var courseExists = await _dbContext.Courses.AnyAsync(course => course.Id == courseId);
        if (!courseExists)
        {
            throw new InvalidOperationException("Course not found.");
        }

        var alreadyEnrolled = await IsEnrolledAsync(userId, courseId);
        if (alreadyEnrolled)
        {
            return;
        }

        var enrollment = new Enrollment
        {
            UserAccountId = userId,
            CourseId = courseId,
            EnrollmentSource = LearnerPurchaseSource,
            SponsoredByBrokerUserId = null,
            ConsentStatus = NotRequiredConsent,
            EnrolledAt = DateTime.UtcNow,
            DueAtUtc = DateTime.UtcNow.AddDays(30),
            DueSoonReminderSentAt = null,
            OverdueReminderSentAt = null,
            ProgressPercent = 0,
            Completed = false
        };

        _dbContext.Enrollments.Add(enrollment);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("Enrollment could not be completed. Please refresh and sign in again.");
        }

        await _assessmentService.EnsureDefaultAssessmentForCourseAsync(courseId);

        var actorEmail = await _dbContext.UserAccounts
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        await _auditLogService.WriteAsync(userId, actorEmail, "enrollment.created", "Enrollment", enrollment.Id, $"CourseId={courseId}");
    }

    public async Task<List<Enrollment>> GetEnrollmentsForLearnerAsync(Guid userId)
    {
        return await _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.Course)
            .Where(enrollment => enrollment.UserAccountId == userId)
            .OrderByDescending(enrollment => enrollment.EnrolledAt)
            .ToListAsync();
    }

    public async Task<Enrollment?> GetEnrollmentAsync(Guid userId, Guid courseId)
    {
        return await _dbContext.Enrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(enrollment => enrollment.UserAccountId == userId && enrollment.CourseId == courseId);
    }

    public async Task<List<Course>> GetAvailableCoursesForLearnerAsync(Guid userId)
    {
        var enrolledCourseIds = await _dbContext.Enrollments
            .Where(enrollment => enrollment.UserAccountId == userId)
            .Select(enrollment => enrollment.CourseId)
            .ToListAsync();

        return await _dbContext.Courses
            .AsNoTracking()
            .Where(course => course.IsPublished && !course.IsArchived && !enrolledCourseIds.Contains(course.Id))
            .OrderBy(course => course.Title)
            .ToListAsync();
    }

    public async Task<List<Course>> GetBrokerPurchasedCoursesAsync(Guid brokerUserId)
    {
        return await _dbContext.Enrollments
            .AsNoTracking()
            .Where(enrollment =>
                enrollment.UserAccountId == brokerUserId &&
                enrollment.EnrollmentSource == LearnerPurchaseSource)
            .Select(enrollment => enrollment.Course!)
            .Where(course => course.IsPublished && !course.IsArchived)
            .Distinct()
            .OrderBy(course => course.Title)
            .ToListAsync();
    }

    public async Task<List<Course>> GetBrokerPurchasableCoursesForLearnerAsync(Guid brokerUserId, Guid learnerUserId)
    {
        var brokerCourseIds = await _dbContext.Enrollments
            .AsNoTracking()
            .Where(enrollment =>
                enrollment.UserAccountId == brokerUserId &&
                enrollment.EnrollmentSource == LearnerPurchaseSource)
            .Select(enrollment => enrollment.CourseId)
            .ToListAsync();

        if (brokerCourseIds.Count == 0)
        {
            return new List<Course>();
        }

        var learnerCourseIds = await _dbContext.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.UserAccountId == learnerUserId)
            .Select(enrollment => enrollment.CourseId)
            .ToListAsync();

        return await _dbContext.Courses
            .AsNoTracking()
            .Where(course =>
                course.IsPublished &&
                !course.IsArchived &&
                brokerCourseIds.Contains(course.Id) &&
                !learnerCourseIds.Contains(course.Id))
            .OrderBy(course => course.Title)
            .ToListAsync();
    }

    public async Task UpdateProgressAsync(Guid userId, Guid courseId, decimal progressPercent)
    {
        var enrollment = await _dbContext.Enrollments.FirstOrDefaultAsync(existing =>
            existing.UserAccountId == userId && existing.CourseId == courseId);

        if (enrollment is null)
        {
            throw new InvalidOperationException("Enrollment not found.");
        }

        var clampedProgress = Math.Clamp(progressPercent, 0, 100);
        enrollment.ProgressPercent = clampedProgress;
        enrollment.Completed = clampedProgress >= 100;

        await _dbContext.SaveChangesAsync();

        var actorEmail = await _dbContext.UserAccounts
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        await EnsureCertificateStateAsync(userId, enrollment, actorEmail);

        await _auditLogService.WriteAsync(userId, actorEmail, "enrollment.progress.updated", "Enrollment", enrollment.Id, $"Progress={clampedProgress:0.##}");
    }

    public async Task<Dictionary<Guid, bool>> GetLessonCompletionMapAsync(Guid userId, Guid courseId)
    {
        return await _dbContext.LessonProgresses
            .AsNoTracking()
            .Include(progress => progress.Lesson)
                .ThenInclude(lesson => lesson!.Module)
            .Where(progress =>
                progress.UserAccountId == userId &&
                progress.Lesson != null &&
                progress.Lesson.Module != null &&
                progress.Lesson.Module.CourseId == courseId)
            .ToDictionaryAsync(progress => progress.LessonId, progress => progress.Completed);
    }

    public async Task SetLessonCompletionAsync(Guid userId, Guid lessonId, bool completed)
    {
        var lessonCourse = await _dbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.Id == lessonId)
            .Select(lesson => new { lesson.Id, lesson.ModuleId, lesson.IsRequired, CourseId = lesson.Module!.CourseId })
            .FirstOrDefaultAsync();

        if (lessonCourse is null)
        {
            throw new InvalidOperationException("Lesson was not found.");
        }

        var enrollment = await _dbContext.Enrollments.FirstOrDefaultAsync(existing =>
            existing.UserAccountId == userId && existing.CourseId == lessonCourse.CourseId);

        if (enrollment is null)
        {
            throw new InvalidOperationException("You must be enrolled in this course before tracking lesson completion.");
        }

        var progress = await _dbContext.LessonProgresses.FirstOrDefaultAsync(existing =>
            existing.UserAccountId == userId && existing.LessonId == lessonId);

        if (progress is null)
        {
            progress = new LessonProgress
            {
                UserAccountId = userId,
                LessonId = lessonId,
                Completed = completed,
                CompletedAt = completed ? DateTime.UtcNow : null
            };

            _dbContext.LessonProgresses.Add(progress);
        }
        else
        {
            progress.Completed = completed;
            progress.CompletedAt = completed ? DateTime.UtcNow : null;
        }

        await _dbContext.SaveChangesAsync();

        await RecalculateEnrollmentProgressAsync(userId, lessonCourse.CourseId, enrollment);

        var actorEmail = await _dbContext.UserAccounts
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        await _auditLogService.WriteAsync(userId, actorEmail, "lesson.completion.updated", "Lesson", lessonId, completed ? "Completed" : "Marked incomplete");
    }

    public async Task<List<LearnerCertificateRow>> GetCertificatesForLearnerAsync(Guid userId)
    {
        var now = DateTime.UtcNow;

        var actorEmail = await _dbContext.UserAccounts
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        var enrollments = await _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.Course)
            .Where(enrollment => enrollment.UserAccountId == userId)
            .ToListAsync();

        foreach (var enrollment in enrollments)
        {
            await EnsureCertificateStateAsync(userId, enrollment, actorEmail);
        }

        var certificates = await _dbContext.CompletionCertificates
            .AsNoTracking()
            .Include(certificate => certificate.Course)
            .Where(certificate => certificate.UserAccountId == userId)
            .OrderByDescending(certificate => certificate.IssuedAt)
            .ToListAsync();

        return certificates
            .Select(certificate => new LearnerCertificateRow(
                certificate.Id,
                certificate.CourseId,
                certificate.Course?.Title ?? "Unknown Course",
                certificate.CertificateNumber,
                certificate.VerificationCode,
                certificate.IssuedAt,
                certificate.ExpiresAt,
                GetCertificateStatus(certificate, now)))
            .ToList();
    }

    public async Task<CertificateComplianceSummary> GetCertificateComplianceSummaryAsync(Guid? brokerUserId = null)
    {
        var rows = await GetCertificateComplianceRowsAsync(brokerUserId);

        return new CertificateComplianceSummary(
            rows.Count,
            rows.Count(row => row.Status == "Active"),
            rows.Count(row => row.Status == "Expiring Soon"),
            rows.Count(row => row.Status == "Expired"),
            rows.Count(row => row.Status == "Revoked"));
    }

    public async Task<List<CertificateComplianceRow>> GetCertificateComplianceRowsAsync(Guid? brokerUserId = null)
    {
        var now = DateTime.UtcNow;
        var visibleLearnerIds = await GetVisibleLearnerIdsAsync();

        var certificatesQuery = _dbContext.CompletionCertificates
            .AsNoTracking()
            .Include(certificate => certificate.UserAccount)
            .Include(certificate => certificate.Course)
            .OrderByDescending(certificate => certificate.IssuedAt)
            .AsQueryable();

        certificatesQuery = certificatesQuery.Where(certificate => visibleLearnerIds.Contains(certificate.UserAccountId));

        var certificates = await certificatesQuery.ToListAsync();

        return certificates
            .Select(certificate => new CertificateComplianceRow(
                certificate.Id,
                certificate.UserAccount?.DisplayName ?? "Unknown Learner",
                certificate.UserAccount?.Email ?? "unknown@lms.local",
                certificate.Course?.Title ?? "Unknown Course",
                certificate.CertificateNumber,
                certificate.VerificationCode,
                certificate.IssuedAt,
                certificate.ExpiresAt,
                GetCertificateStatus(certificate, now),
                certificate.IsRevoked || certificate.ExpiresAt <= now))
            .ToList();
    }

    public async Task<CertificateDownloadPayload?> GetCertificateDownloadPayloadAsync(Guid certificateId, Guid requesterUserId, bool isPrivileged)
    {
        var certificate = await _dbContext.CompletionCertificates
            .AsNoTracking()
            .Include(existing => existing.UserAccount)
            .Include(existing => existing.Course)
            .FirstOrDefaultAsync(existing => existing.Id == certificateId);

        if (certificate is null)
        {
            return null;
        }

        if (!isPrivileged && certificate.UserAccountId != requesterUserId)
        {
            return null;
        }

        var status = GetCertificateStatus(certificate, DateTime.UtcNow);

        return new CertificateDownloadPayload(
            certificate.Id,
            certificate.CertificateNumber,
            certificate.VerificationCode,
            certificate.UserAccount?.DisplayName ?? "Unknown Learner",
            certificate.UserAccount?.Email ?? "unknown@lms.local",
            certificate.Course?.Title ?? "Unknown Course",
            certificate.IssuedAt,
            certificate.ExpiresAt,
            status,
            certificate.IsRevoked,
            certificate.RevocationReason);
    }

    public async Task<CertificateDownloadPayload?> VerifyCertificateAsync(string certificateNumber, string verificationCode)
    {
        var normalizedNumber = certificateNumber.Trim();
        var normalizedCode = verificationCode.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedNumber) || string.IsNullOrWhiteSpace(normalizedCode))
        {
            return null;
        }

        var certificate = await _dbContext.CompletionCertificates
            .AsNoTracking()
            .Include(existing => existing.UserAccount)
            .Include(existing => existing.Course)
            .FirstOrDefaultAsync(existing =>
                existing.CertificateNumber == normalizedNumber &&
                existing.VerificationCode == normalizedCode);

        if (certificate is null)
        {
            return null;
        }

        return new CertificateDownloadPayload(
            certificate.Id,
            certificate.CertificateNumber,
            certificate.VerificationCode,
            certificate.UserAccount?.DisplayName ?? "Unknown Learner",
            certificate.UserAccount?.Email ?? "unknown@lms.local",
            certificate.Course?.Title ?? "Unknown Course",
            certificate.IssuedAt,
            certificate.ExpiresAt,
            GetCertificateStatus(certificate, DateTime.UtcNow),
            certificate.IsRevoked,
            certificate.RevocationReason);
    }

    public async Task<byte[]?> GenerateCertificatePdfAsync(Guid certificateId, Guid requesterUserId, bool isPrivileged)
    {
        var payload = await GetCertificateDownloadPayloadAsync(certificateId, requesterUserId, isPrivileged);
        if (payload is null)
        {
            return null;
        }

        var verifyUrl = $"/verify-certificate?certificateNumber={Uri.EscapeDataString(payload.CertificateNumber)}&verificationCode={Uri.EscapeDataString(payload.VerificationCode)}";
        byte[] qrBytes;

        using (var qrGenerator = new QRCodeGenerator())
        using (var qrData = qrGenerator.CreateQrCode(verifyUrl, QRCodeGenerator.ECCLevel.Q))
        {
            var pngQrCode = new PngByteQRCode(qrData);
            qrBytes = pngQrCode.GetGraphic(8);
        }

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(style => style.FontSize(11));

                page.Content().Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Text("Learning Completion Certificate").FontSize(28).Bold().FontColor(Colors.Blue.Darken2);
                    column.Item().Text($"Certificate Number: {payload.CertificateNumber}").Bold();

                    column.Item().PaddingTop(6).Text(text =>
                    {
                        text.Span("This certifies that ").FontSize(12);
                        text.Span(payload.LearnerName).Bold().FontSize(14);
                        text.Span(" has successfully completed ").FontSize(12).FontColor(Colors.Green.Darken2);
                        text.Span(payload.CourseTitle).Bold().FontSize(14);
                        text.Span(".").FontSize(12);
                    });

                    column.Item().Text($"Issued (UTC): {payload.IssuedAt:u}");
                    column.Item().Text($"Expires (UTC): {payload.ExpiresAt:u}");
                    column.Item().Text($"Status: {payload.Status}");
                    column.Item().Text($"Verification Code: {payload.VerificationCode}");

                    if (!string.IsNullOrWhiteSpace(payload.RevocationReason))
                    {
                        column.Item().Text($"Revocation Reason: {payload.RevocationReason}").FontColor(Colors.Red.Darken2);
                    }

                    column.Item().PaddingTop(10).Text("Scan QR to verify online").SemiBold();
                    column.Item().Width(140).Image(qrBytes);
                    column.Item().Text($"Verification URL: {verifyUrl}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return pdf.GeneratePdf();
    }

    public async Task<bool> RenewCertificateAsync(Guid certificateId, Guid actorUserId, string actorEmail)
    {
        var certificate = await _dbContext.CompletionCertificates
            .FirstOrDefaultAsync(existing => existing.Id == certificateId);

        if (certificate is null)
        {
            return false;
        }

        var renewedAt = DateTime.UtcNow;
        certificate.IssuedAt = renewedAt;
        certificate.ExpiresAt = renewedAt.AddYears(1);
        certificate.IsRevoked = false;
        certificate.RevokedAt = null;
        certificate.RevocationReason = null;
        certificate.VerificationCode = GenerateVerificationCode(certificate.CertificateNumber, certificate.UserAccountId, certificate.CourseId, renewedAt);

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "certificate.renewed", "CompletionCertificate", certificate.Id, certificate.CertificateNumber);
        return true;
    }

    public async Task<BrokerDashboardSummary> GetBrokerSummaryAsync(Guid brokerUserId)
    {
        var assignedLearnerIds = await GetVisibleLearnerIdsAsync();

        var learners = assignedLearnerIds.Count;
        var enrollments = await _dbContext.Enrollments
            .AsNoTracking()
            .Where(enrollment => assignedLearnerIds.Contains(enrollment.UserAccountId))
            .Select(enrollment => new { enrollment.CourseId, enrollment.UserAccountId, enrollment.Completed, enrollment.ProgressPercent })
            .ToListAsync();

        var completedEnrollments = 0;
        var atRiskEnrollments = 0;

        foreach (var enrollment in enrollments)
        {
            var effectiveCompletion = enrollment.Completed || await _assessmentService.HasPassedRequiredAssessmentAsync(enrollment.CourseId, enrollment.UserAccountId);
            if (effectiveCompletion)
            {
                completedEnrollments++;
                continue;
            }

            if (enrollment.ProgressPercent < 30)
            {
                atRiskEnrollments++;
            }
        }

        return new BrokerDashboardSummary(learners, enrollments.Count, completedEnrollments, atRiskEnrollments);
    }

    public async Task<List<BrokerAssignedLearnerRow>> GetAssignedLearnersAsync(Guid brokerUserId)
    {
        var learners = await _dbContext.UserAccounts
            .AsNoTracking()
            .Where(user => user.Role == "Learner" && user.IsActive)
            .OrderBy(user => user.DisplayName)
            .ToListAsync();

        var enrollmentCounts = await _dbContext.Enrollments
            .AsNoTracking()
            .GroupBy(enrollment => enrollment.UserAccountId)
            .Select(group => new { LearnerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.LearnerId, item => item.Count);

        return learners
            .Select(learner => new BrokerAssignedLearnerRow(
                learner.Id,
                learner.DisplayName,
                learner.Email,
                enrollmentCounts.TryGetValue(learner.Id, out var count) ? count : 0))
            .OrderBy(row => row.LearnerName)
            .ToList();
    }

    public async Task<List<BrokerLearnerRow>> GetBrokerLearnerRowsAsync(Guid brokerUserId)
    {
        var assignedLearnerIds = await GetVisibleLearnerIdsAsync();

        var enrollments = await _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.UserAccount)
            .Include(enrollment => enrollment.Course)
            .Where(enrollment => assignedLearnerIds.Contains(enrollment.UserAccountId))
            .OrderByDescending(enrollment => enrollment.EnrolledAt)
            .ToListAsync();

        var rows = new List<BrokerLearnerRow>(enrollments.Count);
        foreach (var enrollment in enrollments)
        {
            var effectiveCompletion = enrollment.Completed || await _assessmentService.HasPassedRequiredAssessmentAsync(enrollment.CourseId, enrollment.UserAccountId);
            var effectiveProgress = effectiveCompletion ? 100m : enrollment.ProgressPercent;

            rows.Add(new BrokerLearnerRow(
                enrollment.UserAccount!.DisplayName,
                enrollment.UserAccount.Email,
                enrollment.Course!.Title,
                effectiveProgress,
                effectiveCompletion,
                enrollment.EnrolledAt));
        }

        return rows;
    }

    public async Task<List<AssessmentBlockedLearnerRow>> GetAssessmentBlockedLearnersAsync(Guid brokerUserId)
    {
        var rows = new List<AssessmentBlockedLearnerRow>();

        var assignedLearnerIds = await GetVisibleLearnerIdsAsync();

        var enrollments = await _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.UserAccount)
            .Include(enrollment => enrollment.Course)
            .Where(enrollment => assignedLearnerIds.Contains(enrollment.UserAccountId))
            .ToListAsync();

        foreach (var enrollment in enrollments)
        {
            var eligibility = await _assessmentService.GetEligibilityAsync(enrollment.CourseId, enrollment.UserAccountId);
            if (!eligibility.RequiresAssessment || eligibility.HasPassed)
            {
                continue;
            }

            rows.Add(new AssessmentBlockedLearnerRow(
                $"{enrollment.UserAccount?.DisplayName ?? "Learner"} ({enrollment.UserAccount?.Email ?? "unknown@lms.local"})",
                enrollment.Course?.Title ?? "Unknown Course",
                eligibility.LatestAttempt?.ScorePercent,
                eligibility.BlockingReason ?? "Assessment policy gate not satisfied."));
        }

        return rows
            .OrderBy(row => row.CourseTitle)
            .ThenBy(row => row.LearnerDisplay)
            .ToList();
    }

    public async Task<bool> AssignLearnerToBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid actorUserId, string actorEmail, string? reason = null)
    {
        var brokerValid = await _dbContext.UserAccounts.AnyAsync(user => user.Id == brokerUserId && user.Role == "Broker" && user.IsActive);
        var learnerValid = await _dbContext.UserAccounts.AnyAsync(user => user.Id == learnerUserId && user.Role == "Learner" && user.IsActive);

        if (!brokerValid || !learnerValid)
        {
            return false;
        }

        var exists = await _dbContext.BrokerLearnerAssignments.AnyAsync(assignment =>
            assignment.BrokerUserId == brokerUserId && assignment.LearnerUserId == learnerUserId);

        if (exists)
        {
            return true;
        }

        _dbContext.BrokerLearnerAssignments.Add(new BrokerLearnerAssignment
        {
            BrokerUserId = brokerUserId,
            LearnerUserId = learnerUserId,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = actorUserId
        });

        await _dbContext.SaveChangesAsync();
        var auditDetails = $"BrokerUserId={brokerUserId}" + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $";Reason={reason}");
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "broker.assignment.created", "BrokerLearnerAssignment", learnerUserId, auditDetails);
        await CreateNotificationAsync(learnerUserId, "assignment", "Broker assignment updated", "You were assigned to a broker portfolio.");
        return true;
    }

    public async Task<bool> UnassignLearnerFromBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid actorUserId, string actorEmail, string? reason = null)
    {
        var assignment = await _dbContext.BrokerLearnerAssignments.FirstOrDefaultAsync(existing =>
            existing.BrokerUserId == brokerUserId && existing.LearnerUserId == learnerUserId);

        if (assignment is null)
        {
            return false;
        }

        _dbContext.BrokerLearnerAssignments.Remove(assignment);
        await _dbContext.SaveChangesAsync();
        var auditDetails = $"BrokerUserId={brokerUserId}" + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $";Reason={reason}");
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "broker.assignment.removed", "BrokerLearnerAssignment", learnerUserId, auditDetails);
        await CreateNotificationAsync(learnerUserId, "assignment", "Broker assignment updated", "You were removed from a broker portfolio.");
        return true;
    }

    public async Task<EnrollmentLifecycleResult> AdminEnrollLearnerAsync(Guid learnerUserId, Guid courseId, Guid actorUserId, string actorEmail, string reason)
    {
        var learnerValid = await _dbContext.UserAccounts.AnyAsync(user => user.Id == learnerUserId && user.Role == "Learner" && user.IsActive);
        if (!learnerValid)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "repair", "Learner not found or not active.");
        }

        var courseValid = await _dbContext.Courses.AnyAsync(course => course.Id == courseId);
        if (!courseValid)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "correction", "Course not found.");
        }

        var alreadyEnrolled = await IsEnrolledAsync(learnerUserId, courseId);
        if (alreadyEnrolled)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, true, "correction", "Learner is already enrolled.");
        }

        var enrollment = new Enrollment
        {
            UserAccountId = learnerUserId,
            CourseId = courseId,
            EnrollmentSource = LearnerPurchaseSource,
            SponsoredByBrokerUserId = null,
            ConsentStatus = NotRequiredConsent,
            EnrolledAt = DateTime.UtcNow,
            DueAtUtc = DateTime.UtcNow.AddDays(30),
            DueSoonReminderSentAt = null,
            OverdueReminderSentAt = null,
            ProgressPercent = 0,
            Completed = false
        };

        _dbContext.Enrollments.Add(enrollment);
        await _dbContext.SaveChangesAsync();
        await _assessmentService.EnsureDefaultAssessmentForCourseAsync(courseId);

        var auditDetails = $"LearnerUserId={learnerUserId};CourseId={courseId};Reason={reason}";
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "enrollment.created.by-admin", "Enrollment", enrollment.Id, auditDetails);
        await CreateNotificationAsync(learnerUserId, "enrollment", "Enrollment corrected", "An admin restored your course enrollment.");
        return new EnrollmentLifecycleResult(learnerUserId, courseId, true, "correction", "Enrollment restored.");
    }

    public async Task<EnrollmentLifecycleResult> AdminRemoveEnrollmentAsync(Guid learnerUserId, Guid courseId, Guid actorUserId, string actorEmail, string reason)
    {
        var enrollment = await _dbContext.Enrollments
            .Include(existing => existing.CompletionCertificate)
            .FirstOrDefaultAsync(existing => existing.UserAccountId == learnerUserId && existing.CourseId == courseId);

        if (enrollment is null)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "correction", "Enrollment not found.");
        }

        var lessonIds = await _dbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.Module!.CourseId == courseId)
            .Select(lesson => lesson.Id)
            .ToListAsync();

        var progresses = await _dbContext.LessonProgresses
            .Where(progress => progress.UserAccountId == learnerUserId && lessonIds.Contains(progress.LessonId))
            .ToListAsync();

        if (progresses.Count > 0)
        {
            _dbContext.LessonProgresses.RemoveRange(progresses);
        }

        _dbContext.Enrollments.Remove(enrollment);
        await _dbContext.SaveChangesAsync();

        var auditDetails = $"LearnerUserId={learnerUserId};CourseId={courseId};Reason={reason}";
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "enrollment.removed.by-admin", "Enrollment", enrollment.Id, auditDetails);
        await CreateNotificationAsync(learnerUserId, "enrollment", "Enrollment corrected", "An admin removed an incorrect course enrollment.");
        return new EnrollmentLifecycleResult(learnerUserId, courseId, true, "correction", "Enrollment removed.");
    }

    public async Task<EnrollmentLifecycleResult> AdminTransferEnrollmentAsync(Guid learnerUserId, Guid fromCourseId, Guid toCourseId, Guid actorUserId, string actorEmail, string reason)
    {
        if (fromCourseId == toCourseId)
        {
            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "correction", "Source and destination courses must differ.");
        }

        var targetExists = await IsEnrolledAsync(learnerUserId, toCourseId);
        if (targetExists)
        {
            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "correction", "Learner is already enrolled in the destination course.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var removal = await AdminRemoveEnrollmentAsync(learnerUserId, fromCourseId, actorUserId, actorEmail, reason);
            if (!removal.Succeeded)
            {
                await transaction.RollbackAsync();
                return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "correction", $"Transfer failed during remove: {removal.Message}");
            }

            var add = await AdminEnrollLearnerAsync(learnerUserId, toCourseId, actorUserId, actorEmail, reason);
            if (!add.Succeeded)
            {
                await transaction.RollbackAsync();
                return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "correction", $"Transfer failed during restore: {add.Message}");
            }

            await _auditLogService.WriteAsync(actorUserId, actorEmail, "enrollment.transferred.by-admin", "Enrollment", learnerUserId, $"FromCourseId={fromCourseId};ToCourseId={toCourseId};Reason={reason}");
            await CreateNotificationAsync(learnerUserId, "enrollment", "Enrollment corrected", "An admin moved your enrollment to the correct course.");
            await transaction.CommitAsync();

            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, true, "correction", "Enrollment transferred.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<EnrollmentLifecycleResult> EnrollLearnerByBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid courseId, Guid actorUserId, string actorEmail)
    {
        var canManageLearner = await CanBrokerManageLearnerAsync(brokerUserId, learnerUserId);
        if (!canManageLearner)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "enroll", "Broker is not assigned to this learner.");
        }

        var canUseCourse = await CanBrokerUseCourseAsync(brokerUserId, courseId);
        if (!canUseCourse)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "enroll", "Broker has not purchased this course.");
        }

        var alreadyEnrolled = await IsEnrolledAsync(learnerUserId, courseId);
        if (alreadyEnrolled)
        {
            var existingEnrollment = await _dbContext.Enrollments.FirstAsync(existing =>
                existing.UserAccountId == learnerUserId && existing.CourseId == courseId);

            if (!IsBrokerSponsoredEnrollment(existingEnrollment, brokerUserId))
            {
                return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "enroll", "Learner is already enrolled in this course.");
            }

            return new EnrollmentLifecycleResult(learnerUserId, courseId, true, "enroll", "Learner is already enrolled.");
        }

        var enrollment = new Enrollment
        {
            UserAccountId = learnerUserId,
            CourseId = courseId,
            EnrollmentSource = BrokerSponsoredSource,
            SponsoredByBrokerUserId = brokerUserId,
            ConsentStatus = ApprovedConsent,
            EnrolledAt = DateTime.UtcNow,
            DueAtUtc = DateTime.UtcNow.AddDays(30),
            DueSoonReminderSentAt = null,
            OverdueReminderSentAt = null,
            ProgressPercent = 0,
            Completed = false
        };

        _dbContext.Enrollments.Add(enrollment);
        await _dbContext.SaveChangesAsync();
        await _assessmentService.EnsureDefaultAssessmentForCourseAsync(courseId);

        await _auditLogService.WriteAsync(actorUserId, actorEmail, "enrollment.created.by-broker", "Enrollment", enrollment.Id, $"LearnerUserId={learnerUserId};CourseId={courseId}");
        await CreateNotificationAsync(learnerUserId, "enrollment", "New course enrollment", "A broker enrolled you in a course.");
        return new EnrollmentLifecycleResult(learnerUserId, courseId, true, "enroll", "Enrollment created.");
    }

    public async Task<EnrollmentLifecycleResult> UnenrollLearnerByBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid courseId, Guid actorUserId, string actorEmail)
    {
        var canManageLearner = await CanBrokerManageLearnerAsync(brokerUserId, learnerUserId);
        if (!canManageLearner)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "unenroll", "Broker is not assigned to this learner.");
        }

        var canUseCourse = await CanBrokerUseCourseAsync(brokerUserId, courseId);
        if (!canUseCourse)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "unenroll", "Broker has not purchased this course.");
        }

        var enrollment = await _dbContext.Enrollments
            .Include(existing => existing.CompletionCertificate)
            .FirstOrDefaultAsync(existing => existing.UserAccountId == learnerUserId && existing.CourseId == courseId);

        if (enrollment is null)
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "unenroll", "Enrollment not found.");
        }

        if (!IsBrokerSponsoredEnrollment(enrollment, brokerUserId))
        {
            return new EnrollmentLifecycleResult(learnerUserId, courseId, false, "unenroll", "This enrollment is not broker enrolled by this broker.");
        }

        var lessonIds = await _dbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.Module!.CourseId == courseId)
            .Select(lesson => lesson.Id)
            .ToListAsync();

        var progresses = await _dbContext.LessonProgresses
            .Where(progress => progress.UserAccountId == learnerUserId && lessonIds.Contains(progress.LessonId))
            .ToListAsync();

        if (progresses.Count > 0)
        {
            _dbContext.LessonProgresses.RemoveRange(progresses);
        }

        _dbContext.Enrollments.Remove(enrollment);
        await _dbContext.SaveChangesAsync();

        await _auditLogService.WriteAsync(actorUserId, actorEmail, "enrollment.removed.by-broker", "Enrollment", enrollment.Id, $"LearnerUserId={learnerUserId};CourseId={courseId}");
        await CreateNotificationAsync(learnerUserId, "enrollment", "Course unenrollment", "A broker unenrolled you from a course.");
        return new EnrollmentLifecycleResult(learnerUserId, courseId, true, "unenroll", "Enrollment removed.");
    }

    public async Task<EnrollmentLifecycleResult> TransferEnrollmentByBrokerAsync(Guid brokerUserId, Guid learnerUserId, Guid fromCourseId, Guid toCourseId, Guid actorUserId, string actorEmail)
    {
        var canManageLearner = await CanBrokerManageLearnerAsync(brokerUserId, learnerUserId);
        if (!canManageLearner)
        {
            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "transfer", "Broker is not assigned to this learner.");
        }

        if (fromCourseId == toCourseId)
        {
            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "transfer", "Source and destination courses must differ.");
        }

        var canUseSourceCourse = await CanBrokerUseCourseAsync(brokerUserId, fromCourseId);
        if (!canUseSourceCourse)
        {
            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "transfer", "Broker has not purchased the source course.");
        }

        var canUseTargetCourse = await CanBrokerUseCourseAsync(brokerUserId, toCourseId);
        if (!canUseTargetCourse)
        {
            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "transfer", "Broker has not purchased the target course.");
        }

        var sourceEnrollment = await _dbContext.Enrollments.AsNoTracking().FirstOrDefaultAsync(existing =>
            existing.UserAccountId == learnerUserId && existing.CourseId == fromCourseId);

        if (sourceEnrollment is null)
        {
            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "transfer", "Transfer source enrollment not found.");
        }

        if (!IsBrokerSponsoredEnrollment(sourceEnrollment, brokerUserId))
        {
            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "transfer", "This enrollment is not broker enrolled by this broker.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var removal = await UnenrollLearnerByBrokerAsync(brokerUserId, learnerUserId, fromCourseId, actorUserId, actorEmail);
            if (!removal.Succeeded)
            {
                await transaction.RollbackAsync();
                return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "transfer", $"Transfer failed during unenroll: {removal.Message}");
            }

            var add = await EnrollLearnerByBrokerAsync(brokerUserId, learnerUserId, toCourseId, actorUserId, actorEmail);
            if (!add.Succeeded)
            {
                await transaction.RollbackAsync();
                return new EnrollmentLifecycleResult(learnerUserId, toCourseId, false, "transfer", $"Transfer failed during enroll: {add.Message}");
            }

            await _auditLogService.WriteAsync(actorUserId, actorEmail, "enrollment.transferred.by-broker", "Enrollment", learnerUserId, $"FromCourseId={fromCourseId};ToCourseId={toCourseId}");
            await CreateNotificationAsync(learnerUserId, "enrollment", "Enrollment transferred", "A broker transferred your enrollment to a different course.");
            await transaction.CommitAsync();

            return new EnrollmentLifecycleResult(learnerUserId, toCourseId, true, "transfer", "Enrollment transferred.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<EnrollmentLifecycleResult>> BulkEnrollLearnersByBrokerAsync(Guid brokerUserId, IReadOnlyCollection<Guid> learnerUserIds, Guid courseId, Guid actorUserId, string actorEmail)
    {
        var results = new List<EnrollmentLifecycleResult>(learnerUserIds.Count);

        foreach (var learnerUserId in learnerUserIds.Distinct())
        {
            var result = await EnrollLearnerByBrokerAsync(brokerUserId, learnerUserId, courseId, actorUserId, actorEmail);
            results.Add(result);
        }

        return results;
    }

    public async Task<List<LearnerLogItem>> GetLearnerLogsAsync(Guid learnerUserId, Guid? brokerUserId = null)
    {
        var events = new List<LearnerLogItem>();

        var enrollments = await _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.Course)
            .Where(enrollment => enrollment.UserAccountId == learnerUserId)
            .ToListAsync();

        events.AddRange(enrollments.Select(enrollment => new LearnerLogItem(
            enrollment.EnrolledAt,
            "enrollment",
            $"Enrolled: {enrollment.Course?.Title ?? "Unknown Course"}",
            $"Progress={enrollment.ProgressPercent:0.##}; Completed={enrollment.Completed}")));

        var lessonEvents = await _dbContext.LessonProgresses
            .AsNoTracking()
            .Include(progress => progress.Lesson)
            .Where(progress => progress.UserAccountId == learnerUserId && progress.Completed && progress.CompletedAt.HasValue)
            .ToListAsync();

        events.AddRange(lessonEvents.Select(progress => new LearnerLogItem(
            progress.CompletedAt!.Value,
            "lesson",
            $"Lesson completed: {progress.Lesson?.Title ?? "Unknown Lesson"}",
            "Required progression milestone reached.")));

        var attempts = await _dbContext.AssessmentAttempts
            .AsNoTracking()
            .Include(attempt => attempt.CourseAssessment)
                .ThenInclude(assessment => assessment!.Course)
            .Where(attempt => attempt.UserAccountId == learnerUserId)
            .ToListAsync();

        events.AddRange(attempts.Select(attempt => new LearnerLogItem(
            attempt.SubmittedAt,
            "assessment",
            $"Assessment attempt: {attempt.CourseAssessment?.Course?.Title ?? attempt.CourseAssessment?.Title ?? "Assessment"}",
            $"Score={attempt.ScorePercent:0.##}; Passed={attempt.Passed}; Attempt={attempt.AttemptNumber}")));

        var retakeGrants = await _dbContext.RetakeGrants
            .AsNoTracking()
            .Include(grant => grant.CourseAssessment)
                .ThenInclude(assessment => assessment!.Course)
            .Where(grant => grant.UserAccountId == learnerUserId)
            .ToListAsync();

        events.AddRange(retakeGrants.Select(grant => new LearnerLogItem(
            grant.GrantedAt,
            "retake",
            $"Retake granted: {grant.CourseAssessment?.Course?.Title ?? grant.CourseAssessment?.Title ?? "Assessment"}",
            $"GrantedAttempts={grant.GrantedAttempts}")));

        var certificates = await _dbContext.CompletionCertificates
            .AsNoTracking()
            .Include(certificate => certificate.Course)
            .Where(certificate => certificate.UserAccountId == learnerUserId)
            .ToListAsync();

        events.AddRange(certificates.Select(certificate => new LearnerLogItem(
            certificate.IssuedAt,
            "certificate",
            $"Certificate issued: {certificate.Course?.Title ?? "Unknown Course"}",
            $"Certificate={certificate.CertificateNumber}; Status={GetCertificateStatus(certificate, DateTime.UtcNow)}")));

        events.AddRange(certificates
            .Where(certificate => certificate.RevokedAt.HasValue)
            .Select(certificate => new LearnerLogItem(
                certificate.RevokedAt!.Value,
                "certificate",
                $"Certificate revoked: {certificate.Course?.Title ?? "Unknown Course"}",
                certificate.RevocationReason ?? "Revoked")));

        return events
            .OrderByDescending(existing => existing.TimestampUtc)
            .ToList();
    }

    public async Task<List<BrokerEnrollmentReportRow>> GetBrokerEnrollmentReportAsync(Guid brokerUserId, DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        var assignedLearnerIds = await GetVisibleLearnerIdsAsync();

        var query = _dbContext.Enrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.UserAccount)
            .Include(enrollment => enrollment.Course)
            .Where(enrollment => assignedLearnerIds.Contains(enrollment.UserAccountId));

        if (fromUtc.HasValue)
        {
            query = query.Where(enrollment => enrollment.EnrolledAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(enrollment => enrollment.EnrolledAt <= toUtc.Value);
        }

        var rows = await query
            .OrderByDescending(enrollment => enrollment.EnrolledAt)
            .ToListAsync();

        return rows
            .Select(enrollment => new BrokerEnrollmentReportRow(
                enrollment.UserAccount?.DisplayName ?? "Unknown Learner",
                enrollment.UserAccount?.Email ?? "unknown@lms.local",
                enrollment.Course?.Title ?? "Unknown Course",
                enrollment.EnrolledAt,
                enrollment.ProgressPercent,
                enrollment.Completed))
            .ToList();
    }

    private async Task RecalculateEnrollmentProgressAsync(Guid userId, Guid courseId, Enrollment enrollment)
    {
        var courseLessons = await _dbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.Module!.CourseId == courseId)
            .Select(lesson => new { lesson.Id, lesson.IsRequired })
            .ToListAsync();

        if (courseLessons.Count == 0)
        {
            enrollment.ProgressPercent = 0;
            enrollment.Completed = false;
            await _dbContext.SaveChangesAsync();
            return;
        }

        var requiredLessons = courseLessons.Where(lesson => lesson.IsRequired).ToList();
        var progressScope = requiredLessons.Count > 0 ? requiredLessons : courseLessons;

        var completedIds = await _dbContext.LessonProgresses
            .AsNoTracking()
            .Where(progress => progress.UserAccountId == userId && progress.Completed)
            .Select(progress => progress.LessonId)
            .ToListAsync();

        var completedCount = progressScope.Count(lesson => completedIds.Contains(lesson.Id));
        var percent = Math.Round(100m * completedCount / progressScope.Count, 2, MidpointRounding.AwayFromZero);

        enrollment.ProgressPercent = percent;
        enrollment.Completed = percent >= 100m;
        await _dbContext.SaveChangesAsync();

        var actorEmail = await _dbContext.UserAccounts
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        await EnsureCertificateStateAsync(userId, enrollment, actorEmail);

        await _auditLogService.WriteAsync(userId, actorEmail, "enrollment.progress.recalculated", "Enrollment", enrollment.Id, $"Progress={percent:0.##}");
    }

    private async Task EnsureCertificateStateAsync(Guid actorUserId, Enrollment enrollment, string? actorEmail)
    {
        var certificate = await _dbContext.CompletionCertificates
            .FirstOrDefaultAsync(existing => existing.EnrollmentId == enrollment.Id);

        var hasPassedRequiredAssessment = await _assessmentService.HasPassedRequiredAssessmentAsync(enrollment.CourseId, enrollment.UserAccountId);
        var certificateEligible = enrollment.Completed || hasPassedRequiredAssessment;

        if (certificateEligible)
        {
            if (certificate is null)
            {
                var issuedAt = DateTime.UtcNow;
                certificate = new CompletionCertificate
                {
                    UserAccountId = enrollment.UserAccountId,
                    CourseId = enrollment.CourseId,
                    EnrollmentId = enrollment.Id,
                    CertificateNumber = GenerateCertificateNumber(enrollment.CourseId, enrollment.UserAccountId),
                    VerificationCode = string.Empty,
                    IssuedAt = issuedAt,
                    ExpiresAt = issuedAt.AddYears(1),
                    IsRevoked = false,
                    RevokedAt = null,
                    RevocationReason = null
                };

                certificate.VerificationCode = GenerateVerificationCode(certificate.CertificateNumber, certificate.UserAccountId, certificate.CourseId, issuedAt);

                _dbContext.CompletionCertificates.Add(certificate);
                await _dbContext.SaveChangesAsync();
                await _auditLogService.WriteAsync(actorUserId, actorEmail, "certificate.issued", "CompletionCertificate", certificate.Id, certificate.CertificateNumber);
                return;
            }

            if (certificate.IsRevoked)
            {
                var reissuedAt = DateTime.UtcNow;
                certificate.IsRevoked = false;
                certificate.RevokedAt = null;
                certificate.RevocationReason = null;
                certificate.IssuedAt = reissuedAt;
                certificate.ExpiresAt = reissuedAt.AddYears(1);
                certificate.VerificationCode = GenerateVerificationCode(certificate.CertificateNumber, certificate.UserAccountId, certificate.CourseId, reissuedAt);
                await _dbContext.SaveChangesAsync();
                await _auditLogService.WriteAsync(actorUserId, actorEmail, "certificate.reissued", "CompletionCertificate", certificate.Id, certificate.CertificateNumber);
            }

            return;
        }

        if (certificate is not null && !certificate.IsRevoked)
        {
            certificate.IsRevoked = true;
            certificate.RevokedAt = DateTime.UtcNow;
            certificate.RevocationReason = "Enrollment dropped below completion threshold.";
            await _dbContext.SaveChangesAsync();
            await _auditLogService.WriteAsync(actorUserId, actorEmail, "certificate.revoked", "CompletionCertificate", certificate.Id, certificate.CertificateNumber);
        }
    }

    private static string GenerateCertificateNumber(Guid courseId, Guid userId)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var coursePart = courseId.ToString("N")[..6].ToUpperInvariant();
        var userPart = userId.ToString("N")[..6].ToUpperInvariant();
        return $"CERT-{stamp}-{coursePart}-{userPart}";
    }

    private static string GenerateVerificationCode(string certificateNumber, Guid userId, Guid courseId, DateTime issuedAt)
    {
        var source = $"{certificateNumber}|{userId:N}|{courseId:N}|{issuedAt:O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes);
    }


    private static string GetCertificateStatus(CompletionCertificate certificate, DateTime nowUtc)
    {
        if (certificate.IsRevoked)
        {
            return "Revoked";
        }

        if (certificate.ExpiresAt <= nowUtc)
        {
            return "Expired";
        }

        if (certificate.ExpiresAt <= nowUtc.AddDays(30))
        {
            return "Expiring Soon";
        }

        return "Active";
    }

    private async Task<bool> CanBrokerManageLearnerAsync(Guid brokerUserId, Guid learnerUserId)
    {
        var brokerValid = await _dbContext.UserAccounts
            .AsNoTracking()
            .AnyAsync(user => user.Id == brokerUserId && user.Role == "Broker" && user.IsActive);

        if (!brokerValid)
        {
            return false;
        }

        return await _dbContext.UserAccounts
            .AsNoTracking()
            .AnyAsync(user => user.Id == learnerUserId && user.Role == "Learner" && user.IsActive);
    }

    private async Task<bool> CanBrokerUseCourseAsync(Guid brokerUserId, Guid courseId)
    {
        return await _dbContext.Enrollments
            .AsNoTracking()
            .AnyAsync(enrollment =>
                enrollment.UserAccountId == brokerUserId &&
                enrollment.CourseId == courseId &&
                enrollment.EnrollmentSource == LearnerPurchaseSource);
    }

    private async Task<List<Guid>> GetVisibleLearnerIdsAsync()
    {
        return await _dbContext.UserAccounts
            .AsNoTracking()
            .Where(user => user.Role == "Learner" && user.IsActive)
            .Select(user => user.Id)
            .ToListAsync();
    }

    private static bool IsBrokerSponsoredEnrollment(Enrollment enrollment, Guid brokerUserId)
    {
        return enrollment.EnrollmentSource == BrokerSponsoredSource && enrollment.SponsoredByBrokerUserId == brokerUserId;
    }

    private async Task CreateNotificationAsync(Guid recipientUserId, string category, string title, string message)
    {
        _dbContext.SystemNotifications.Add(new SystemNotification
        {
            RecipientUserId = recipientUserId,
            Category = category,
            Title = title,
            Message = message,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }
}
