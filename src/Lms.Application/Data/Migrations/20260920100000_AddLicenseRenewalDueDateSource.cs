using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260920100000_AddLicenseRenewalDueDateSource")]
public partial class AddLicenseRenewalDueDateSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LicenseRenewalDueDateSource",
            table: "UserAccounts",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "Unknown");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LicenseRenewalDueDateSource",
            table: "UserAccounts");
    }
}
