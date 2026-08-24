using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrimaryInstructorContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrimaryInstructorEmail",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 160,
                nullable: false,
                defaultValue: "wapont01@hotmail.com");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryInstructorTelephone",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "7865530222");

            migrationBuilder.Sql(
                """
                UPDATE SchoolProfiles
                SET EducationDirectorName = 'William A. Aponte',
                    CorporateOfficerName = 'William A. Aponte',
                    PrimaryInstructorName = 'William A. Aponte',
                    PrimaryInstructorEmail = 'wapont01@hotmail.com',
                    PrimaryInstructorTelephone = '7865530222',
                    SupportEmail = 'wapont01@hotmail.com',
                    SupportTelephone = '7865530222';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryInstructorEmail",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "PrimaryInstructorTelephone",
                table: "SchoolProfiles");
        }
    }
}
