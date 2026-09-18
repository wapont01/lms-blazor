using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IRegulatoryComplianceService
{
    Task<bool> LinkUserToApprovedLicenseAsync(Guid userId, Guid regulatoryLicenseeId, Guid actorUserId, string actorEmail);
    Task<RegulatoryLicenseComplianceStatus?> GetUserComplianceStatusAsync(Guid userId, DateOnly? today = null);
}

public sealed record RegulatoryLicenseComplianceStatus(
    string State,
    string LicenseNumber,
    string LicenseeName,
    string? LicenseLevel,
    string? LicenseStatus,
    DateOnly? RenewalDate,
    int? UpdateHours,
    int? ElectiveHours,
    int? PostlicensingHours,
    DateTime SnapshotRetrievedAtUtc,
    IReadOnlyList<string> RecommendedCourseMessages);

public sealed class RegulatoryComplianceService : IRegulatoryComplianceService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public RegulatoryComplianceService(ApplicationDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<bool> LinkUserToApprovedLicenseAsync(Guid userId, Guid regulatoryLicenseeId, Guid actorUserId, string actorEmail)
    {
        var licensee = await _dbContext.RegulatoryLicensees
            .Include(candidate => candidate.Snapshot)
            .FirstOrDefaultAsync(candidate => candidate.Id == regulatoryLicenseeId);

        if (licensee?.Snapshot?.Status != RegulatorySnapshotStatuses.Approved || !await _dbContext.UserAccounts.AnyAsync(user => user.Id == userId))
        {
            return false;
        }

        var existing = await _dbContext.UserRegulatoryLicenseLinks.SingleOrDefaultAsync(link => link.UserAccountId == userId);
        if (existing is null)
        {
            _dbContext.UserRegulatoryLicenseLinks.Add(new UserRegulatoryLicenseLink
            {
                UserAccountId = userId,
                RegulatorySnapshotId = licensee.SnapshotId,
                RegulatoryLicenseeId = licensee.Id,
                VerifiedAtUtc = DateTime.UtcNow,
                VerifiedByUserId = actorUserId
            });
        }
        else
        {
            existing.RegulatorySnapshotId = licensee.SnapshotId;
            existing.RegulatoryLicenseeId = licensee.Id;
            existing.VerifiedAtUtc = DateTime.UtcNow;
            existing.VerifiedByUserId = actorUserId;
        }

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "regulatory-license.linked", "UserAccount", userId, $"License={licensee.LicenseNumber}; Snapshot={licensee.SnapshotId}");
        return true;
    }

    public async Task<RegulatoryLicenseComplianceStatus?> GetUserComplianceStatusAsync(Guid userId, DateOnly? today = null)
    {
        var link = await _dbContext.UserRegulatoryLicenseLinks
            .AsNoTracking()
            .Include(candidate => candidate.RegulatoryLicensee)
            .Include(candidate => candidate.RegulatorySnapshot)
            .SingleOrDefaultAsync(candidate => candidate.UserAccountId == userId);

        if (link?.RegulatoryLicensee is null || link.RegulatorySnapshot?.Status != RegulatorySnapshotStatuses.Approved)
        {
            return null;
        }

        var licensee = link.RegulatoryLicensee;
        var effectiveDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var messages = await _dbContext.CourseEligibilityPolicies
            .AsNoTracking()
            .Where(policy => policy.IsActive &&
                policy.State == licensee.State &&
                policy.ComplianceType != CourseComplianceTypes.Prelicensing &&
                (policy.LicenseLevel == null || policy.LicenseLevel == licensee.LicenseLevel) &&
                policy.EffectiveFrom <= effectiveDate &&
                (policy.EffectiveThrough == null || policy.EffectiveThrough >= effectiveDate))
            .OrderBy(policy => policy.ComplianceType)
            .Select(policy => policy.EligibilityMessage)
            .ToListAsync();

        return new RegulatoryLicenseComplianceStatus(
            licensee.State,
            licensee.LicenseNumber,
            licensee.Name,
            licensee.LicenseLevel,
            licensee.LicenseStatus,
            licensee.LicenseRenewalDate,
            licensee.UpdateHours,
            licensee.ElectiveHours,
            licensee.PostlicensingHours,
            link.RegulatorySnapshot.RetrievedAtUtc,
            messages);
    }
}