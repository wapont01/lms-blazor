using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameCheckpointIsFinalToGatesFinalAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsFinal",
                table: "CourseCheckpointDefinitions",
                newName: "GatesFinalAssessment");

            migrationBuilder.AlterColumn<string>(
                name: "TextContent",
                table: "Lessons",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GatesFinalAssessment",
                table: "CourseCheckpointDefinitions",
                newName: "IsFinal");

            migrationBuilder.AlterColumn<string>(
                name: "TextContent",
                table: "Lessons",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }
    }
}
