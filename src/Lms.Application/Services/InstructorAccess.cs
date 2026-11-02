using Lms.Application.Data;
using Lms.Domain.Entities;

namespace Lms.Application.Services;

public static class InstructorAccess
{
    public static IQueryable<UserAccount> Accounts(ApplicationDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eligible = db.RegulatoryLicensees.Where(record =>
            record.Snapshot != null
            && record.Snapshot.Status == RegulatorySnapshotStatuses.Approved
            && record.Snapshot.State == record.State
            && record.SnapshotId == db.RegulatoryDataSnapshots
                .Where(snapshot => snapshot.State == record.State
                    && snapshot.Status == RegulatorySnapshotStatuses.Approved)
                .OrderByDescending(snapshot => snapshot.State == "Florida" || snapshot.State == "Texas" || snapshot.State == "California"
                    ? snapshot.ActivatedAtUtc ?? snapshot.RetrievedAtUtc : snapshot.RetrievedAtUtc)
                .ThenByDescending(snapshot => snapshot.ImportedAtUtc).ThenBy(snapshot => snapshot.Id)
                .Select(snapshot => snapshot.Id).First()
            && !record.IsRestricted
            && record.LicenseNumber.Trim() != ""
            && record.LicenseType != null
            && (record.LicenseType.Trim().ToUpper() == "REAL ESTATE INSTRUCTOR"
                || record.LicenseType.Trim().ToUpper() == "INSTRUCTOR"
                || (record.State == "Florida" && record.LicenseType.Trim().ToUpper() == "ZH"))
            && record.LicenseStatus != null
            && (record.LicenseStatus.Trim().ToUpper() == "ACTIVE"
                || record.LicenseStatus.Replace(" ", "").ToUpper() == "CURRENT,ACTIVE")
            && (!record.LicenseRenewalDate.HasValue || record.LicenseRenewalDate >= today));
        var licenseOwners = db.RosterAccountRequests.Where(request =>
            request.Status == "Confirmed" && request.RegulatoryLicensee != null
            && request.RegulatoryLicensee.Snapshot != null
            && request.RegulatoryLicensee.Snapshot.Status == RegulatorySnapshotStatuses.Approved
            && request.RegulatoryLicensee.Snapshot.State == request.RegulatoryLicensee.State
            && request.RegulatoryLicensee.LicenseType != null
            && (request.RegulatoryLicensee.LicenseType.Trim().ToUpper() == "REAL ESTATE INSTRUCTOR"
                || request.RegulatoryLicensee.LicenseType.Trim().ToUpper() == "INSTRUCTOR"
                || (request.RegulatoryLicensee.State == "Florida"
                    && request.RegulatoryLicensee.LicenseType.Trim().ToUpper() == "ZH"))
            && eligible.Any(record => record.Id == request.RegulatoryLicenseeId
                || (record.State == "Florida" && request.RegulatoryLicensee.State == "Florida"
                    && record.LicenseNumber == request.RegulatoryLicensee.LicenseNumber)))
            .Select(request => request.UserAccountId);

        return db.UserAccounts.Where(user => user.Role == "Instructor"
            || ((user.Role == "Broker" || user.Role == "Learner")
                && user.EmailVerifiedAtUtc != null && licenseOwners.Contains(user.Id)));
    }
}
