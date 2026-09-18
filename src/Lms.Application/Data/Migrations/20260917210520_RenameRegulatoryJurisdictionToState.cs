using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameRegulatoryJurisdictionToState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Jurisdiction",
                table: "RegulatoryLicensees",
                newName: "State");

            migrationBuilder.RenameIndex(
                name: "IX_RegulatoryLicensees_SnapshotId_Jurisdiction_LicenseNumber",
                table: "RegulatoryLicensees",
                newName: "IX_RegulatoryLicensees_SnapshotId_State_LicenseNumber");

            migrationBuilder.RenameColumn(
                name: "Jurisdiction",
                table: "RegulatoryFirms",
                newName: "State");

            migrationBuilder.RenameIndex(
                name: "IX_RegulatoryFirms_SnapshotId_Jurisdiction_LicenseNumber",
                table: "RegulatoryFirms",
                newName: "IX_RegulatoryFirms_SnapshotId_State_LicenseNumber");

            migrationBuilder.RenameColumn(
                name: "Jurisdiction",
                table: "RegulatoryDataSnapshots",
                newName: "State");

            migrationBuilder.RenameIndex(
                name: "IX_RegulatoryDataSnapshots_Jurisdiction_Status_RetrievedAtUtc",
                table: "RegulatoryDataSnapshots",
                newName: "IX_RegulatoryDataSnapshots_State_Status_RetrievedAtUtc");

            migrationBuilder.RenameColumn(
                name: "Jurisdiction",
                table: "CourseEligibilityPolicies",
                newName: "State");

            migrationBuilder.RenameIndex(
                name: "IX_CourseEligibilityPolicies_Jurisdiction_LicenseLevel_ComplianceType_EffectiveFrom",
                table: "CourseEligibilityPolicies",
                newName: "IX_CourseEligibilityPolicies_State_LicenseLevel_ComplianceType_EffectiveFrom");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "State",
                table: "RegulatoryLicensees",
                newName: "Jurisdiction");

            migrationBuilder.RenameIndex(
                name: "IX_RegulatoryLicensees_SnapshotId_State_LicenseNumber",
                table: "RegulatoryLicensees",
                newName: "IX_RegulatoryLicensees_SnapshotId_Jurisdiction_LicenseNumber");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "RegulatoryFirms",
                newName: "Jurisdiction");

            migrationBuilder.RenameIndex(
                name: "IX_RegulatoryFirms_SnapshotId_State_LicenseNumber",
                table: "RegulatoryFirms",
                newName: "IX_RegulatoryFirms_SnapshotId_Jurisdiction_LicenseNumber");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "RegulatoryDataSnapshots",
                newName: "Jurisdiction");

            migrationBuilder.RenameIndex(
                name: "IX_RegulatoryDataSnapshots_State_Status_RetrievedAtUtc",
                table: "RegulatoryDataSnapshots",
                newName: "IX_RegulatoryDataSnapshots_Jurisdiction_Status_RetrievedAtUtc");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "CourseEligibilityPolicies",
                newName: "Jurisdiction");

            migrationBuilder.RenameIndex(
                name: "IX_CourseEligibilityPolicies_State_LicenseLevel_ComplianceType_EffectiveFrom",
                table: "CourseEligibilityPolicies",
                newName: "IX_CourseEligibilityPolicies_Jurisdiction_LicenseLevel_ComplianceType_EffectiveFrom");
        }
    }
}
