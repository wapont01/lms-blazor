using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGeneralComplianceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Courses SET ComplianceType = '' WHERE ComplianceType = 'General';");

            migrationBuilder.AlterColumn<string>(
                name: "ComplianceType",
                table: "Courses",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 40,
                oldDefaultValue: "General");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Courses SET ComplianceType = 'General' WHERE ComplianceType = '';");

            migrationBuilder.AlterColumn<string>(
                name: "ComplianceType",
                table: "Courses",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "General",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 40,
                oldDefaultValue: "");
        }
    }
}
