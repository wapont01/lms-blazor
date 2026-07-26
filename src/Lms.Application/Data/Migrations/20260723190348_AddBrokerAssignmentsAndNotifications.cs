using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerAssignmentsAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrokerLearnerAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BrokerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LearnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerLearnerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrokerLearnerAssignments_UserAccounts_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BrokerLearnerAssignments_UserAccounts_BrokerUserId",
                        column: x => x.BrokerUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrokerLearnerAssignments_UserAccounts_LearnerUserId",
                        column: x => x.LearnerUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemNotifications_UserAccounts_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrokerLearnerAssignments_AssignedByUserId",
                table: "BrokerLearnerAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrokerLearnerAssignments_BrokerUserId_LearnerUserId",
                table: "BrokerLearnerAssignments",
                columns: new[] { "BrokerUserId", "LearnerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrokerLearnerAssignments_LearnerUserId",
                table: "BrokerLearnerAssignments",
                column: "LearnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemNotifications_RecipientUserId_ReadAt_CreatedAt",
                table: "SystemNotifications",
                columns: new[] { "RecipientUserId", "ReadAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrokerLearnerAssignments");

            migrationBuilder.DropTable(
                name: "SystemNotifications");
        }
    }
}
