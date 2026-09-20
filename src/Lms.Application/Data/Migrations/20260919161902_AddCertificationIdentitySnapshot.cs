using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificationIdentitySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificationFirstName",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificationIdentitySource",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CertificationLastName",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificationLicenseNumber",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificationReviewStatus",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CertificationState",
                table: "CompletionCertificates",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CommissionSubmittedAtUtc",
                table: "CompletionCertificates",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificationFirstName",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "CertificationIdentitySource",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "CertificationLastName",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "CertificationLicenseNumber",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "CertificationReviewStatus",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "CertificationState",
                table: "CompletionCertificates");

            migrationBuilder.DropColumn(
                name: "CommissionSubmittedAtUtc",
                table: "CompletionCertificates");
        }
    }
}
