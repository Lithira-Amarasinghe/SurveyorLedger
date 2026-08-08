using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class FilterUserAccessUniqueIndexToActiveOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccesses_UserId_RoleId_ScopeType_ScopeId",
                table: "UserAccesses");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccesses_UserId_RoleId_ScopeType_ScopeId",
                table: "UserAccesses",
                columns: new[] { "UserId", "RoleId", "ScopeType", "ScopeId" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccesses_UserId_RoleId_ScopeType_ScopeId",
                table: "UserAccesses");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccesses_UserId_RoleId_ScopeType_ScopeId",
                table: "UserAccesses",
                columns: new[] { "UserId", "RoleId", "ScopeType", "ScopeId" },
                unique: true);
        }
    }
}
