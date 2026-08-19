using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameLandDocumentRequestFulfilledDocumentIdToBatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LandDocumentRequests_Documents_FulfilledDocumentId",
                table: "LandDocumentRequests");

            migrationBuilder.DropIndex(
                name: "IX_LandDocumentRequests_FulfilledDocumentId",
                table: "LandDocumentRequests");

            migrationBuilder.RenameColumn(
                name: "FulfilledDocumentId",
                table: "LandDocumentRequests",
                newName: "FulfilledBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FulfilledBatchId",
                table: "LandDocumentRequests",
                newName: "FulfilledDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LandDocumentRequests_FulfilledDocumentId",
                table: "LandDocumentRequests",
                column: "FulfilledDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_LandDocumentRequests_Documents_FulfilledDocumentId",
                table: "LandDocumentRequests",
                column: "FulfilledDocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
