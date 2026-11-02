using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstructorRecordOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserAccountId",
                table: "SchoolStaffMembers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolStaffMembers_UserAccountId",
                table: "SchoolStaffMembers",
                column: "UserAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_SchoolStaffMembers_UserAccounts_UserAccountId",
                table: "SchoolStaffMembers",
                column: "UserAccountId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SchoolStaffMembers_UserAccounts_UserAccountId",
                table: "SchoolStaffMembers");

            migrationBuilder.DropIndex(
                name: "IX_SchoolStaffMembers_UserAccountId",
                table: "SchoolStaffMembers");

            migrationBuilder.DropColumn(
                name: "UserAccountId",
                table: "SchoolStaffMembers");
        }
    }
}
