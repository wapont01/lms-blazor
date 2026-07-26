using Microsoft.EntityFrameworkCore.Migrations;

namespace Lms.Application.Data.Migrations;

public partial class AddInstructorPayoutAndSubscriptionTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InstructorPayouts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                InstructorId = table.Column<Guid>(type: "TEXT", nullable: false),
                Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
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
                BillingCycle = table.Column<string>(type: "TEXT", nullable: false),
                AmountPerCycle = table.Column<decimal>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
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

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "InstructorPayouts");

        migrationBuilder.DropTable(
            name: "Subscriptions");
    }
}
