using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VIIDII.Migrations
{
    /// <inheritdoc />
    public partial class DropRosterEntrySessionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_Sessions_SessionId",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_SessionId",
                table: "RosterEntries");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "RosterEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SessionId",
                table: "RosterEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_SessionId",
                table: "RosterEntries",
                column: "SessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_Sessions_SessionId",
                table: "RosterEntries",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id");
        }
    }
}
