using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReclassifyExistingCoursesAsCeElective : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE Courses
                SET ComplianceType = 'ContinuingEducation',
                    ContinuingEducationType = 'Elective'
                WHERE lower(Id) <> '64cc1cbb-45fc-4a76-b3b4-de399cb3830f';

                UPDATE Courses
                SET ComplianceType = 'Prelicensing',
                    ContinuingEducationType = NULL
                WHERE lower(Id) = '64cc1cbb-45fc-4a76-b3b4-de399cb3830f';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE Courses
                SET ComplianceType = '',
                    ContinuingEducationType = NULL
                WHERE lower(Id) <> '64cc1cbb-45fc-4a76-b3b4-de399cb3830f';
                """);
        }
    }
}
