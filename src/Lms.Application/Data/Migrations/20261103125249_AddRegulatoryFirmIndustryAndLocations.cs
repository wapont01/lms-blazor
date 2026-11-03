using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatoryFirmIndustryAndLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "RegulatoryFirms",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "Real Estate");

            migrationBuilder.CreateTable(
                name: "RegulatoryFirmLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirmId = table.Column<Guid>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    MatchMethod = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryFirmLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryFirmLocations_RegulatoryDataSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "RegulatoryDataSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RegulatoryFirmLocations_RegulatoryFirms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "RegulatoryFirms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryFirmLocations_FirmId",
                table: "RegulatoryFirmLocations",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryFirmLocations_SnapshotId_State_RegistrationNumber",
                table: "RegulatoryFirmLocations",
                columns: new[] { "SnapshotId", "State", "RegistrationNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegulatoryFirmLocations");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "RegulatoryFirms");
        }
    }
}
