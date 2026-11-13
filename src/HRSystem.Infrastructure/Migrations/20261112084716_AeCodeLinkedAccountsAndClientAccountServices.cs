using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AeCodeLinkedAccountsAndClientAccountServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParentAeCode",
                table: "ChecklistTaskAeCodes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // The AE Code task moves from Finance & Accounts to Client Account Services & Middle Office. New databases
            // get the template from the seeder; this moves it, its people and its tasks on databases that already have them.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM ChecklistTemplates t JOIN Departments d ON d.Id = t.DepartmentId
           WHERE t.Kind = 2 AND t.TaskType = 1 AND d.Name = N'Finance & Accounts')
BEGIN
    DECLARE @fa int = (SELECT TOP 1 Id FROM Departments WHERE Name = N'Finance & Accounts');
    DECLARE @cas int = (SELECT TOP 1 Id FROM Departments WHERE Name = N'Client Account Services & Middle Office');

    -- When the department exists and has no AE Code template of its own yet.
    IF @cas IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ChecklistTemplates WHERE Kind = 2 AND TaskType = 1 AND DepartmentId = @cas AND IsDeleted = 0)
    BEGIN
        UPDATE ChecklistTemplates SET DepartmentId = @cas WHERE Kind = 2 AND TaskType = 1 AND DepartmentId = @fa;
        UPDATE DepartmentAssignees SET DepartmentId = @cas
        WHERE Area = 3 AND DepartmentId = @fa
          AND NOT EXISTS (SELECT 1 FROM DepartmentAssignees x
                          WHERE x.Area = 3 AND x.DepartmentId = @cas AND x.Role = DepartmentAssignees.Role AND x.UserId = DepartmentAssignees.UserId);
        DELETE FROM DepartmentAssignees WHERE Area = 3 AND DepartmentId = @fa;
        UPDATE ChecklistTasks SET AssignedDepartmentId = @cas, DepartmentName = N'Client Account Services & Middle Office (AE Code)'
        WHERE TaskType = 1 AND AssignedDepartmentId = @fa;
    END
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentAeCode",
                table: "ChecklistTaskAeCodes");
        }
    }
}
