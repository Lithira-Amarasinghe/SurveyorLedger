using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SurveyorLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMilestonePaymentRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MilestonePaymentRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequiredState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MilestoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MilestonePaymentRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MilestonePaymentRequirements_Milestones_MilestoneId",
                        column: x => x.MilestoneId,
                        principalTable: "Milestones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MilestonePaymentRequirements_MilestoneId",
                table: "MilestonePaymentRequirements",
                column: "MilestoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MilestonePaymentRequirements");
        }
    }
}
