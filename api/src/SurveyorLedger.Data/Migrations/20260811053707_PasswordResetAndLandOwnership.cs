using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class PasswordResetAndLandOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerEmail",
                table: "Lands",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Lands",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerName",
                table: "Lands",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerPhone",
                table: "Lands",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lands_OwnerId",
                table: "Lands",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Lands_Users_OwnerId",
                table: "Lands",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lands_Users_OwnerId",
                table: "Lands");

            migrationBuilder.DropIndex(
                name: "IX_Lands_OwnerId",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "OwnerEmail",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "OwnerName",
                table: "Lands");

            migrationBuilder.DropColumn(
                name: "OwnerPhone",
                table: "Lands");
        }
    }
}
