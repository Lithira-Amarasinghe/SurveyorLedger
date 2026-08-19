using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSizeWithUnifiedArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Size",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "SizeUnit",
                table: "Lands");

            migrationBuilder.AddColumn<decimal>(
                name: "AreaSquareMeters",
                table: "Lands",
                type: "decimal(14,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreaSquareMeters",
                table: "Lands");

            migrationBuilder.AddColumn<decimal>(
                name: "Size",
                table: "Lands",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SizeUnit",
                table: "Lands",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
