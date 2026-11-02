using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStateLicensingEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StateLicensingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SeedKey = table.Column<string>(type: "TEXT", nullable: true),
                    Question = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: false),
                    SourceNotes = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedQuestion = table.Column<string>(type: "TEXT", nullable: true),
                    PublishedAnswer = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StateLicensingEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StateLicensingEntries_SeedKey",
                table: "StateLicensingEntries",
                column: "SeedKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StateLicensingEntries_State",
                table: "StateLicensingEntries",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StateLicensingEntries");
        }
    }
}
