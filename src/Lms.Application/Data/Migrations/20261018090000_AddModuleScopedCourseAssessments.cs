using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261018090000_AddModuleScopedCourseAssessments")]
public partial class AddModuleScopedCourseAssessments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ModuleId",
            table: "CourseAssessments",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_CourseAssessments_ModuleId",
            table: "CourseAssessments",
            column: "ModuleId");

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CourseAssessments_ModuleId",
            table: "CourseAssessments");

        migrationBuilder.DropColumn(
            name: "ModuleId",
            table: "CourseAssessments");
    }
}