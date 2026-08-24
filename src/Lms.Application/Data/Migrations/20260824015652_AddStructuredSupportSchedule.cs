using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredSupportSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SaturdayExtendedSupportHours",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true,
                defaultValue: "Technical and examination support often available until 7:00 PM");

            migrationBuilder.AddColumn<string>(
                name: "SaturdaySupportHours",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "9:00 AM-4:30 PM");

            migrationBuilder.AddColumn<string>(
                name: "SundaySupportHours",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "Limited or scheduled examination-proctoring support until 7:00 PM; general telephone support queues otherwise closed");

            migrationBuilder.AddColumn<string>(
                name: "SupportResponseTarget",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SupportScheduleEffectiveDate",
                table: "SchoolProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportScheduleExceptions",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportTimeZoneId",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "America/New_York");

            migrationBuilder.AddColumn<string>(
                name: "WeekdayExtendedSupportHours",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true,
                defaultValue: "Technical and examination support often available until 10:00 PM");

            migrationBuilder.AddColumn<string>(
                name: "WeekdaySupportHours",
                table: "SchoolProfiles",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "7:00 AM-9:00 PM");

            migrationBuilder.Sql(
                """
                UPDATE SchoolProfiles
                SET SupportHours = 'Monday-Friday: 7:00 AM-9:00 PM ET, Technical and examination support often available until 10:00 PM; Saturday: 9:00 AM-4:30 PM ET, Technical and examination support often available until 7:00 PM; Sunday: Limited or scheduled examination-proctoring support until 7:00 PM; general telephone support queues otherwise closed ET',
                    WebsiteUrl = 'https://www.WilliamsLandRealty.com'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SaturdayExtendedSupportHours",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "SaturdaySupportHours",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "SundaySupportHours",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "SupportResponseTarget",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "SupportScheduleEffectiveDate",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "SupportScheduleExceptions",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "SupportTimeZoneId",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "WeekdayExtendedSupportHours",
                table: "SchoolProfiles");

            migrationBuilder.DropColumn(
                name: "WeekdaySupportHours",
                table: "SchoolProfiles");
        }
    }
}
