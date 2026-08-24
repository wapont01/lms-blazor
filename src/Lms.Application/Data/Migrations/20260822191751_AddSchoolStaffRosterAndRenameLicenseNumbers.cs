using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolStaffRosterAndRenameLicenseNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProviderCertificationNumber",
                table: "SchoolProfiles",
                newName: "ProviderLicenseNumber");

            migrationBuilder.RenameColumn(
                name: "InstructorApprovalNumber",
                table: "SchoolProfiles",
                newName: "InstructorLicenseNumber");

            migrationBuilder.CreateTable(
                name: "SchoolStaffMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchoolProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    LicenseNumber = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolStaffMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolStaffMembers_SchoolProfiles_SchoolProfileId",
                        column: x => x.SchoolProfileId,
                        principalTable: "SchoolProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolStaffMembers_SchoolProfileId_Role",
                table: "SchoolStaffMembers",
                columns: new[] { "SchoolProfileId", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolStaffMembers");

            migrationBuilder.RenameColumn(
                name: "ProviderLicenseNumber",
                table: "SchoolProfiles",
                newName: "ProviderCertificationNumber");

            migrationBuilder.RenameColumn(
                name: "InstructorLicenseNumber",
                table: "SchoolProfiles",
                newName: "InstructorApprovalNumber");
        }
    }
}
