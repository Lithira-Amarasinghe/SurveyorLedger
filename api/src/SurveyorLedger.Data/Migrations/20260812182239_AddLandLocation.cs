using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLandLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Lands",
                type: "decimal(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationShareToken",
                table: "Lands",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Lands",
                type: "decimal(9,6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lands_LocationShareToken",
                table: "Lands",
                column: "LocationShareToken",
                unique: true,
                filter: "[LocationShareToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lands_LocationShareToken",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "LocationShareToken",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Lands");
        }
    }
}
