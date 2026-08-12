using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRequestShareLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "DocumentRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShareTokenExpiresAt",
                table: "DocumentRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_ShareToken",
                table: "DocumentRequests",
                column: "ShareToken",
                unique: true,
                filter: "[ShareToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentRequests_ShareToken",
                table: "DocumentRequests");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "DocumentRequests");

            migrationBuilder.DropColumn(
                name: "ShareTokenExpiresAt",
                table: "DocumentRequests");
        }
    }
}
