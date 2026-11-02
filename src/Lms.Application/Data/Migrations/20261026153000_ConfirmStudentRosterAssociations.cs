using Lms.Application.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026153000_ConfirmStudentRosterAssociations")]
public sealed class ConfirmStudentRosterAssociations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "RosterAccountRequests"
            SET "Status" = 'Confirmed'
            WHERE "Status" IN ('Pending', 'Approved')
              AND EXISTS (
                  SELECT 1 FROM "UserAccounts" AS "u"
                  WHERE "u"."Id" = "RosterAccountRequests"."UserAccountId"
                    AND "u"."EmailVerifiedAtUtc" IS NOT NULL
                    AND "u"."IsActive" = 1
                    AND "u"."Role" = 'Learner'
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Student confirmations cannot safely be reconstructed as historical admin decisions.
        throw new NotSupportedException("Student confirmation policy migration cannot be reversed automatically.");
    }
}
