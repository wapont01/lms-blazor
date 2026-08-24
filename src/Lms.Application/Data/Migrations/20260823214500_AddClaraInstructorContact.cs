using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823214500_AddClaraInstructorContact")]
public partial class AddClaraInstructorContact : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE SchoolStaffMembers
            SET Email = 'waponte2003@yahoo.com',
                Telephone = '7049145824'
            WHERE Role = 'Instructor'
              AND lower(Name) = lower('Clara M. Aponte');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE SchoolStaffMembers
            SET Email = NULL,
                Telephone = NULL
            WHERE Role = 'Instructor'
              AND lower(Name) = lower('Clara M. Aponte')
              AND Email = 'waponte2003@yahoo.com'
              AND Telephone = '7049145824';
            """);
    }
}