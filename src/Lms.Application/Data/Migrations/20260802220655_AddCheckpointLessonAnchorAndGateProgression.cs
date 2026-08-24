using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckpointLessonAnchorAndGateProgression : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GatesFinalAssessment",
                table: "CourseCheckpointDefinitions",
                newName: "GatesProgression");

            migrationBuilder.AddColumn<Guid>(
                name: "LessonId",
                table: "CourseCheckpointDefinitions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LessonId",
                table: "CourseCheckpointDefinitions");

            migrationBuilder.RenameColumn(
                name: "GatesProgression",
                table: "CourseCheckpointDefinitions",
                newName: "GatesFinalAssessment");
        }
    }
}
