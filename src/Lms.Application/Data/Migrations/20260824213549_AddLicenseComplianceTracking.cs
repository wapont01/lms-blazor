using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

public partial class AddLicenseComplianceTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("BicCourseCompletedAt", "UserAccounts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("BicDesignationAt", "UserAccounts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("BicEligibleAt", "UserAccounts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("BicUpdateCourseDueDate", "UserAccounts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("BrokerageExperienceStartDate", "UserAccounts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("ContinuingEducationDueDate", "UserAccounts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("LicenseRenewalDueDate", "UserAccounts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>("PostlicensingCompletedAt", "UserAccounts", type: "TEXT", nullable: true);

        migrationBuilder.CreateTable(
            name: "LicenseComplianceReminders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                ReminderType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                DueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LicenseComplianceReminders", x => x.Id);
                table.ForeignKey("FK_LicenseComplianceReminders_UserAccounts_UserAccountId", x => x.UserAccountId, "UserAccounts", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_UserAccounts_BicEligibleAt", "UserAccounts", "BicEligibleAt");
        migrationBuilder.CreateIndex("IX_UserAccounts_ContinuingEducationDueDate", "UserAccounts", "ContinuingEducationDueDate");
        migrationBuilder.CreateIndex("IX_UserAccounts_InitialLicensureDate", "UserAccounts", "InitialLicensureDate");
        migrationBuilder.CreateIndex("IX_UserAccounts_LicenseRenewalDueDate", "UserAccounts", "LicenseRenewalDueDate");
        migrationBuilder.CreateIndex("IX_LicenseComplianceReminders_UserAccountId_ReminderType_DueAtUtc", "LicenseComplianceReminders", new[] { "UserAccountId", "ReminderType", "DueAtUtc" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("LicenseComplianceReminders");
        migrationBuilder.DropIndex("IX_UserAccounts_BicEligibleAt", "UserAccounts");
        migrationBuilder.DropIndex("IX_UserAccounts_ContinuingEducationDueDate", "UserAccounts");
        migrationBuilder.DropIndex("IX_UserAccounts_InitialLicensureDate", "UserAccounts");
        migrationBuilder.DropIndex("IX_UserAccounts_LicenseRenewalDueDate", "UserAccounts");
        migrationBuilder.DropColumn("BicCourseCompletedAt", "UserAccounts");
        migrationBuilder.DropColumn("BicDesignationAt", "UserAccounts");
        migrationBuilder.DropColumn("BicEligibleAt", "UserAccounts");
        migrationBuilder.DropColumn("BicUpdateCourseDueDate", "UserAccounts");
        migrationBuilder.DropColumn("BrokerageExperienceStartDate", "UserAccounts");
        migrationBuilder.DropColumn("ContinuingEducationDueDate", "UserAccounts");
        migrationBuilder.DropColumn("LicenseRenewalDueDate", "UserAccounts");
        migrationBuilder.DropColumn("PostlicensingCompletedAt", "UserAccounts");
    }
}
