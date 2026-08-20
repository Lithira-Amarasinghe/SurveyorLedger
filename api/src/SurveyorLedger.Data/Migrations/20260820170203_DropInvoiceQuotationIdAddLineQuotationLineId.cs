using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropInvoiceQuotationIdAddLineQuotationLineId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Quotations_QuotationId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_QuotationId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "QuotationId",
                table: "Invoices");

            migrationBuilder.AddColumn<Guid>(
                name: "QuotationLineId",
                table: "InvoiceLineItems",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuotationLineId",
                table: "InvoiceLineItems");

            migrationBuilder.AddColumn<Guid>(
                name: "QuotationId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuotationId",
                table: "Invoices",
                column: "QuotationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Quotations_QuotationId",
                table: "Invoices",
                column: "QuotationId",
                principalTable: "Quotations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
