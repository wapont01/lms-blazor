using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentCertificationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificationFirstName",
                table: "Enrollments",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificationIdentitySource",
                table: "Enrollments",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CertificationLastName",
                table: "Enrollments",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificationLicenseNumber",
                table: "Enrollments",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CertificationNameConfirmed",
                table: "Enrollments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CertificationState",
                table: "Enrollments",
                type: "TEXT",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificationFirstName",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "CertificationIdentitySource",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "CertificationLastName",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "CertificationLicenseNumber",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "CertificationNameConfirmed",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "CertificationState",
                table: "Enrollments");
        }
    }
}
