using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMilestoneIdToExpenseAndLineItemIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MilestoneId",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuotationLineItems_MilestoneId",
                table: "QuotationLineItems",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLineItems_MilestoneId",
                table: "InvoiceLineItems",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_MilestoneId",
                table: "Expenses",
                column: "MilestoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuotationLineItems_MilestoneId",
                table: "QuotationLineItems");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLineItems_MilestoneId",
                table: "InvoiceLineItems");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_MilestoneId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "MilestoneId",
                table: "Expenses");
        }
    }
}
