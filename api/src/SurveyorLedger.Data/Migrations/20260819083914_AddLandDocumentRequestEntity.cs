using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLandDocumentRequestEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LandDocumentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FulfilledDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FulfilledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FulfilledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ShareToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ShareTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandDocumentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandDocumentRequests_Documents_FulfilledDocumentId",
                        column: x => x.FulfilledDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LandDocumentRequests_Lands_LandId",
                        column: x => x.LandId,
                        principalTable: "Lands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandDocumentRequests_People_FulfilledBy",
                        column: x => x.FulfilledBy,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LandDocumentRequests_People_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LandDocumentRequests_FulfilledBy",
                table: "LandDocumentRequests",
                column: "FulfilledBy");

            migrationBuilder.CreateIndex(
                name: "IX_LandDocumentRequests_FulfilledDocumentId",
                table: "LandDocumentRequests",
                column: "FulfilledDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LandDocumentRequests_LandId",
                table: "LandDocumentRequests",
                column: "LandId");

            migrationBuilder.CreateIndex(
                name: "IX_LandDocumentRequests_RequestedBy",
                table: "LandDocumentRequests",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_LandDocumentRequests_ShareToken",
                table: "LandDocumentRequests",
                column: "ShareToken",
                unique: true,
                filter: "[ShareToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LandDocumentRequests");
        }
    }
}
