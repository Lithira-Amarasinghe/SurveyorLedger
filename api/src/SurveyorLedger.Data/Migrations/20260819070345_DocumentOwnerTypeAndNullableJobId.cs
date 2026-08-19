using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentOwnerTypeAndNullableJobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerType",
                table: "Documents",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerType_OwnerId",
                table: "Documents",
                columns: new[] { "OwnerType", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_OwnerType_OwnerId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "OwnerType",
                table: "Documents");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
