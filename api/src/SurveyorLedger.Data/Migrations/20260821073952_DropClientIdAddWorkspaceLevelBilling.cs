using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropClientIdAddWorkspaceLevelBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_People_ClientId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_People_ClientId",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_JobId_Number",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_JobId_Number",
                table: "Invoices");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "Quotations",
                newName: "WorkspaceId");

            migrationBuilder.RenameIndex(
                name: "IX_Quotations_ClientId",
                table: "Quotations",
                newName: "IX_Quotations_WorkspaceId");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "Invoices",
                newName: "WorkspaceId");

            migrationBuilder.RenameIndex(
                name: "IX_Invoices_ClientId",
                table: "Invoices",
                newName: "IX_Invoices_WorkspaceId");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "Quotations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_WorkspaceId_Number",
                table: "Quotations",
                columns: new[] { "WorkspaceId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_WorkspaceId_Number",
                table: "Invoices",
                columns: new[] { "WorkspaceId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotations_WorkspaceId_Number",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_WorkspaceId_Number",
                table: "Invoices");

            migrationBuilder.RenameColumn(
                name: "WorkspaceId",
                table: "Quotations",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_Quotations_WorkspaceId",
                table: "Quotations",
                newName: "IX_Quotations_ClientId");

            migrationBuilder.RenameColumn(
                name: "WorkspaceId",
                table: "Invoices",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_Invoices_WorkspaceId",
                table: "Invoices",
                newName: "IX_Invoices_ClientId");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "Quotations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_JobId_Number",
                table: "Quotations",
                columns: new[] { "JobId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_JobId_Number",
                table: "Invoices",
                columns: new[] { "JobId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_People_ClientId",
                table: "Invoices",
                column: "ClientId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_People_ClientId",
                table: "Quotations",
                column: "ClientId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
