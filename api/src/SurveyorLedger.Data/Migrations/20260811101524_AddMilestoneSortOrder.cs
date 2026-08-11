using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMilestoneSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Milestones_JobId",
                table: "Milestones");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Milestones",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Milestones_JobId_SortOrder",
                table: "Milestones",
                columns: new[] { "JobId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Milestones_JobId_SortOrder",
                table: "Milestones");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Milestones");

            migrationBuilder.CreateIndex(
                name: "IX_Milestones_JobId",
                table: "Milestones",
                column: "JobId");
        }
    }
}
