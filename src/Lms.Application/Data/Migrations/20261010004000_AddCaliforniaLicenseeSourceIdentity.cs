using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261010004000_AddCaliforniaLicenseeSourceIdentity")]
public partial class AddCaliforniaLicenseeSourceIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "EthicsAndAgencyOnly",
            table: "RegulatoryLicensees",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsRestricted",
            table: "RegulatoryLicensees",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateOnly>(
            name: "OriginalLicenseDate",
            table: "RegulatoryLicensees",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RelatedLicenseName",
            table: "RegulatoryLicensees",
            type: "TEXT",
            maxLength: 160,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RelatedLicenseNumber",
            table: "RegulatoryLicensees",
            type: "TEXT",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RelatedLicenseType",
            table: "RegulatoryLicensees",
            type: "TEXT",
            maxLength: 40,
            nullable: true);

        migrationBuilder.DropIndex(
            name: "IX_RegulatoryLicensees_SnapshotId_State_LicenseNumber",
            table: "RegulatoryLicensees");

        migrationBuilder.CreateIndex(
            name: "IX_RegulatoryLicensees_SnapshotId_State_LicenseNumber_LicenseType_LicenseStatus_LicenseRenewalDate",
            table: "RegulatoryLicensees",
            columns: new[]
            {
                "SnapshotId",
                "State",
                "LicenseNumber",
                "LicenseType",
                "LicenseStatus",
                "LicenseRenewalDate"
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RegulatoryLicensees_SnapshotId_State_LicenseNumber_LicenseType_LicenseStatus_LicenseRenewalDate",
            table: "RegulatoryLicensees");

        migrationBuilder.CreateIndex(
            name: "IX_RegulatoryLicensees_SnapshotId_State_LicenseNumber",
            table: "RegulatoryLicensees",
            columns: new[] { "SnapshotId", "State", "LicenseNumber" },
            unique: true);

        migrationBuilder.DropColumn(
            name: "EthicsAndAgencyOnly",
            table: "RegulatoryLicensees");

        migrationBuilder.DropColumn(
            name: "IsRestricted",
            table: "RegulatoryLicensees");

        migrationBuilder.DropColumn(
            name: "OriginalLicenseDate",
            table: "RegulatoryLicensees");

        migrationBuilder.DropColumn(
            name: "RelatedLicenseName",
            table: "RegulatoryLicensees");

        migrationBuilder.DropColumn(
            name: "RelatedLicenseNumber",
            table: "RegulatoryLicensees");

        migrationBuilder.DropColumn(
            name: "RelatedLicenseType",
            table: "RegulatoryLicensees");
    }
}
