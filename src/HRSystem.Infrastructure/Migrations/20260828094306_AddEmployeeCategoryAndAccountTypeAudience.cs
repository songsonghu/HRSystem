using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCategoryAndAccountTypeAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Audience",
                table: "AccountTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DetailLabel",
                table: "AccountTypes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresDetail",
                table: "AccountTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsNewHeadcount",
                table: "AccountRequests",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDay",
                table: "AccountRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplacementOf",
                table: "AccountRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestDetail",
                table: "AccountRequestItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "AccountTypes");

            migrationBuilder.DropColumn(
                name: "DetailLabel",
                table: "AccountTypes");

            migrationBuilder.DropColumn(
                name: "RequiresDetail",
                table: "AccountTypes");

            migrationBuilder.DropColumn(
                name: "IsNewHeadcount",
                table: "AccountRequests");

            migrationBuilder.DropColumn(
                name: "LastDay",
                table: "AccountRequests");

            migrationBuilder.DropColumn(
                name: "ReplacementOf",
                table: "AccountRequests");

            migrationBuilder.DropColumn(
                name: "RequestDetail",
                table: "AccountRequestItems");
        }
    }
}
