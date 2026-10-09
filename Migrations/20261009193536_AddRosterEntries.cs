using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VIIDII.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RosterEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Identifier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Grouping = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    HasClaimed = table.Column<bool>(type: "bit", nullable: false),
                    ClaimToken = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterEntries_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_SessionId_Identifier",
                table: "RosterEntries",
                columns: new[] { "SessionId", "Identifier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RosterEntries");
        }
    }
}
