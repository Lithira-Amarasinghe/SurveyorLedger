using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLandDocumentRequestOwnerTypeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "LandDocumentRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "OwnerType",
                table: "LandDocumentRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_LandDocumentRequests_OwnerType_OwnerId",
                table: "LandDocumentRequests",
                columns: new[] { "OwnerType", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LandDocumentRequests_OwnerType_OwnerId",
                table: "LandDocumentRequests");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "LandDocumentRequests");

            migrationBuilder.DropColumn(
                name: "OwnerType",
                table: "LandDocumentRequests");
        }
    }
}
