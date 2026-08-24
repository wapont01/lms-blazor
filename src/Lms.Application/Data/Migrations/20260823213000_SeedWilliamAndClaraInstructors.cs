using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823213000_SeedWilliamAndClaraInstructors")]
public partial class SeedWilliamAndClaraInstructors : Migration
{
    private const string WilliamId = "f2248de7-3287-4bd4-91e3-137e52fe2e71";
    private const string ClaraId = "093c5a95-60b2-4a3b-a588-ce14657dbf14";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            INSERT INTO SchoolStaffMembers
                (Id, SchoolProfileId, Name, Role, Title, LicenseNumber, Email, Telephone, AddedAtUtc)
            SELECT '{WilliamId}', profile.Id, 'William A. Aponte', 'Instructor', 'Primary Instructor', NULL,
                   'wapont01@hotmail.com', '7865530222', CURRENT_TIMESTAMP
            FROM SchoolProfiles AS profile
            WHERE NOT EXISTS (
                SELECT 1
                FROM SchoolStaffMembers AS member
                WHERE member.SchoolProfileId = profile.Id
                  AND member.Role = 'Instructor'
                  AND lower(member.Name) = lower('William A. Aponte'));

            INSERT INTO SchoolStaffMembers
                (Id, SchoolProfileId, Name, Role, Title, LicenseNumber, Email, Telephone, AddedAtUtc)
            SELECT '{ClaraId}', profile.Id, 'Clara M. Aponte', 'Instructor', 'Instructor', NULL,
                   NULL, NULL, CURRENT_TIMESTAMP
            FROM SchoolProfiles AS profile
            WHERE NOT EXISTS (
                SELECT 1
                FROM SchoolStaffMembers AS member
                WHERE member.SchoolProfileId = profile.Id
                  AND member.Role = 'Instructor'
                  AND lower(member.Name) = lower('Clara M. Aponte'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"DELETE FROM SchoolStaffMembers WHERE Id IN ('{WilliamId}', '{ClaraId}');");
    }
}