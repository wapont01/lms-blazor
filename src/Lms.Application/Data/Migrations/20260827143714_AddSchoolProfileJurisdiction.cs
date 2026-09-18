using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolProfileJurisdiction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Jurisdiction",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "Florida");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Jurisdiction",
                table: "SchoolProfiles");
        }
    }
}
