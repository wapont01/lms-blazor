using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Lms.Application.Services;

public interface ISchoolProfileService
{
    Task<SchoolProfile> GetAsync();
    Task<SchoolProfile> GetForStateAsync(string state);
    Task<SchoolProfile> UpdateAsync(SchoolProfile input, Guid actorUserId, string actorEmail);
    Task<SchoolProfile> UpdateForStateAsync(SchoolProfile input, Guid actorUserId, string actorEmail);
    Task<SchoolStaffMember> AddStaffMemberAsync(SchoolStaffMember input, Guid actorUserId, string actorEmail);
    Task RemoveStaffMemberAsync(Guid staffMemberId, Guid actorUserId, string actorEmail);
}

public sealed class SchoolProfileService : ISchoolProfileService
{
    private const string TemporaryInstructorName = "Susan (Jill) Malloy";
    private const string TemporaryInstructorLicenseNumber = "2277";

    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public SchoolProfileService(ApplicationDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<SchoolProfile> GetAsync()
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE SchoolStaffMembers SET Id = UPPER(Id) WHERE Id <> UPPER(Id)");

        var profile = await _dbContext.SchoolProfiles
            .Include(existing => existing.StaffMembers.OrderBy(member => member.Role).ThenBy(member => member.Name))
            .OrderBy(existing => existing.UpdatedAtUtc)
            .FirstOrDefaultAsync();
        if (profile is not null)
        {
            var hasChanges = false;
            if (profile.StreetAddress == "17214 Belmont Stakes Lane"
                && profile.City == "Charlotte"
                && profile.AddressState.Equals("NC", StringComparison.OrdinalIgnoreCase)
                && profile.PostalCode == "28278-8147")
            {
                profile.StreetAddress = SchoolProfile.FloridaSchoolStreetAddress;
                profile.City = SchoolProfile.FloridaSchoolCity;
                profile.AddressState = SchoolProfile.FloridaSchoolState;
                profile.PostalCode = SchoolProfile.FloridaSchoolPostalCode;
                hasChanges = true;
            }

            if (string.IsNullOrWhiteSpace(profile.ProviderLicenseNumber)
                || string.Equals(profile.ProviderLicenseNumber, "C42007", StringComparison.OrdinalIgnoreCase))
            {
                profile.ProviderLicenseNumber = SchoolProfile.TemporaryProviderLicenseNumber;
                hasChanges = true;
            }

            var temporaryInstructor = profile.StaffMembers.FirstOrDefault(member =>
                string.Equals(member.Name, TemporaryInstructorName, StringComparison.OrdinalIgnoreCase));
            if (temporaryInstructor is null)
            {
                var newTemporaryInstructor = CreateTemporaryInstructor(profile.Id);
                profile.StaffMembers.Add(newTemporaryInstructor);
                _dbContext.SchoolStaffMembers.Add(newTemporaryInstructor);
                hasChanges = true;
            }
            else
            {
                if (temporaryInstructor.Role != SchoolStaffRoles.Instructor)
                {
                    temporaryInstructor.Role = SchoolStaffRoles.Instructor;
                    hasChanges = true;
                }
                if (temporaryInstructor.Title != "Instructor")
                {
                    temporaryInstructor.Title = "Instructor";
                    hasChanges = true;
                }
                if (temporaryInstructor.LicenseNumber != TemporaryInstructorLicenseNumber)
                {
                    temporaryInstructor.LicenseNumber = TemporaryInstructorLicenseNumber;
                    hasChanges = true;
                }
            }

            hasChanges |= await ReconcileInstructorAccountsAsync(profile);

            if (profile.State.Equals(SchoolProfile.FloridaSchoolState, StringComparison.OrdinalIgnoreCase))
            {
                var william = profile.StaffMembers.FirstOrDefault(member =>
                    string.Equals(member.Name, "William A. Aponte", StringComparison.OrdinalIgnoreCase));
                if (william is not null)
                {
                    hasChanges |= SetIfChanged(william.LicenseNumber, "BK3059873", value => william.LicenseNumber = value);
                    hasChanges |= SetIfChanged(william.Title, "Education Director / Instructor", value => william.Title = value);
                    hasChanges |= SetIfChanged(profile.InstructorLicenseNumber, "BK3059873", value => profile.InstructorLicenseNumber = value);
                }

                var milena = profile.StaffMembers.FirstOrDefault(member =>
                    string.Equals(member.Name, "Milena", StringComparison.OrdinalIgnoreCase));
                if (milena is null)
                {
                    profile.StaffMembers.Add(new SchoolStaffMember
                    {
                        SchoolProfileId = profile.Id,
                        Name = "Milena",
                        Role = SchoolStaffRoles.Instructor,
                        Title = "Instructor",
                        LicenseNumber = "FL-INSTRUCTOR-PLACEHOLDER"
                    });
                    hasChanges = true;
                }
                else
                {
                    hasChanges |= SetIfChanged(milena.Role, SchoolStaffRoles.Instructor, value => milena.Role = value);
                    hasChanges |= SetIfChanged(milena.Title, "Instructor", value => milena.Title = value);
                    hasChanges |= SetIfChanged(milena.LicenseNumber, "FL-INSTRUCTOR-PLACEHOLDER", value => milena.LicenseNumber = value);
                }

                var nonFloridaStaff = profile.StaffMembers
                    .Where(member => !string.Equals(member.Name, "William A. Aponte", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(member.Name, "Milena", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (nonFloridaStaff.Count > 0)
                {
                    _dbContext.SchoolStaffMembers.RemoveRange(nonFloridaStaff);
                    hasChanges = true;
                }
            }

            foreach (var official in profile.StaffMembers.Where(member => member.Role == SchoolStaffRoles.Officer))
            {
                hasChanges |= SetIfChanged(official.Title, "Full-Time Official", value => official.Title = value);
            }

            if (hasChanges)
            {
                await _dbContext.SaveChangesAsync();
            }

            return profile;
        }

        profile = new SchoolProfile();
        profile.InstructorLicenseNumber = "BK3059873";
        _dbContext.SchoolProfiles.Add(profile);
        profile.StaffMembers.Add(new SchoolStaffMember
        {
            SchoolProfileId = profile.Id,
            Name = "William A. Aponte",
            Role = SchoolStaffRoles.Instructor,
            Title = "Education Director / Instructor",
            LicenseNumber = "BK3059873",
            Email = profile.PrimaryInstructorEmail,
            Telephone = profile.PrimaryInstructorTelephone
        });
        profile.StaffMembers.Add(new SchoolStaffMember
        {
            SchoolProfileId = profile.Id,
            Name = "Milena",
            Role = SchoolStaffRoles.Instructor,
            Title = "Instructor",
            LicenseNumber = "FL-INSTRUCTOR-PLACEHOLDER"
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
        profile.StaffMembers.Add(CreateTemporaryInstructor(profile.Id));
        await ReconcileInstructorAccountsAsync(profile);
        await _dbContext.SaveChangesAsync();
        return profile;
    }

    public async Task<SchoolProfile> GetForStateAsync(string state)
    {
        var normalizedState = string.Equals(state, "North Carolina", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "NC", StringComparison.OrdinalIgnoreCase)
            ? "North Carolina"
            : "Florida";
        var profile = await _dbContext.SchoolProfiles
            .Include(existing => existing.StaffMembers)
            .FirstOrDefaultAsync(existing => existing.State == normalizedState);
        if (profile is not null)
        {
            return profile;
        }

        var source = await GetAsync();
        profile = new SchoolProfile
        {
            State = normalizedState,
            LegalName = source.LegalName,
            AdvertisedName = source.AdvertisedName,
            StreetAddress = normalizedState == "Florida" ? SchoolProfile.FloridaSchoolStreetAddress : "17214 Belmont Stakes Lane",
            City = normalizedState == "Florida" ? SchoolProfile.FloridaSchoolCity : "Charlotte",
            AddressState = normalizedState == "Florida" ? SchoolProfile.FloridaSchoolState : "NC",
            PostalCode = normalizedState == "Florida" ? SchoolProfile.FloridaSchoolPostalCode : "28278-8147",
            EducationDirectorName = source.EducationDirectorName,
            CorporateOfficerName = source.CorporateOfficerName,
            PrimaryInstructorName = source.PrimaryInstructorName,
            PrimaryInstructorEmail = source.PrimaryInstructorEmail,
            PrimaryInstructorTelephone = source.PrimaryInstructorTelephone,
            ProviderLicenseNumber = source.ProviderLicenseNumber,
            InstructorLicenseNumber = normalizedState == "Florida" ? "BK3059873" : source.InstructorLicenseNumber,
            SupportEmail = source.SupportEmail,
            SupportTelephone = source.SupportTelephone,
            SupportHours = source.SupportHours,
            WebsiteUrl = source.WebsiteUrl,
            LicenseExaminationPerformanceRecord = source.LicenseExaminationPerformanceRecord,
            AnnualSummaryReportData = source.AnnualSummaryReportData
        };
        foreach (var staffMember in source.StaffMembers)
        {
            profile.StaffMembers.Add(new SchoolStaffMember
            {
                SchoolProfileId = profile.Id,
                Name = staffMember.Name,
                Role = staffMember.Role,
                Title = staffMember.Title,
                LicenseNumber = staffMember.LicenseNumber,
                Email = staffMember.Email,
                Telephone = staffMember.Telephone
            });
        }
        _dbContext.SchoolProfiles.Add(profile);
        await _dbContext.SaveChangesAsync();
        return profile;
    }

    private static SchoolStaffMember CreateTemporaryInstructor(Guid schoolProfileId) => new()
    {
        SchoolProfileId = schoolProfileId,
        Name = TemporaryInstructorName,
        Role = SchoolStaffRoles.Instructor,
        Title = "Instructor",
        LicenseNumber = TemporaryInstructorLicenseNumber
    };

    private async Task<bool> ReconcileInstructorAccountsAsync(SchoolProfile profile)
    {
        var instructorAccounts = await _dbContext.UserAccounts
            .AsNoTracking()
            .Where(user => user.Role == SchoolStaffRoles.Instructor && user.IsActive)
            .OrderBy(user => user.Email)
            .ToListAsync();
        var hasChanges = false;

        foreach (var account in instructorAccounts)
        {
            var member = profile.StaffMembers.FirstOrDefault(existing =>
                    string.Equals(existing.Email, account.Email, StringComparison.OrdinalIgnoreCase))
                ?? profile.StaffMembers.FirstOrDefault(existing =>
                    string.Equals(existing.Name, account.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (member is null)
            {
                member = new SchoolStaffMember
                {
                    SchoolProfileId = profile.Id,
                    Name = account.DisplayName,
                    Role = SchoolStaffRoles.Instructor,
                    Title = "Instructor"
                };
                profile.StaffMembers.Add(member);
                _dbContext.SchoolStaffMembers.Add(member);
                hasChanges = true;
            }

            hasChanges |= SetRequiredIfChanged(member.Name, account.DisplayName, value => member.Name = value);
            hasChanges |= SetRequiredIfChanged(member.Role, SchoolStaffRoles.Instructor, value => member.Role = value);
            hasChanges |= SetIfChanged(member.Title, "Instructor", value => member.Title = value);
            hasChanges |= SetIfChanged(member.LicenseNumber, account.LicenseNumber, value => member.LicenseNumber = value);
            hasChanges |= SetIfChanged(member.Email, account.Email, value => member.Email = value);
            hasChanges |= SetIfChanged(member.Telephone, account.PhoneNumber, value => member.Telephone = value);

            if (string.Equals(profile.PrimaryInstructorName, account.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                hasChanges |= SetIfChanged(profile.InstructorLicenseNumber, account.LicenseNumber, value => profile.InstructorLicenseNumber = value);
                hasChanges |= SetRequiredIfChanged(profile.PrimaryInstructorEmail, account.Email, value => profile.PrimaryInstructorEmail = value);
                if (!string.IsNullOrWhiteSpace(account.PhoneNumber))
                {
                    hasChanges |= SetRequiredIfChanged(profile.PrimaryInstructorTelephone, account.PhoneNumber, value => profile.PrimaryInstructorTelephone = value);
                }
            }
        }

        return hasChanges;
    }

    private static bool SetRequiredIfChanged(string currentValue, string newValue, Action<string> setter)
    {
        if (string.Equals(currentValue, newValue, StringComparison.Ordinal))
        {
            return false;
        }

        setter(newValue);
        return true;
    }

    private static bool SetIfChanged(string? currentValue, string? newValue, Action<string?> setter)
    {
        if (string.Equals(currentValue, newValue, StringComparison.Ordinal))
        {
            return false;
        }

        setter(newValue);
        return true;
    }

    public async Task<SchoolStaffMember> AddStaffMemberAsync(SchoolStaffMember input, Guid actorUserId, string actorEmail)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new InvalidOperationException("Enter the staff member's name.");
        }

        var role = SchoolStaffRoles.All.SingleOrDefault(existing => string.Equals(existing, input.Role?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Select Instructor or Full-Time Official as the title.");
        var profile = await GetAsync();
        var member = new SchoolStaffMember
        {
            SchoolProfileId = profile.Id,
            Name = input.Name.Trim(),
            Role = role,
            Title = role == SchoolStaffRoles.Officer ? "Full-Time Official" : "Instructor",
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
        return await UpdateInternalAsync(input, actorUserId, actorEmail, null);
    }

    public async Task<SchoolProfile> UpdateForStateAsync(SchoolProfile input, Guid actorUserId, string actorEmail)
    {
        return await UpdateInternalAsync(input, actorUserId, actorEmail, input.State);
    }

    private async Task<SchoolProfile> UpdateInternalAsync(SchoolProfile input, Guid actorUserId, string actorEmail, string? state)
    {
        Validate(input);
        var profile = string.IsNullOrWhiteSpace(state)
            ? await GetAsync()
            : await GetForStateAsync(state);
        profile.LegalName = input.LegalName.Trim();
        profile.AdvertisedName = input.AdvertisedName.Trim();
        profile.StreetAddress = input.StreetAddress.Trim();
        profile.City = input.City.Trim();
        profile.AddressState = input.AddressState.Trim().ToUpperInvariant();
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
        profile.SupportTimeZoneId = input.SupportTimeZoneId.Trim();
        profile.WeekdaySupportHours = input.WeekdaySupportHours.Trim();
        profile.WeekdayExtendedSupportHours = NormalizeOptional(input.WeekdayExtendedSupportHours);
        profile.SaturdaySupportHours = input.SaturdaySupportHours.Trim();
        profile.SaturdayExtendedSupportHours = NormalizeOptional(input.SaturdayExtendedSupportHours);
        profile.SundaySupportHours = input.SundaySupportHours.Trim();
        profile.SupportScheduleExceptions = NormalizeOptional(input.SupportScheduleExceptions);
        profile.SupportResponseTarget = NormalizeOptional(input.SupportResponseTarget);
        profile.SupportScheduleEffectiveDate = input.SupportScheduleEffectiveDate;
        profile.SupportHours = BuildSupportHoursSummary(profile);
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
            input.AddressState,
            input.PostalCode,
            input.EducationDirectorName,
            input.CorporateOfficerName,
            input.PrimaryInstructorName,
            input.PrimaryInstructorEmail,
            input.PrimaryInstructorTelephone,
            input.SupportEmail,
            input.SupportTelephone,
            input.SupportTimeZoneId,
            input.WeekdaySupportHours,
            input.SaturdaySupportHours,
            input.SundaySupportHours,
            input.LicenseExaminationPerformanceRecord,
            input.AnnualSummaryReportData
        };
        if (requiredValues.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Complete all required school profile fields before saving.");
        }

        if (input.AddressState.Trim().Length != 2)
        {
            throw new InvalidOperationException("State must be a two-letter abbreviation.");
        }
    }

    private static string BuildSupportHoursSummary(SchoolProfile profile)
    {
        var timeZone = profile.SupportTimeZoneLabel;
        var lines = new List<string>
        {
            BuildScheduleLine("Monday-Friday", profile.WeekdaySupportHours, profile.WeekdayExtendedSupportHours, timeZone),
            BuildScheduleLine("Saturday", profile.SaturdaySupportHours, profile.SaturdayExtendedSupportHours, timeZone),
            $"Sunday ({timeZone}): {profile.SundaySupportHours.Trim()}"
        };

        if (profile.SupportScheduleEffectiveDate.HasValue)
        {
            lines.Add($"Effective {profile.SupportScheduleEffectiveDate.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}");
        }
        if (!string.IsNullOrWhiteSpace(profile.SupportScheduleExceptions))
        {
            lines.Add($"Exceptions: {profile.SupportScheduleExceptions.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(profile.SupportResponseTarget))
        {
            lines.Add($"Response target: {profile.SupportResponseTarget.Trim()}");
        }

        return string.Join("; ", lines);
    }

    private static string BuildScheduleLine(string dayLabel, string hours, string? extendedHours, string timeZone)
    {
        var line = $"{dayLabel}: {hours.Trim()} {timeZone}";
        return string.IsNullOrWhiteSpace(extendedHours) ? line : $"{line}, {extendedHours.Trim()}";
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}