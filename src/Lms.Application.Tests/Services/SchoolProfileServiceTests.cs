using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class SchoolProfileServiceTests
{
    [Fact]
    public async Task StaffRoster_AllowsMultipleInstructorsAndOfficers_WithAuditedRemoval()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var service = new SchoolProfileService(dbContext, new AuditLogService(dbContext));
        var actorId = Guid.NewGuid();

        var instructor = await service.AddStaffMemberAsync(new SchoolStaffMember
        {
            Name = "  Jane Instructor  ",
            Role = SchoolStaffRoles.Instructor,
            LicenseNumber = "  I-42  "
        }, actorId, "admin@example.com");
        await service.AddStaffMemberAsync(new SchoolStaffMember
        {
            Name = "Robert Officer",
            Role = SchoolStaffRoles.Officer,
            Title = "Secretary"
        }, actorId, "admin@example.com");

        var profile = await service.GetAsync();
        Assert.Equal(6, profile.StaffMembers.Count);
        Assert.Equal("Jane Instructor", instructor.Name);
        Assert.Equal("I-42", instructor.LicenseNumber);

        await service.RemoveStaffMemberAsync(instructor.Id, actorId, "admin@example.com");

        Assert.Equal(5, await dbContext.SchoolStaffMembers.CountAsync());
        Assert.Equal(2, await dbContext.AuditLogs.CountAsync(log => log.Action == "school-staff.added"));
        Assert.Equal(1, await dbContext.AuditLogs.CountAsync(log => log.Action == "school-staff.removed"));
    }

    [Fact]
    public async Task GetAndUpdateAsync_PersistsSingletonProfileAndAuditEvent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var service = new SchoolProfileService(dbContext, new AuditLogService(dbContext));

        var profile = await service.GetAsync();
        Assert.Equal("WILLIAMS LAND REALTY LLC", profile.LegalName);
        Assert.Equal("221 Meridian Ave #511", profile.StreetAddress);
        Assert.Equal("Miami Beach", profile.City);
        Assert.Equal("Florida", profile.State);
        Assert.Equal("FL", profile.AddressState);
        Assert.Equal("33139", profile.PostalCode);
        Assert.Equal(SchoolProfile.TemporaryProviderLicenseNumber, profile.ProviderLicenseNumber);
        Assert.Equal("wapont01@hotmail.com", profile.SupportEmail);
        Assert.Equal("William A. Aponte", profile.PrimaryInstructorName);
        Assert.Equal("wapont01@hotmail.com", profile.PrimaryInstructorEmail);
        Assert.Equal("7865530222", profile.PrimaryInstructorTelephone);
        Assert.Equal("7865530222", profile.SupportTelephone);
        Assert.Equal("https://www.WilliamsLandRealty.com", profile.WebsiteUrl);
        Assert.Equal("America/New_York", profile.SupportTimeZoneId);
        Assert.Equal("7:00 AM-9:00 PM", profile.WeekdaySupportHours);
        Assert.Equal("9:00 AM-4:30 PM", profile.SaturdaySupportHours);
        Assert.Contains("scheduled examination-proctoring support", profile.SundaySupportHours);
        Assert.Contains(profile.StaffMembers, member => member.Name == "William A. Aponte" && member.Role == SchoolStaffRoles.Instructor);
        Assert.Contains(profile.StaffMembers, member => member.Name == "Clara M. Aponte"
            && member.Role == SchoolStaffRoles.Instructor
            && member.Email == "waponte2003@yahoo.com"
            && member.Telephone == "7049145824");
        Assert.Contains(profile.StaffMembers, member => member.Name == "Susan (Jill) Malloy"
            && member.Role == SchoolStaffRoles.Instructor
            && member.Title == "Instructor"
            && member.LicenseNumber == "2277");
        Assert.Contains(profile.StaffMembers, member => member.Name == "William A. Aponte"
            && member.LicenseNumber == "BK3059873"
            && member.Title == "Education Director / Instructor");
        Assert.Contains(profile.StaffMembers, member => member.Name == "Milena"
            && member.LicenseNumber == "FL-INSTRUCTOR-PLACEHOLDER");
        Assert.Equal("BK3059873", profile.InstructorLicenseNumber);

        profile.AdvertisedName = "Williams Land Realty School";
        profile.State = "North Carolina";
        profile.AddressState = "nc";
        profile.ProviderLicenseNumber = "  EP-123  ";
        profile.PrimaryInstructorTelephone = "  786-553-0222  ";
        profile.WeekdaySupportHours = "  8:00 AM-6:00 PM  ";
        profile.SupportScheduleEffectiveDate = new DateOnly(2026, 8, 23);
        profile.SupportScheduleExceptions = "  Closed on federal holidays.  ";
        profile.SupportResponseTarget = "  Replies within one business day.  ";
        var actorId = Guid.NewGuid();
        var updated = await service.UpdateAsync(profile, actorId, "ADMIN@EXAMPLE.COM");

        Assert.Equal("Williams Land Realty School", updated.AdvertisedName);
        Assert.Equal("North Carolina", updated.State);
        Assert.Equal("NC", updated.AddressState);
        Assert.Equal("EP-123", updated.ProviderLicenseNumber);
        Assert.Equal("786-553-0222", updated.PrimaryInstructorTelephone);
        Assert.Equal("8:00 AM-6:00 PM", updated.WeekdaySupportHours);
        Assert.Contains("Monday-Friday: 8:00 AM-6:00 PM ET", updated.SupportHours);
        Assert.Contains("Effective August 23, 2026", updated.SupportHours);
        Assert.Contains("Exceptions: Closed on federal holidays.", updated.SupportHours);
        Assert.Contains("Response target: Replies within one business day.", updated.SupportHours);
        Assert.Equal(1, await dbContext.SchoolProfiles.CountAsync());
        var audit = await dbContext.AuditLogs.SingleAsync();
        Assert.Equal("school-profile.updated", audit.Action);
        Assert.Equal("admin@example.com", audit.ActorEmail);
    }

    [Fact]
    public async Task GetForStateAsync_ReturnsSeparateFloridaAndNorthCarolinaProfiles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var service = new SchoolProfileService(dbContext, new AuditLogService(dbContext));

        var florida = await service.GetForStateAsync("Florida");
        var northCarolina = await service.GetForStateAsync("North Carolina");

        Assert.NotEqual(florida.Id, northCarolina.Id);
        Assert.Equal("Florida", florida.State);
        Assert.Equal("221 Meridian Ave #511", florida.StreetAddress);
        Assert.Equal("Miami Beach", florida.City);
        Assert.Equal("FL", florida.AddressState);
        Assert.Equal("North Carolina", northCarolina.State);
        Assert.Equal("17214 Belmont Stakes Lane", northCarolina.StreetAddress);
        Assert.Equal("Charlotte", northCarolina.City);
        Assert.Equal("NC", northCarolina.AddressState);
        Assert.Contains(northCarolina.StaffMembers, member => member.Name == "Clara M. Aponte");
    }
}