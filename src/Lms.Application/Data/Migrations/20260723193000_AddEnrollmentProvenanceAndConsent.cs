using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    public partial class AddEnrollmentProvenanceAndConsent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnrollmentSource",
                table: "Enrollments",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "LearnerPurchase");

            migrationBuilder.AddColumn<Guid>(
                name: "SponsoredByBrokerUserId",
                table: "Enrollments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentStatus",
                table: "Enrollments",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_SponsoredByBrokerUserId_EnrollmentSource",
                table: "Enrollments",
                columns: new[] { "SponsoredByBrokerUserId", "EnrollmentSource" });

            migrationBuilder.AddForeignKey(
                name: "FK_Enrollments_UserAccounts_SponsoredByBrokerUserId",
                table: "Enrollments",
                column: "SponsoredByBrokerUserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Enrollments_UserAccounts_SponsoredByBrokerUserId",
                table: "Enrollments");

            migrationBuilder.DropIndex(
                name: "IX_Enrollments_SponsoredByBrokerUserId_EnrollmentSource",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "EnrollmentSource",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "SponsoredByBrokerUserId",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "ConsentStatus",
                table: "Enrollments");
        }
    }
}