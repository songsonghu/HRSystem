using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartureAeCodeTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChecklistTemplates_Kind_DepartmentId",
                table: "ChecklistTemplates");

            migrationBuilder.AddColumn<int>(
                name: "TaskType",
                table: "ChecklistTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TaskType",
                table: "ChecklistTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChecklistTaskAeCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChecklistTaskId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    AeCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistTaskAeCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChecklistTaskAeCodes_ChecklistTasks_ChecklistTaskId",
                        column: x => x.ChecklistTaskId,
                        principalTable: "ChecklistTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTemplates_Kind_DepartmentId_TaskType",
                table: "ChecklistTemplates",
                columns: new[] { "Kind", "DepartmentId", "TaskType" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTaskAeCodes_ChecklistTaskId_LineNo",
                table: "ChecklistTaskAeCodes",
                columns: new[] { "ChecklistTaskId", "LineNo" });

            // Existing databases: add the Finance & Accounts (AE Code) departure task next to the department's
            // checklist. New databases get it from the seeder, which runs once departments exist.
            migrationBuilder.Sql(@"
INSERT INTO ChecklistTemplates (Kind, DepartmentId, IsActive, IsRequired, SortOrder, TaskType, CreatedBy, CreatedAt, IsDeleted)
SELECT TOP 1 2, d.Id, 1, 1, COALESCE(t.SortOrder, 0), 1, 'system', SYSUTCDATETIME(), 0
FROM Departments d
LEFT JOIN ChecklistTemplates t ON t.DepartmentId = d.Id AND t.Kind = 2 AND t.TaskType = 0 AND t.IsDeleted = 0
WHERE d.Name = N'Finance & Accounts'
  AND EXISTS (SELECT 1 FROM ChecklistTemplates WHERE Kind = 2)
  AND NOT EXISTS (SELECT 1 FROM ChecklistTemplates WHERE Kind = 2 AND TaskType = 1 AND IsDeleted = 0)
ORDER BY d.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Without task types a department has one template of a kind again, and its AE Code people go.
            migrationBuilder.Sql("DELETE FROM ChecklistTemplates WHERE TaskType = 1;");
            migrationBuilder.Sql("DELETE FROM DepartmentAssignees WHERE Area = 3;");

            migrationBuilder.DropTable(
                name: "ChecklistTaskAeCodes");

            migrationBuilder.DropIndex(
                name: "IX_ChecklistTemplates_Kind_DepartmentId_TaskType",
                table: "ChecklistTemplates");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "ChecklistTemplates");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "ChecklistTasks");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTemplates_Kind_DepartmentId",
                table: "ChecklistTemplates",
                columns: new[] { "Kind", "DepartmentId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
