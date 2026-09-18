using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEducationDirectorOrientationAndInstructorObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HireReportedToCommissionAtUtc",
                table: "SchoolStaffMembers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastObservationAtUtc",
                table: "SchoolStaffMembers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EducationDirectorDesignatedAtUtc",
                table: "SchoolProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EducationDirectorOrientationVideoViewedAtUtc",
                table: "SchoolProfiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HireReportedToCommissionAtUtc",
                table: "SchoolStaffMembers");

            migrationBuilder.DropColumn(
                name: "LastObservationAtUtc",
                table: "SchoolStaffMembers");

            migrationBuilder.DropColumn(
                name: "EducationDirectorDesignatedAtUtc",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "EducationDirectorOrientationVideoViewedAtUtc",
                table: "SchoolProfiles");
        }
    }
}
