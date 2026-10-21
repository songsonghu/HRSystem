using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartureFinalApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "ChecklistTasks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "ChecklistTasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalApproverUserId",
                table: "ChecklistRequests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalApproverUserName",
                table: "ChecklistRequests",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistRequests_FinalApproverUserId_Status",
                table: "ChecklistRequests",
                columns: new[] { "FinalApproverUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChecklistRequests_FinalApproverUserId_Status",
                table: "ChecklistRequests");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "FinalApproverUserId",
                table: "ChecklistRequests");

            migrationBuilder.DropColumn(
                name: "FinalApproverUserName",
                table: "ChecklistRequests");
        }
    }
}
