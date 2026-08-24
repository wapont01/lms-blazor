using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface ISchoolProfileService
{
    Task<SchoolProfile> GetAsync();
    Task<SchoolProfile> UpdateAsync(SchoolProfile input, Guid actorUserId, string actorEmail);
    Task<SchoolStaffMember> AddStaffMemberAsync(SchoolStaffMember input, Guid actorUserId, string actorEmail);
    Task RemoveStaffMemberAsync(Guid staffMemberId, Guid actorUserId, string actorEmail);
}

public sealed class SchoolProfileService : ISchoolProfileService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public SchoolProfileService(ApplicationDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<SchoolProfile> GetAsync()
    {
        var profile = await _dbContext.SchoolProfiles
            .Include(existing => existing.StaffMembers.OrderBy(member => member.Role).ThenBy(member => member.Name))
            .OrderBy(existing => existing.UpdatedAtUtc)
            .FirstOrDefaultAsync();
        if (profile is not null)
        {
            return profile;
        }

        profile = new SchoolProfile();
        _dbContext.SchoolProfiles.Add(profile);
        profile.StaffMembers.Add(new SchoolStaffMember
        {
            SchoolProfileId = profile.Id,
            Name = "William A. Aponte",
            Role = SchoolStaffRoles.Instructor,
            Title = "Primary Instructor",
            Email = profile.PrimaryInstructorEmail,
            Telephone = profile.PrimaryInstructorTelephone
        });
        profile.StaffMembers.Add(new SchoolStaffMember
        {
            SchoolProfileId = profile.Id,
            Name = "Clara M. Aponte",
            Role = SchoolStaffRoles.Instructor,
            Title = "Instructor",
            Email = "waponte2003@yahoo.com",
            Telephone = "7049145824"
        });
        await _dbContext.SaveChangesAsync();
        return profile;
    }

    public async Task<SchoolStaffMember> AddStaffMemberAsync(SchoolStaffMember input, Guid actorUserId, string actorEmail)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new InvalidOperationException("Enter the staff member's name.");
        }

        var role = SchoolStaffRoles.All.SingleOrDefault(existing => string.Equals(existing, input.Role?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Select Instructor or Officer as the staff role.");
        var profile = await GetAsync();
        var member = new SchoolStaffMember
        {
            SchoolProfileId = profile.Id,
            Name = input.Name.Trim(),
            Role = role,
            Title = NormalizeOptional(input.Title),
            LicenseNumber = NormalizeOptional(input.LicenseNumber),
            Email = NormalizeOptional(input.Email),
            Telephone = NormalizeOptional(input.Telephone)
        };
        _dbContext.SchoolStaffMembers.Add(member);
        profile.UpdatedAtUtc = DateTime.UtcNow;
        profile.UpdatedByUserId = actorUserId;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "school-staff.added", "SchoolStaffMember", member.Id, $"Role={member.Role}; Name={member.Name}");
        return member;
    }

    public async Task RemoveStaffMemberAsync(Guid staffMemberId, Guid actorUserId, string actorEmail)
    {
        var member = await _dbContext.SchoolStaffMembers.SingleOrDefaultAsync(existing => existing.Id == staffMemberId)
            ?? throw new InvalidOperationException("The selected staff member no longer exists.");
        _dbContext.SchoolStaffMembers.Remove(member);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "school-staff.removed", "SchoolStaffMember", staffMemberId, $"Role={member.Role}; Name={member.Name}");
    }

    public async Task<SchoolProfile> UpdateAsync(SchoolProfile input, Guid actorUserId, string actorEmail)
    {
        Validate(input);
        var profile = await GetAsync();
        profile.LegalName = input.LegalName.Trim();
        profile.AdvertisedName = input.AdvertisedName.Trim();
        profile.StreetAddress = input.StreetAddress.Trim();
        profile.City = input.City.Trim();
        profile.State = input.State.Trim().ToUpperInvariant();
        profile.PostalCode = input.PostalCode.Trim();
        profile.EducationDirectorName = input.EducationDirectorName.Trim();
        profile.CorporateOfficerName = input.CorporateOfficerName.Trim();
        profile.PrimaryInstructorName = input.PrimaryInstructorName.Trim();
        profile.PrimaryInstructorEmail = input.PrimaryInstructorEmail.Trim();
        profile.PrimaryInstructorTelephone = input.PrimaryInstructorTelephone.Trim();
        profile.ProviderLicenseNumber = NormalizeOptional(input.ProviderLicenseNumber);
        profile.InstructorLicenseNumber = NormalizeOptional(input.InstructorLicenseNumber);
        profile.SupportEmail = input.SupportEmail.Trim();
        profile.SupportTelephone = input.SupportTelephone.Trim();
        profile.SupportHours = NormalizeOptional(input.SupportHours);
        profile.WebsiteUrl = NormalizeOptional(input.WebsiteUrl);
        profile.LicenseExaminationPerformanceRecord = input.LicenseExaminationPerformanceRecord.Trim();
        profile.AnnualSummaryReportData = input.AnnualSummaryReportData.Trim();
        profile.UpdatedAtUtc = DateTime.UtcNow;
        profile.UpdatedByUserId = actorUserId;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "school-profile.updated", "SchoolProfile", profile.Id, $"LegalName={profile.LegalName}");
        return profile;
    }

    private static void Validate(SchoolProfile input)
    {
        var requiredValues = new[]
        {
            input.LegalName,
            input.AdvertisedName,
            input.StreetAddress,
            input.City,
            input.State,
            input.PostalCode,
            input.EducationDirectorName,
            input.CorporateOfficerName,
            input.PrimaryInstructorName,
            input.PrimaryInstructorEmail,
            input.PrimaryInstructorTelephone,
            input.SupportEmail,
            input.SupportTelephone,
            input.LicenseExaminationPerformanceRecord,
            input.AnnualSummaryReportData
        };
        if (requiredValues.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Complete all required school profile fields before saving.");
        }

        if (input.State.Trim().Length != 2)
        {
            throw new InvalidOperationException("State must be a two-letter abbreviation.");
        }
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}