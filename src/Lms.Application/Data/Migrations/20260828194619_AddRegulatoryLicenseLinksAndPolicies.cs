using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatoryLicenseLinksAndPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseEligibilityPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LicenseLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ComplianceType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ContinuingEducationType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EligibilityMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseEligibilityPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRegulatoryLicenseLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegulatorySnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegulatoryLicenseeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRegulatoryLicenseLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRegulatoryLicenseLinks_RegulatoryDataSnapshots_RegulatorySnapshotId",
                        column: x => x.RegulatorySnapshotId,
                        principalTable: "RegulatoryDataSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRegulatoryLicenseLinks_RegulatoryLicensees_RegulatoryLicenseeId",
                        column: x => x.RegulatoryLicenseeId,
                        principalTable: "RegulatoryLicensees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRegulatoryLicenseLinks_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseEligibilityPolicies_Jurisdiction_LicenseLevel_ComplianceType_EffectiveFrom",
                table: "CourseEligibilityPolicies",
                columns: new[] { "Jurisdiction", "LicenseLevel", "ComplianceType", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRegulatoryLicenseLinks_RegulatoryLicenseeId",
                table: "UserRegulatoryLicenseLinks",
                column: "RegulatoryLicenseeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRegulatoryLicenseLinks_RegulatorySnapshotId_RegulatoryLicenseeId",
                table: "UserRegulatoryLicenseLinks",
                columns: new[] { "RegulatorySnapshotId", "RegulatoryLicenseeId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRegulatoryLicenseLinks_UserAccountId",
                table: "UserRegulatoryLicenseLinks",
                column: "UserAccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseEligibilityPolicies");

            migrationBuilder.DropTable(
                name: "UserRegulatoryLicenseLinks");
        }
    }
}
