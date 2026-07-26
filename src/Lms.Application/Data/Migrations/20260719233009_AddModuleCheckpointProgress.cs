using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleCheckpointProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModuleCheckpointProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckpointKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PassedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleCheckpointProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModuleCheckpointProgresses_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ModuleCheckpointProgresses_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCheckpointProgresses_CourseId",
                table: "ModuleCheckpointProgresses",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCheckpointProgresses_UserAccountId_CourseId_CheckpointKey",
                table: "ModuleCheckpointProgresses",
                columns: new[] { "UserAccountId", "CourseId", "CheckpointKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModuleCheckpointProgresses");
        }
    }
}
