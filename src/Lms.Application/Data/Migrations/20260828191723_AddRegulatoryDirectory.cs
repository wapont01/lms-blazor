using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatoryDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegulatoryDataSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ParserVersion = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    AcquisitionCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    RetrievedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryDataSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryFirms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LicenseNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TradeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LicenseStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    QualifyingBrokerName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryFirms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryFirms_RegulatoryDataSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "RegulatoryDataSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryLicensees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LicenseNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    LicenseType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    LicenseLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LicenseStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    LicenseRenewalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    UpdateHours = table.Column<int>(type: "INTEGER", nullable: true),
                    ElectiveHours = table.Column<int>(type: "INTEGER", nullable: true),
                    PostlicensingHours = table.Column<int>(type: "INTEGER", nullable: true),
                    IsBicEligible = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryLicensees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryLicensees_RegulatoryDataSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "RegulatoryDataSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegulatoryFirmLicenseeAffiliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirmId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LicenseeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsBrokerInCharge = table.Column<bool>(type: "INTEGER", nullable: false),
                    BrokerInChargeName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryFirmLicenseeAffiliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryFirmLicenseeAffiliations_RegulatoryDataSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "RegulatoryDataSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RegulatoryFirmLicenseeAffiliations_RegulatoryFirms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "RegulatoryFirms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RegulatoryFirmLicenseeAffiliations_RegulatoryLicensees_LicenseeId",
                        column: x => x.LicenseeId,
                        principalTable: "RegulatoryLicensees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryDataSnapshots_Jurisdiction_Status_RetrievedAtUtc",
                table: "RegulatoryDataSnapshots",
                columns: new[] { "Jurisdiction", "Status", "RetrievedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryFirmLicenseeAffiliations_FirmId",
                table: "RegulatoryFirmLicenseeAffiliations",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryFirmLicenseeAffiliations_LicenseeId",
                table: "RegulatoryFirmLicenseeAffiliations",
                column: "LicenseeId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryFirmLicenseeAffiliations_SnapshotId_FirmId_LicenseeId",
                table: "RegulatoryFirmLicenseeAffiliations",
                columns: new[] { "SnapshotId", "FirmId", "LicenseeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryFirms_SnapshotId_Jurisdiction_LicenseNumber",
                table: "RegulatoryFirms",
                columns: new[] { "SnapshotId", "Jurisdiction", "LicenseNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryLicensees_SnapshotId_Jurisdiction_LicenseNumber",
                table: "RegulatoryLicensees",
                columns: new[] { "SnapshotId", "Jurisdiction", "LicenseNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegulatoryFirmLicenseeAffiliations");

            migrationBuilder.DropTable(
                name: "RegulatoryFirms");

            migrationBuilder.DropTable(
                name: "RegulatoryLicensees");

            migrationBuilder.DropTable(
                name: "RegulatoryDataSnapshots");
        }
    }
}
