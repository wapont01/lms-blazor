using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddColoradoDoraSourceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceLicenseType",
                table: "RegulatoryLicensees",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RegulatorySourceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceRowNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    LicensePrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LicenseNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    LicenseType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    LicenseStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    SourceRecordJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatorySourceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatorySourceRecords_RegulatoryDataSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "RegulatoryDataSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatorySourceRecords_SnapshotId_LicenseType_LicenseStatus",
                table: "RegulatorySourceRecords",
                columns: new[] { "SnapshotId", "LicenseType", "LicenseStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatorySourceRecords_SnapshotId_SourceRowNumber",
                table: "RegulatorySourceRecords",
                columns: new[] { "SnapshotId", "SourceRowNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegulatorySourceRecords");

            migrationBuilder.DropColumn(
                name: "SourceLicenseType",
                table: "RegulatoryLicensees");
        }
    }
}
