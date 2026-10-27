using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChecklistItemAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAttachment",
                table: "ChecklistTemplateItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowAttachment",
                table: "ChecklistTaskItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "AttachmentContent",
                table: "ChecklistTaskItems",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "ChecklistTaskItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentFileName",
                table: "ChecklistTaskItems",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AttachmentSize",
                table: "ChecklistTaskItems",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowAttachment",
                table: "ChecklistTemplateItems");

            migrationBuilder.DropColumn(
                name: "AllowAttachment",
                table: "ChecklistTaskItems");

            migrationBuilder.DropColumn(
                name: "AttachmentContent",
                table: "ChecklistTaskItems");

            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "ChecklistTaskItems");

            migrationBuilder.DropColumn(
                name: "AttachmentFileName",
                table: "ChecklistTaskItems");

            migrationBuilder.DropColumn(
                name: "AttachmentSize",
                table: "ChecklistTaskItems");
        }
    }
}
