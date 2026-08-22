using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReseedPoliciesForOrganizationChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AssignmentPolicies",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000701"),
                column: "RulesJson",
                value: "{\"grants\":{}}");

            migrationBuilder.UpdateData(
                table: "AssignmentPolicies",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000702"),
                column: "RulesJson",
                value: "{\"grants\":{\"Workspace\":\"00000000-0000-0000-0000-000000000801\",\"Organization\":\"00000000-0000-0000-0000-000000000010\"}}");

            migrationBuilder.InsertData(
                table: "AssignmentPolicies",
                columns: new[] { "Id", "Name", "RulesJson" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000703"), "OrgOnly", "{\"grants\":{\"Organization\":\"00000000-0000-0000-0000-000000000010\"}}" });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000703"));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000006"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000703"));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000801"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000702"));

            migrationBuilder.InsertData(
                table: "ScopeParentTypes",
                columns: new[] { "ScopeType", "ParentScopeType" },
                values: new object[] { "Workspace", "Organization" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AssignmentPolicies",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000703"));

            migrationBuilder.DeleteData(
                table: "ScopeParentTypes",
                keyColumn: "ScopeType",
                keyValue: "Workspace");

            migrationBuilder.UpdateData(
                table: "AssignmentPolicies",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000701"),
                column: "RulesJson",
                value: "{\"ancestors\":[]}");

            migrationBuilder.UpdateData(
                table: "AssignmentPolicies",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000702"),
                column: "RulesJson",
                value: "{\"ancestors\":[{\"scopeType\":\"Workspace\",\"grantRoleId\":\"00000000-0000-0000-0000-000000000801\"}]}");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000004"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000701"));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000006"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000701"));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000801"),
                column: "PolicyId",
                value: new Guid("00000000-0000-0000-0000-000000000701"));
        }
    }
}
