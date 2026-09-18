using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatoryLicenseDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LicenseIssuedDate",
                table: "RegulatoryLicensees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LicenseIssuedDate",
                table: "RegulatoryFirms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RelationshipEffectiveDate",
                table: "RegulatoryFirmLicenseeAffiliations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LicenseIssuedDate",
                table: "RegulatoryLicensees");

            migrationBuilder.DropColumn(
                name: "LicenseIssuedDate",
                table: "RegulatoryFirms");

            migrationBuilder.DropColumn(
                name: "RelationshipEffectiveDate",
                table: "RegulatoryFirmLicenseeAffiliations");
        }
    }
}
