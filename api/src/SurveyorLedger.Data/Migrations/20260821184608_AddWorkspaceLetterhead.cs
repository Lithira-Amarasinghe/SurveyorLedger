using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceLetterhead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LetterheadAddress",
                table: "Workspaces",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetterheadCompanyName",
                table: "Workspaces",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetterheadEmail",
                table: "Workspaces",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetterheadLogoPath",
                table: "Workspaces",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetterheadPhone",
                table: "Workspaces",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetterheadRegistrationNumber",
                table: "Workspaces",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LetterheadAddress",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "LetterheadCompanyName",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "LetterheadEmail",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "LetterheadLogoPath",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "LetterheadPhone",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "LetterheadRegistrationNumber",
                table: "Workspaces");
        }
    }
}
