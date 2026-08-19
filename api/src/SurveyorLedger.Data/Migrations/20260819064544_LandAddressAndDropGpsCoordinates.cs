using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class LandAddressAndDropGpsCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "GpsCoordinates",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "Lands");

            migrationBuilder.AlterColumn<string>(
                name: "District",
                table: "Lands",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DivisionalSecretariat",
                table: "Lands",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GramaNiladhariDivision",
                table: "Lands",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hatpattu",
                table: "Lands",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Korale",
                table: "Lands",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PradeshiyaSabha",
                table: "Lands",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "Lands",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Village",
                table: "Lands",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DivisionalSecretariat",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "GramaNiladhariDivision",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Hatpattu",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Korale",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "PradeshiyaSabha",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Village",
                table: "Lands");

            migrationBuilder.AlterColumn<string>(
                name: "District",
                table: "Lands",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Lands",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Lands",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GpsCoordinates",
                table: "Lands",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Lands",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Lands",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }
    }
}
