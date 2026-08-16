using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitUserIntoPersonAndUserAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthTokens_Users_UserId",
                table: "AuthTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRequests_Users_FulfilledBy",
                table: "DocumentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRequests_Users_RequestedBy",
                table: "DocumentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRequests_Users_TargetUserId",
                table: "DocumentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Users_UploadedBy",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Users_RecordedBy",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Users_InvitedBy",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Users_UserId",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Clients_ClientId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Users_CreatedBy",
                table: "Jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_LandPhotos_Users_UploadedBy",
                table: "LandPhotos");

            migrationBuilder.DropForeignKey(
                name: "FK_Lands_Users_OwnerId",
                table: "Lands");

            migrationBuilder.DropForeignKey(
                name: "FK_Milestones_Users_CompletedBy",
                table: "Milestones");

            migrationBuilder.DropForeignKey(
                name: "FK_Milestones_Users_CreatedBy",
                table: "Milestones");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Users_RecordedBy",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_Clients_ClientId",
                table: "Quotations");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffPayments_Users_RecordedBy",
                table: "StaffPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffPayments_Users_UserId",
                table: "StaffPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAccesses_Users_UserId",
                table: "UserAccesses");

            migrationBuilder.DropForeignKey(
                name: "FK_Workspaces_Users_OwnerId",
                table: "Workspaces");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Street = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailVerified = table.Column<bool>(type: "bit", nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HasCompletedSignup = table.Column<bool>(type: "bit", nullable: false),
                    FailedLoginAttempts = table.Column<int>(type: "int", nullable: false),
                    LockoutEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAccounts_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_People_CreatedAt",
                table: "People",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_People_Email",
                table: "People",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_People_IsActive",
                table: "People",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_CreatedAt",
                table: "UserAccounts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_IsActive",
                table: "UserAccounts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_PersonId",
                table: "UserAccounts",
                column: "PersonId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_UserAccounts_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "UserAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AuthTokens_UserAccounts_UserId",
                table: "AuthTokens",
                column: "UserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRequests_People_FulfilledBy",
                table: "DocumentRequests",
                column: "FulfilledBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRequests_People_RequestedBy",
                table: "DocumentRequests",
                column: "RequestedBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRequests_People_TargetUserId",
                table: "DocumentRequests",
                column: "TargetUserId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_People_UploadedBy",
                table: "Documents",
                column: "UploadedBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_People_RecordedBy",
                table: "Expenses",
                column: "RecordedBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_People_UserId",
                table: "Invitations",
                column: "UserId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_UserAccounts_InvitedBy",
                table: "Invitations",
                column: "InvitedBy",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_People_ClientId",
                table: "Invoices",
                column: "ClientId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_People_CreatedBy",
                table: "Jobs",
                column: "CreatedBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LandPhotos_People_UploadedBy",
                table: "LandPhotos",
                column: "UploadedBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lands_People_OwnerId",
                table: "Lands",
                column: "OwnerId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Milestones_People_CompletedBy",
                table: "Milestones",
                column: "CompletedBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Milestones_People_CreatedBy",
                table: "Milestones",
                column: "CreatedBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_People_RecordedBy",
                table: "Payments",
                column: "RecordedBy",
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

            migrationBuilder.AddForeignKey(
                name: "FK_StaffPayments_People_RecordedBy",
                table: "StaffPayments",
                column: "RecordedBy",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffPayments_People_UserId",
                table: "StaffPayments",
                column: "UserId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccesses_UserAccounts_UserId",
                table: "UserAccesses",
                column: "UserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Workspaces_UserAccounts_OwnerId",
                table: "Workspaces",
                column: "OwnerId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_UserAccounts_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_AuthTokens_UserAccounts_UserId",
                table: "AuthTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRequests_People_FulfilledBy",
                table: "DocumentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRequests_People_RequestedBy",
                table: "DocumentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRequests_People_TargetUserId",
                table: "DocumentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_People_UploadedBy",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_People_RecordedBy",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_People_UserId",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_UserAccounts_InvitedBy",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_People_ClientId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_People_CreatedBy",
                table: "Jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_LandPhotos_People_UploadedBy",
                table: "LandPhotos");

            migrationBuilder.DropForeignKey(
                name: "FK_Lands_People_OwnerId",
                table: "Lands");

            migrationBuilder.DropForeignKey(
                name: "FK_Milestones_People_CompletedBy",
                table: "Milestones");

            migrationBuilder.DropForeignKey(
                name: "FK_Milestones_People_CreatedBy",
                table: "Milestones");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_People_RecordedBy",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_People_ClientId",
                table: "Quotations");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffPayments_People_RecordedBy",
                table: "StaffPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffPayments_People_UserId",
                table: "StaffPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAccesses_UserAccounts_UserId",
                table: "UserAccesses");

            migrationBuilder.DropForeignKey(
                name: "FK_Workspaces_UserAccounts_OwnerId",
                table: "Workspaces");

            migrationBuilder.DropTable(
                name: "UserAccounts");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Street = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clients_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    EmailVerified = table.Column<bool>(type: "bit", nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HasCompletedSignup = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LockoutEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Street = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_IsActive",
                table: "Clients",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_WorkspaceId",
                table: "Clients",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CreatedAt",
                table: "Users",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsActive",
                table: "Users",
                column: "IsActive");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AuthTokens_Users_UserId",
                table: "AuthTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRequests_Users_FulfilledBy",
                table: "DocumentRequests",
                column: "FulfilledBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRequests_Users_RequestedBy",
                table: "DocumentRequests",
                column: "RequestedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRequests_Users_TargetUserId",
                table: "DocumentRequests",
                column: "TargetUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Users_UploadedBy",
                table: "Documents",
                column: "UploadedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Users_RecordedBy",
                table: "Expenses",
                column: "RecordedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_InvitedBy",
                table: "Invitations",
                column: "InvitedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_UserId",
                table: "Invitations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Clients_ClientId",
                table: "Invoices",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Users_CreatedBy",
                table: "Jobs",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LandPhotos_Users_UploadedBy",
                table: "LandPhotos",
                column: "UploadedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lands_Users_OwnerId",
                table: "Lands",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Milestones_Users_CompletedBy",
                table: "Milestones",
                column: "CompletedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Milestones_Users_CreatedBy",
                table: "Milestones",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_RecordedBy",
                table: "Payments",
                column: "RecordedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_Clients_ClientId",
                table: "Quotations",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffPayments_Users_RecordedBy",
                table: "StaffPayments",
                column: "RecordedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffPayments_Users_UserId",
                table: "StaffPayments",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccesses_Users_UserId",
                table: "UserAccesses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Workspaces_Users_OwnerId",
                table: "Workspaces",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
