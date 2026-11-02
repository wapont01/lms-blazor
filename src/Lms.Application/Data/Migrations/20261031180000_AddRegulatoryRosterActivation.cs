using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261031180000_AddRegulatoryRosterActivation")]
public class AddRegulatoryRosterActivation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("ActivatedAtUtc", "RegulatoryDataSnapshots", type: "TEXT", nullable: true);
        migrationBuilder.Sql("""
            UPDATE RegulatoryDataSnapshots SET ActivatedAtUtc = COALESCE(ReviewedAtUtc, ImportedAtUtc)
            WHERE State = 'Florida' AND ParserVersion = 'dbpr-physical-v1' AND Status = 'Approved';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("ActivatedAtUtc", "RegulatoryDataSnapshots");
}
