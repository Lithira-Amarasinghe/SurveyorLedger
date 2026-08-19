using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameDocumentRequestFulfilledDocumentIdToBatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRequests_Documents_FulfilledDocumentId",
                table: "DocumentRequests");

            migrationBuilder.DropIndex(
                name: "IX_DocumentRequests_FulfilledDocumentId",
                table: "DocumentRequests");

            migrationBuilder.RenameColumn(
                name: "FulfilledDocumentId",
                table: "DocumentRequests",
                newName: "FulfilledBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FulfilledBatchId",
                table: "DocumentRequests",
                newName: "FulfilledDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_FulfilledDocumentId",
                table: "DocumentRequests",
                column: "FulfilledDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRequests_Documents_FulfilledDocumentId",
                table: "DocumentRequests",
                column: "FulfilledDocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
