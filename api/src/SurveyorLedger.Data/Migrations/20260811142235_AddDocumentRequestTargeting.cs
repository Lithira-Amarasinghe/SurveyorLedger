using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRequestTargeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetRole",
                table: "DocumentRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                table: "DocumentRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_TargetUserId",
                table: "DocumentRequests",
                column: "TargetUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentRequests_TargetExclusive",
                table: "DocumentRequests",
                sql: "[TargetRole] IS NULL OR [TargetUserId] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRequests_Users_TargetUserId",
                table: "DocumentRequests",
                column: "TargetUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRequests_Users_TargetUserId",
                table: "DocumentRequests");

            migrationBuilder.DropIndex(
                name: "IX_DocumentRequests_TargetUserId",
                table: "DocumentRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentRequests_TargetExclusive",
                table: "DocumentRequests");

            migrationBuilder.DropColumn(
                name: "TargetRole",
                table: "DocumentRequests");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "DocumentRequests");
        }
    }
}
