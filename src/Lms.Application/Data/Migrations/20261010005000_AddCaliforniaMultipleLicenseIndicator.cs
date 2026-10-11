using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261010005000_AddCaliforniaMultipleLicenseIndicator")]
public partial class AddCaliforniaMultipleLicenseIndicator : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsAdditionalLicenseRecord",
            table: "RegulatoryLicensees",
            type: "INTEGER",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsAdditionalLicenseRecord",
            table: "RegulatoryLicensees");
    }
}
