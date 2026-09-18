using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

public partial class RenameJurisdictionToState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn("Jurisdiction", "Courses", "State");
        migrationBuilder.RenameColumn("State", "SchoolProfiles", "AddressState");
        migrationBuilder.RenameColumn("Jurisdiction", "SchoolProfiles", "State");
        migrationBuilder.RenameColumn("Jurisdiction", "UserAccounts", "State");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn("State", "UserAccounts", "Jurisdiction");
        migrationBuilder.RenameColumn("State", "SchoolProfiles", "Jurisdiction");
        migrationBuilder.RenameColumn("AddressState", "SchoolProfiles", "State");
        migrationBuilder.RenameColumn("State", "Courses", "Jurisdiction");
    }
}
