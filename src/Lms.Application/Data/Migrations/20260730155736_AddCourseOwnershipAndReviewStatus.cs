using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseOwnershipAndReviewStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerInstructorId",
                table: "Courses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "Courses",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "Courses",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Courses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "Courses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_OwnerInstructorId",
                table: "Courses",
                column: "OwnerInstructorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Courses_OwnerInstructorId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "OwnerInstructorId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "Courses");
        }
    }
}
