using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvitationScopeGenericRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Users_UserId",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Workspaces_WorkspaceId",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_WorkspaceId_Email",
                table: "Invitations");

            migrationBuilder.RenameColumn(
                name: "WorkspaceId",
                table: "Invitations",
                newName: "ScopeId");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "Invitations",
                newName: "ScopeType");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Invitations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                table: "Invitations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_RoleId",
                table: "Invitations",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ScopeType_ScopeId_Email",
                table: "Invitations",
                columns: new[] { "ScopeType", "ScopeId", "Email" });

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Roles_RoleId",
                table: "Invitations",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_UserId",
                table: "Invitations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Roles_RoleId",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Users_UserId",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_RoleId",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_ScopeType_ScopeId_Email",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "Invitations");

            migrationBuilder.RenameColumn(
                name: "ScopeType",
                table: "Invitations",
                newName: "Role");

            migrationBuilder.RenameColumn(
                name: "ScopeId",
                table: "Invitations",
                newName: "WorkspaceId");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Invitations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_WorkspaceId_Email",
                table: "Invitations",
                columns: new[] { "WorkspaceId", "Email" });

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_UserId",
                table: "Invitations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Workspaces_WorkspaceId",
                table: "Invitations",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
