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
        Assert.Equal(4, profile.StaffMembers.Count);
        Assert.Equal("Jane Instructor", instructor.Name);
        Assert.Equal("I-42", instructor.LicenseNumber);

        await service.RemoveStaffMemberAsync(instructor.Id, actorId, "admin@example.com");

        Assert.Equal(3, await dbContext.SchoolStaffMembers.CountAsync());
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
        Assert.Equal("wapont01@hotmail.com", profile.SupportEmail);
        Assert.Equal("William A. Aponte", profile.PrimaryInstructorName);
        Assert.Equal("wapont01@hotmail.com", profile.PrimaryInstructorEmail);
        Assert.Equal("7865530222", profile.PrimaryInstructorTelephone);
        Assert.Equal("7865530222", profile.SupportTelephone);
        Assert.Contains(profile.StaffMembers, member => member.Name == "William A. Aponte" && member.Role == SchoolStaffRoles.Instructor);
        Assert.Contains(profile.StaffMembers, member => member.Name == "Clara M. Aponte"
            && member.Role == SchoolStaffRoles.Instructor
            && member.Email == "waponte2003@yahoo.com"
            && member.Telephone == "7049145824");

        profile.AdvertisedName = "Williams Land Realty School";
        profile.State = "nc";
        profile.ProviderLicenseNumber = "  EP-123  ";
        profile.PrimaryInstructorTelephone = "  786-553-0222  ";
        var actorId = Guid.NewGuid();
        var updated = await service.UpdateAsync(profile, actorId, "ADMIN@EXAMPLE.COM");

        Assert.Equal("Williams Land Realty School", updated.AdvertisedName);
        Assert.Equal("NC", updated.State);
        Assert.Equal("EP-123", updated.ProviderLicenseNumber);
        Assert.Equal("786-553-0222", updated.PrimaryInstructorTelephone);
        Assert.Equal(1, await dbContext.SchoolProfiles.CountAsync());
        var audit = await dbContext.AuditLogs.SingleAsync();
        Assert.Equal("school-profile.updated", audit.Action);
        Assert.Equal("admin@example.com", audit.ActorEmail);
    }
}