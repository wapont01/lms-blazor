using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseCheckpointDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Refunds",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldDefaultValue: "Initiated");

            migrationBuilder.AddColumn<DateTime>(
                name: "FraudFlaggedAt",
                table: "Refunds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FraudReason",
                table: "Refunds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FraudRiskLevel",
                table: "Refunds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsApproved",
                table: "Refunds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlaggedForReview",
                table: "Refunds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CourseCheckpointDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ModuleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseCheckpointDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseCheckpointDefinitions_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseCheckpointDefinitions_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InstructorPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstructorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StripeTransferId = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduledDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PaidDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructorPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstructorPayouts_UserAccounts_InstructorId",
                        column: x => x.InstructorId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LearnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BillingCycle = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AmountPerCycle = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StripeSubscriptionId = table.Column<string>(type: "TEXT", nullable: true),
                    NextBillingDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_UserAccounts_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourseCheckpointOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseCheckpointDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    IsCorrect = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseCheckpointOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseCheckpointOptions_CourseCheckpointDefinitions_CourseCheckpointDefinitionId",
                        column: x => x.CourseCheckpointDefinitionId,
                        principalTable: "CourseCheckpointDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseCheckpointDefinitions_CourseId_Key",
                table: "CourseCheckpointDefinitions",
                columns: new[] { "CourseId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseCheckpointDefinitions_ModuleId",
                table: "CourseCheckpointDefinitions",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseCheckpointOptions_CourseCheckpointDefinitionId_OrderIndex",
                table: "CourseCheckpointOptions",
                columns: new[] { "CourseCheckpointDefinitionId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorPayouts_InstructorId",
                table: "InstructorPayouts",
                column: "InstructorId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CourseId",
                table: "Subscriptions",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_LearnerId",
                table: "Subscriptions",
                column: "LearnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseCheckpointOptions");

            migrationBuilder.DropTable(
                name: "InstructorPayouts");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "CourseCheckpointDefinitions");

            migrationBuilder.DropColumn(
                name: "FraudFlaggedAt",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "FraudReason",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "FraudRiskLevel",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "IsApproved",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "IsFlaggedForReview",
                table: "Refunds");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Refunds",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "Initiated",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 20);
        }
    }
}
