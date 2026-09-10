using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountTypeLayoutMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Column",
                table: "AccountTypes",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "GroupName",
                table: "AccountTypes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasCheckbox",
                table: "AccountTypes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PrefixText",
                table: "AccountTypes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuffixText",
                table: "AccountTypes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Column",
                table: "AccountTypes");

            migrationBuilder.DropColumn(
                name: "GroupName",
                table: "AccountTypes");

            migrationBuilder.DropColumn(
                name: "HasCheckbox",
                table: "AccountTypes");

            migrationBuilder.DropColumn(
                name: "PrefixText",
                table: "AccountTypes");

            migrationBuilder.DropColumn(
                name: "SuffixText",
                table: "AccountTypes");
        }
    }
}
