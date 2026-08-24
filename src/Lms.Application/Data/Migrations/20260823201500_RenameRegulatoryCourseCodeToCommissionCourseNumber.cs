using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823201500_RenameRegulatoryCourseCodeToCommissionCourseNumber")]
public sealed class RenameRegulatoryCourseCodeToCommissionCourseNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "RegulatoryCourseCode",
            table: "Courses",
            newName: "CommissionCourseNumber");

        migrationBuilder.RenameColumn(
            name: "RegulatoryCourseCode",
            table: "CompletionCertificates",
            newName: "CommissionCourseNumber");

        migrationBuilder.RenameColumn(
            name: "RegulatoryCourseCode",
            table: "PolicyDisclosureAcknowledgments",
            newName: "CommissionCourseNumber");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "CommissionCourseNumber",
            table: "Courses",
            newName: "RegulatoryCourseCode");

        migrationBuilder.RenameColumn(
            name: "CommissionCourseNumber",
            table: "CompletionCertificates",
            newName: "RegulatoryCourseCode");

        migrationBuilder.RenameColumn(
            name: "CommissionCourseNumber",
            table: "PolicyDisclosureAcknowledgments",
            newName: "RegulatoryCourseCode");
    }
}
