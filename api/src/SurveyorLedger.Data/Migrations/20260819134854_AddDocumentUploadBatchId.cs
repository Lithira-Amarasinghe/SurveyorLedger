using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentUploadBatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UploadBatchId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerType_OwnerId_UploadBatchId",
                table: "Documents",
                columns: new[] { "OwnerType", "OwnerId", "UploadBatchId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_OwnerType_OwnerId_UploadBatchId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "UploadBatchId",
                table: "Documents");
        }
    }
}
