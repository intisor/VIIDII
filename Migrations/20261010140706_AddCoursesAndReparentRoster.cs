using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VIIDII.Migrations
{
    /// <inheritdoc />
    public partial class AddCoursesAndReparentRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_Sessions_SessionId",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_SessionId_Identifier",
                table: "RosterEntries");

            migrationBuilder.AddColumn<int>(
                name: "CourseId",
                table: "Sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SessionId",
                table: "RosterEntries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "CourseId",
                table: "RosterEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LecturerId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    JoinPrompt = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RosterMappingJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Courses_Users_LecturerId",
                        column: x => x.LecturerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_CourseId",
                table: "Sessions",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_CourseId_Identifier",
                table: "RosterEntries",
                columns: new[] { "CourseId", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_SessionId",
                table: "RosterEntries",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Code",
                table: "Courses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_LecturerId",
                table: "Courses",
                column: "LecturerId");

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_Courses_CourseId",
                table: "RosterEntries",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_Sessions_SessionId",
                table: "RosterEntries",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Courses_CourseId",
                table: "Sessions",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_Courses_CourseId",
                table: "RosterEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_Sessions_SessionId",
                table: "RosterEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Courses_CourseId",
                table: "Sessions");

            migrationBuilder.DropTable(
                name: "Courses");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_CourseId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_CourseId_Identifier",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_SessionId",
                table: "RosterEntries");

            migrationBuilder.DropColumn(
                name: "CourseId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "CourseId",
                table: "RosterEntries");

            migrationBuilder.AlterColumn<int>(
                name: "SessionId",
                table: "RosterEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_SessionId_Identifier",
                table: "RosterEntries",
                columns: new[] { "SessionId", "Identifier" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_Sessions_SessionId",
                table: "RosterEntries",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
