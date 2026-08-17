using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class PolicyDrivenAccessChaining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PolicyId",
                table: "Roles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "AssignmentPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RulesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScopeParentTypes",
                columns: table => new
                {
                    ScopeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ParentScopeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopeParentTypes", x => x.ScopeType);
                });

            migrationBuilder.InsertData(
                table: "AssignmentPolicies",
                columns: new[] { "Id", "Name", "RulesJson" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000701"), "SingleScope", "{\"ancestors\":[]}" },
                    { new Guid("00000000-0000-0000-0000-000000000702"), "FullChain", "{\"ancestors\":[{\"scopeType\":\"Workspace\",\"grantRoleId\":\"00000000-0000-0000-0000-000000000801\"}]}" }
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000702"));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000702"));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000701"));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000005"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000702"));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000006"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000701"));

            migrationBuilder.InsertData(
                table: "ScopeParentTypes",
                columns: new[] { "ScopeType", "ParentScopeType" },
                values: new object[] { "Job", "Workspace" });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "CreatedAt", "Description", "IsSystem", "Name", "PolicyId", "UpdatedAt" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000801"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Least-privilege membership granted automatically when a role requires workspace-level presence.", true, "WorkspaceMember", new Guid("00000000-0000-0000-0000-000000000701"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "CreatedAt", "PermissionId", "RoleId" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000802"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("00000000-0000-0000-0000-000000000101"), new Guid("00000000-0000-0000-0000-000000000801") });

            migrationBuilder.InsertData(
                table: "RoleScopes",
                columns: new[] { "RoleId", "ScopeType" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000801"), "Workspace" });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_PolicyId",
                table: "Roles",
                column: "PolicyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Roles_AssignmentPolicies_PolicyId",
                table: "Roles",
                column: "PolicyId",
                principalTable: "AssignmentPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Roles_AssignmentPolicies_PolicyId",
                table: "Roles");

            migrationBuilder.DropTable(
                name: "AssignmentPolicies");

            migrationBuilder.DropTable(
                name: "ScopeParentTypes");

            migrationBuilder.DropIndex(
                name: "IX_Roles_PolicyId",
                table: "Roles");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000802"));

            migrationBuilder.DeleteData(
                table: "RoleScopes",
                keyColumns: new[] { "RoleId", "ScopeType" },
                keyValues: new object[] { new Guid("00000000-0000-0000-0000-000000000801"), "Workspace" });

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000801"));

            migrationBuilder.DropColumn(
                name: "PolicyId",
                table: "Roles");
        }
    }
}
