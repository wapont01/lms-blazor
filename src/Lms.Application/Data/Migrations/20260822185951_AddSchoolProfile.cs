using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchoolProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AdvertisedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StreetAddress = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    PostalCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EducationDirectorName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CorporateOfficerName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    PrimaryInstructorName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ProviderCertificationNumber = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    InstructorApprovalNumber = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    SupportEmail = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SupportTelephone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SupportHours = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    LicenseExaminationPerformanceRecord = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AnnualSummaryReportData = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolProfiles", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolProfiles");
        }
    }
}
