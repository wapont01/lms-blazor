using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CourseStartReminderSentAt",
                table: "Enrollments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EnrollmentClosingReminderSentAt",
                table: "Enrollments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssessmentReminderSentAt",
                table: "Enrollments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CourseReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReminderType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseReminders_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseReminders_EnrollmentId_ReminderType",
                table: "CourseReminders",
                columns: new[] { "EnrollmentId", "ReminderType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseReminders_SentAt",
                table: "CourseReminders",
                column: "SentAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseReminders");

            migrationBuilder.DropColumn(
                name: "CourseStartReminderSentAt",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "EnrollmentClosingReminderSentAt",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "AssessmentReminderSentAt",
                table: "Enrollments");
        }
    }
}
