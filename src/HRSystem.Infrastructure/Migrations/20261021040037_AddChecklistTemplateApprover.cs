using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChecklistTemplateApprover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApproverUserId",
                table: "ChecklistTemplates",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApproverUserId",
                table: "ChecklistTemplates");
        }
    }
}
