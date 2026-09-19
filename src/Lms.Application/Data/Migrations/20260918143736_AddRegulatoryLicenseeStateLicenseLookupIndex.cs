using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatoryLicenseeStateLicenseLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryLicensees_State_LicenseNumber",
                table: "RegulatoryLicensees",
                columns: new[] { "State", "LicenseNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegulatoryLicensees_State_LicenseNumber",
                table: "RegulatoryLicensees");
        }
    }
}
