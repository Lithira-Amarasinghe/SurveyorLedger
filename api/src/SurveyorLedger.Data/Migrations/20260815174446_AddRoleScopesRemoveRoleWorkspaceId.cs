using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleScopesRemoveRoleWorkspaceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Roles");

            migrationBuilder.CreateTable(
                name: "RoleScopes",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleScopes", x => new { x.RoleId, x.ScopeType });
                    table.ForeignKey(
                        name: "FK_RoleScopes_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RoleScopes",
                columns: new[] { "RoleId", "ScopeType" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "Workspace" },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "Job" },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "Workspace" },
                    { new Guid("00000000-0000-0000-0000-000000000004"), "Job" },
                    { new Guid("00000000-0000-0000-0000-000000000005"), "Workspace" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleScopes");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Roles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "WorkspaceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "WorkspaceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"),
                column: "WorkspaceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000005"),
                column: "WorkspaceId",
                value: null);
        }
    }
}
