using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestApprovalAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "DepartureRequests",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "ApproverUserId",
                table: "AccountRequests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "AccountRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedBy",
                table: "AccountRequests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionRemark",
                table: "AccountRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "AccountRequests",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequests_ApproverUserId_Status",
                table: "AccountRequests",
                columns: new[] { "ApproverUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountRequests_ApproverUserId_Status",
                table: "AccountRequests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "DepartureRequests");

            migrationBuilder.DropColumn(
                name: "ApproverUserId",
                table: "AccountRequests");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "AccountRequests");

            migrationBuilder.DropColumn(
                name: "DecidedBy",
                table: "AccountRequests");

            migrationBuilder.DropColumn(
                name: "DecisionRemark",
                table: "AccountRequests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "AccountRequests");
        }
    }
}
