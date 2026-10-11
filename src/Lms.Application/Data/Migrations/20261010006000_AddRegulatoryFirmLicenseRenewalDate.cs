using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261010006000_AddRegulatoryFirmLicenseRenewalDate")]
public partial class AddRegulatoryFirmLicenseRenewalDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "LicenseRenewalDate",
            table: "RegulatoryFirms",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LicenseRenewalDate",
            table: "RegulatoryFirms");
    }
}
