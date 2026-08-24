using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyDisclosureAcknowledgments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyDisclosureAcknowledgments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LearnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentTransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DisclosureVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DisclosurePublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StudentLegalName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    StudentEmail = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ElectronicSignature = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CourseTitle = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RegulatoryCourseCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    DeliveryMethod = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    InstructionalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    TuitionAndFees = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ProctoringFee = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SupportEmail = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SupportTelephone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LicenseExaminationPerformanceRecord = table.Column<string>(type: "TEXT", nullable: false),
                    AnnualSummaryReportData = table.Column<string>(type: "TEXT", nullable: false),
                    DisclosureTextSnapshot = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyDisclosureAcknowledgments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyDisclosureAcknowledgments_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PolicyDisclosureAcknowledgments_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PolicyDisclosureAcknowledgments_PaymentTransactions_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PolicyDisclosureAcknowledgments_UserAccounts_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentTransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseTitle = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    LineSubtotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseLines_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseLines_PaymentTransactions_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDisclosureAcknowledgments_CourseId",
                table: "PolicyDisclosureAcknowledgments",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDisclosureAcknowledgments_EnrollmentId",
                table: "PolicyDisclosureAcknowledgments",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDisclosureAcknowledgments_LearnerId_CourseId_AcknowledgedAtUtc",
                table: "PolicyDisclosureAcknowledgments",
                columns: new[] { "LearnerId", "CourseId", "AcknowledgedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDisclosureAcknowledgments_PaymentTransactionId",
                table: "PolicyDisclosureAcknowledgments",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_CourseId",
                table: "PurchaseLines",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_PaymentTransactionId",
                table: "PurchaseLines",
                column: "PaymentTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyDisclosureAcknowledgments");

            migrationBuilder.DropTable(
                name: "PurchaseLines");
        }
    }
}
