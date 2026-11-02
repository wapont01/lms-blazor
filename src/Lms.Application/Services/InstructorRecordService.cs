using System.Security.Claims;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IInstructorRecordIdentity
{
    Task<ClaimsPrincipal> GetUserAsync();
}

public sealed record InstructorAccountRecord(Guid Id, string Name, string? LegalName,
    string Email, string? Phone, string? State, string? LicenseNumber, string? LicenseType,
    string? LicenseStatus, DateTime? InitialLicensureDate, DateTime? ContinuingEducationDueDate,
    DateTime? LicenseRenewalDueDate, bool IsActive)
{
    public IReadOnlyList<InstructorLicenseRecord> LicenseRecords { get; init; } = [];
}

public sealed record InstructorLicenseRecord(string State, string LicenseNumber, string? LicenseType,
    string? LicenseStatus, string AssociationStatus);

public sealed record InstructorFacultyRecord(Guid Id, Guid? UserAccountId, string Name,
    string? Title, string State, string? Email, string? Phone, string? LicenseNumber,
    DateTime? HireReportedToCommissionAtUtc, DateTime? LastObservationAtUtc);

public sealed record InstructorRecords(bool IsAdmin, IReadOnlyList<InstructorAccountRecord> Accounts,
    IReadOnlyList<InstructorFacultyRecord> Faculty);

public sealed class InstructorRecordService(ApplicationDbContext db, IInstructorRecordIdentity identity,
    IAuditLogService audit)
{
    public async Task<InstructorRecords> GetAsync()
    {
        var (userId, admin, _) = await AuthorizeAsync();
        var accounts = InstructorAccess.Accounts(db).AsNoTracking();
        var faculty = db.SchoolStaffMembers.AsNoTracking().Where(m => m.Role == SchoolStaffRoles.Instructor);
        if (!admin)
        {
            accounts = accounts.Where(u => u.Id == userId);
            faculty = faculty.Where(m => m.UserAccountId == userId);
        }
        var profiles = await accounts.OrderBy(u => u.DisplayName).Select(u =>
            new InstructorAccountRecord(u.Id, u.DisplayName, u.LegalName, u.Email, u.PhoneNumber,
                u.State, u.LicenseNumber, u.LicenseType, u.LicenseStatus, u.InitialLicensureDate,
                u.ContinuingEducationDueDate, u.LicenseRenewalDueDate, u.IsActive)).ToListAsync();
        var accountIds = profiles.Select(profile => profile.Id).ToArray();
        var selections = await db.RosterAccountRequests.AsNoTracking()
            .Where(request => accountIds.Contains(request.UserAccountId) && request.RegulatoryLicensee != null)
            .OrderBy(request => request.RegulatoryLicensee!.State).ThenBy(request => request.RegulatoryLicensee!.LicenseNumber)
            .Select(request => new
            {
                request.UserAccountId,
                License = new InstructorLicenseRecord(request.RegulatoryLicensee!.State,
                    request.RegulatoryLicensee.LicenseNumber, request.RegulatoryLicensee.LicenseType,
                    request.RegulatoryLicensee.LicenseStatus, request.Status)
            }).ToListAsync();
        profiles = profiles.Select(profile => profile with
        {
            LicenseRecords = selections.Where(selection => selection.UserAccountId == profile.Id)
                .Select(selection => selection.License).ToList()
        }).ToList();
        var members = await faculty.OrderBy(m => m.Name).ThenBy(m => m.SchoolProfile!.State).Select(m =>
            new InstructorFacultyRecord(m.Id, m.UserAccountId, m.Name, m.Title, m.SchoolProfile!.State,
                m.Email, m.Telephone, m.LicenseNumber, m.HireReportedToCommissionAtUtc,
                m.LastObservationAtUtc)).ToListAsync();
        return new(admin, profiles, members);
    }

    public async Task LinkFacultyAsync(Guid facultyId, Guid? instructorId)
    {
        var (actorId, admin, email) = await AuthorizeAsync();
        if (!admin) throw new UnauthorizedAccessException("Only administrators can assign instructor record ownership.");
        var member = await db.SchoolStaffMembers.SingleOrDefaultAsync(m =>
            m.Id == facultyId && m.Role == SchoolStaffRoles.Instructor)
            ?? throw new InvalidOperationException("Instructor faculty record was not found.");
        if (instructorId.HasValue && !await InstructorAccess.Accounts(db).AnyAsync(u =>
            u.Id == instructorId.Value))
            throw new InvalidOperationException("Choose an instructor LMS account.");
        var previousId = member.UserAccountId;
        if (previousId == instructorId) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        member.UserAccountId = instructorId;
        await db.SaveChangesAsync();
        await audit.WriteAsync(actorId, email, "instructor-record.owner-changed", "SchoolStaffMember", member.Id,
            $"PreviousUserId={previousId}; UserId={instructorId}");
        await transaction.CommitAsync();
    }

    private async Task<(Guid UserId, bool Admin, string Email)> AuthorizeAsync()
    {
        var principal = await identity.GetUserAsync();
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            throw new UnauthorizedAccessException("Sign in to view instructor records.");
        var user = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive);
        if (user is null || !principal.IsInRole(user.Role)
            || (user.Role != "Admin" && (!principal.IsInRole("Instructor")
                || !await InstructorAccess.Accounts(db).AnyAsync(account => account.Id == id))))
            throw new UnauthorizedAccessException("Only active administrators and instructors can view instructor records.");
        return (id, user.Role == "Admin", user.Email);
    }
}
