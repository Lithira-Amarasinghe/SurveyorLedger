using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLandLatLngAndPrimaryAddMapViewToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "LandMapPoints");

            migrationBuilder.AddColumn<string>(
                name: "MapViewShareToken",
                table: "Lands",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lands_MapViewShareToken",
                table: "Lands",
                column: "MapViewShareToken",
                unique: true,
                filter: "[MapViewShareToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lands_MapViewShareToken",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "MapViewShareToken",
                table: "Lands");

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Lands",
                type: "decimal(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Lands",
                type: "decimal(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "LandMapPoints",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
