using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VIIDII.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterColumnMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RosterIdentifierPrompt",
                table: "Sessions",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RosterMappingJson",
                table: "Sessions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RosterIdentifierPrompt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "RosterMappingJson",
                table: "Sessions");
        }
    }
}
