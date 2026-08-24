using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDistanceEducationCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InitialLicensureDate",
                table: "UserAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBicEligible",
                table: "UserAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "UserAccounts",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseNumber",
                table: "UserAccounts",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseStatus",
                table: "UserAccounts",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AccessGrantedAtUtc",
                table: "Enrollments",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "Enrollments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompletionWindowDays",
                table: "Courses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComplianceType",
                table: "Courses",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "ContinuingEducationType",
                table: "Courses",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryMethod",
                table: "Courses",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "DistanceEducation");

            migrationBuilder.AddColumn<int>(
                name: "MinimumAttendancePercent",
                table: "Courses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.AddColumn<int>(
                name: "MinimumPassingPercent",
                table: "Courses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 75);

            migrationBuilder.AddColumn<string>(
                name: "RegulatoryCourseCode",
                table: "Courses",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredInstructionalMinutes",
                table: "Courses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresProctoredExam",
                table: "Courses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OrderIndex",
                table: "CourseCheckpointDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "CompletionCertificates",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreditHours",
                table: "CompletionCertificates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EducationDirectorName",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructorName",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegulatoryCourseCode",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExamProctoringSessionId",
                table: "AssessmentAttempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CourseActivitySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastActivityAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreditedMinutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseActivitySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseActivitySessions_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseActivitySessions_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamProctoringSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProctorName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ExternalSessionId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    IdentityVerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedBookConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SecurityIncidentReported = table.Column<bool>(type: "INTEGER", nullable: false),
                    SecurityIncidentNotes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamProctoringSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamProctoringSessions_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamProctoringSessions_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentAttempts_ExamProctoringSessionId",
                table: "AssessmentAttempts",
                column: "ExamProctoringSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseActivitySessions_CourseId_UserAccountId_StartedAtUtc",
                table: "CourseActivitySessions",
                columns: new[] { "CourseId", "UserAccountId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseActivitySessions_UserAccountId_EndedAtUtc",
                table: "CourseActivitySessions",
                columns: new[] { "UserAccountId", "EndedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamProctoringSessions_CourseId_UserAccountId_ExpiresAtUtc",
                table: "ExamProctoringSessions",
                columns: new[] { "CourseId", "UserAccountId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamProctoringSessions_ExternalSessionId",
                table: "ExamProctoringSessions",
                column: "ExternalSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamProctoringSessions_UserAccountId",
                table: "ExamProctoringSessions",
                column: "UserAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssessmentAttempts_ExamProctoringSessions_ExamProctoringSessionId",
                table: "AssessmentAttempts",
                column: "ExamProctoringSessionId",
                principalTable: "ExamProctoringSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssessmentAttempts_ExamProctoringSessions_ExamProctoringSessionId",
                table: "AssessmentAttempts");

            migrationBuilder.DropTable(
                name: "CourseActivitySessions");

            migrationBuilder.DropTable(
                name: "ExamProctoringSessions");

            migrationBuilder.DropIndex(
                name: "IX_AssessmentAttempts_ExamProctoringSessionId",
                table: "AssessmentAttempts");

            migrationBuilder.DropColumn(
                name: "InitialLicensureDate",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "IsBicEligible",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "LicenseNumber",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "LicenseStatus",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "AccessGrantedAtUtc",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "CompletionWindowDays",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "ComplianceType",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "ContinuingEducationType",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "DeliveryMethod",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "MinimumAttendancePercent",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "MinimumPassingPercent",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "RegulatoryCourseCode",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "RequiredInstructionalMinutes",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "RequiresProctoredExam",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "OrderIndex",
                table: "CourseCheckpointDefinitions");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "CreditHours",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "EducationDirectorName",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "InstructorName",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "RegulatoryCourseCode",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "ExamProctoringSessionId",
                table: "AssessmentAttempts");
        }
    }
}
