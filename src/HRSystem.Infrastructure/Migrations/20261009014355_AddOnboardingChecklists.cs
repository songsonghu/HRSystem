using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <summary>
    /// Generalizes the departure checklist into onboarding + departure checklists by renaming
    /// the Departure* tables (existing rows become Kind = Departure) and keying templates by
    /// department id instead of name.
    /// </summary>
    public partial class AddOnboardingChecklists : Migration
    {
        private static readonly (string Table, string Fk)[] OldForeignKeys =
        {
            ("DepartureRequests", "FK_DepartureRequests_Employees_EmployeeId"),
            ("DepartureTasks", "FK_DepartureTasks_DepartureRequests_DepartureRequestId"),
            ("DepartureTaskItems", "FK_DepartureTaskItems_DepartureTasks_DepartureTaskId"),
            ("DepartureTaskTemplateItems", "FK_DepartureTaskTemplateItems_DepartureTaskTemplates_DepartureTaskTemplateId"),
        };

        private static readonly (string Table, string Index)[] OldIndexes =
        {
            ("DepartureRequests", "IX_DepartureRequests_EmployeeId_Status"),
            ("DepartureRequests", "IX_DepartureRequests_RequestNo"),
            ("DepartureTasks", "IX_DepartureTasks_AssignedUserId_Status"),
            ("DepartureTasks", "IX_DepartureTasks_DepartureRequestId_SortOrder"),
            ("DepartureTaskItems", "IX_DepartureTaskItems_DepartureTaskId_SortOrder"),
            ("DepartureTaskTemplates", "IX_DepartureTaskTemplates_DepartmentName_IsDeleted"),
            ("DepartureTaskTemplateItems", "IX_DepartureTaskTemplateItems_DepartureTaskTemplateId_SortOrder"),
        };

        private static readonly (string Old, string New)[] Tables =
        {
            ("DepartureRequests", "ChecklistRequests"),
            ("DepartureTasks", "ChecklistTasks"),
            ("DepartureTaskItems", "ChecklistTaskItems"),
            ("DepartureTaskTemplates", "ChecklistTemplates"),
            ("DepartureTaskTemplateItems", "ChecklistTemplateItems"),
        };

        private static readonly (string Table, string Old, string New)[] Columns =
        {
            ("ChecklistTasks", "DepartureRequestId", "ChecklistRequestId"),
            ("ChecklistTaskItems", "DepartureTaskId", "ChecklistTaskId"),
            ("ChecklistTemplateItems", "DepartureTaskTemplateId", "ChecklistTemplateId"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, fk) in OldForeignKeys) migrationBuilder.DropForeignKey(fk, table);
            foreach (var (table, index) in OldIndexes) migrationBuilder.DropIndex(index, table);
            foreach (var (oldName, _) in Tables) migrationBuilder.DropPrimaryKey($"PK_{oldName}", oldName);

            foreach (var (oldName, newName) in Tables) migrationBuilder.RenameTable(oldName, newName: newName);
            foreach (var (table, oldName, newName) in Columns) migrationBuilder.RenameColumn(oldName, table, newName);
            foreach (var (_, newName) in Tables) migrationBuilder.AddPrimaryKey($"PK_{newName}", newName, "Id");

            // --- requests: kind, onboarding fields, departure dates become optional
            migrationBuilder.AddColumn<int>(name: "Kind", table: "ChecklistRequests", type: "int", nullable: false, defaultValue: 2);
            migrationBuilder.AddColumn<DateTime>(name: "StartDate", table: "ChecklistRequests", type: "datetime2", nullable: true);
            migrationBuilder.AddColumn<int>(name: "AccountRequestId", table: "ChecklistRequests", type: "int", nullable: true);
            migrationBuilder.AlterColumn<DateTime>(name: "LastWorkingDate", table: "ChecklistRequests", type: "datetime2", nullable: true,
                oldClrType: typeof(DateTime), oldType: "datetime2");
            migrationBuilder.AlterColumn<DateTime>(name: "LastEmploymentDate", table: "ChecklistRequests", type: "datetime2", nullable: true,
                oldClrType: typeof(DateTime), oldType: "datetime2");

            // --- templates: kind, and department by id instead of by name
            migrationBuilder.AddColumn<int>(name: "Kind", table: "ChecklistTemplates", type: "int", nullable: false, defaultValue: 2);
            migrationBuilder.AddColumn<int>(name: "DepartmentId", table: "ChecklistTemplates", type: "int", nullable: true);
            migrationBuilder.Sql(@"
INSERT INTO Departments (Name, IsActive, IsDeleted, CreatedAt, CreatedBy)
SELECT DISTINCT t.DepartmentName, 1, 0, SYSUTCDATETIME(), 'migration'
FROM ChecklistTemplates t
WHERE NOT EXISTS (SELECT 1 FROM Departments d WHERE d.Name = t.DepartmentName);

UPDATE t SET DepartmentId = d.Id
FROM ChecklistTemplates t JOIN Departments d ON d.Name = t.DepartmentName;");
            migrationBuilder.AlterColumn<int>(name: "DepartmentId", table: "ChecklistTemplates", type: "int", nullable: false,
                oldClrType: typeof(int), oldType: "int", oldNullable: true);
            migrationBuilder.DropColumn(name: "DepartmentName", table: "ChecklistTemplates");

            // --- indexes and foreign keys under the new names
            migrationBuilder.CreateIndex(name: "IX_ChecklistRequests_AccountRequestId", table: "ChecklistRequests", column: "AccountRequestId");
            migrationBuilder.CreateIndex(name: "IX_ChecklistRequests_EmployeeId", table: "ChecklistRequests", column: "EmployeeId");
            migrationBuilder.CreateIndex(name: "IX_ChecklistRequests_Kind_EmployeeId_Status", table: "ChecklistRequests", columns: new[] { "Kind", "EmployeeId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_ChecklistRequests_RequestNo", table: "ChecklistRequests", column: "RequestNo", unique: true);
            migrationBuilder.CreateIndex(name: "IX_ChecklistTaskItems_ChecklistTaskId_SortOrder", table: "ChecklistTaskItems", columns: new[] { "ChecklistTaskId", "SortOrder" });
            migrationBuilder.CreateIndex(name: "IX_ChecklistTasks_AssignedUserId_Status", table: "ChecklistTasks", columns: new[] { "AssignedUserId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_ChecklistTasks_ChecklistRequestId_SortOrder", table: "ChecklistTasks", columns: new[] { "ChecklistRequestId", "SortOrder" });
            migrationBuilder.CreateIndex(name: "IX_ChecklistTemplateItems_ChecklistTemplateId_SortOrder", table: "ChecklistTemplateItems", columns: new[] { "ChecklistTemplateId", "SortOrder" });
            migrationBuilder.CreateIndex(name: "IX_ChecklistTemplates_DepartmentId", table: "ChecklistTemplates", column: "DepartmentId");
            migrationBuilder.CreateIndex(name: "IX_ChecklistTemplates_Kind_DepartmentId", table: "ChecklistTemplates", columns: new[] { "Kind", "DepartmentId" }, unique: true, filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(name: "FK_ChecklistRequests_Employees_EmployeeId", table: "ChecklistRequests", column: "EmployeeId",
                principalTable: "Employees", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_ChecklistRequests_AccountRequests_AccountRequestId", table: "ChecklistRequests", column: "AccountRequestId",
                principalTable: "AccountRequests", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_ChecklistTasks_ChecklistRequests_ChecklistRequestId", table: "ChecklistTasks", column: "ChecklistRequestId",
                principalTable: "ChecklistRequests", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(name: "FK_ChecklistTaskItems_ChecklistTasks_ChecklistTaskId", table: "ChecklistTaskItems", column: "ChecklistTaskId",
                principalTable: "ChecklistTasks", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(name: "FK_ChecklistTemplates_Departments_DepartmentId", table: "ChecklistTemplates", column: "DepartmentId",
                principalTable: "Departments", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_ChecklistTemplateItems_ChecklistTemplates_ChecklistTemplateId", table: "ChecklistTemplateItems", column: "ChecklistTemplateId",
                principalTable: "ChecklistTemplates", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(@"
INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue)
SELECT r.Id, 'permission', 'onboarding.manage'
FROM AspNetRoles r
WHERE r.Name = 'HR'
  AND NOT EXISTS (SELECT 1 FROM AspNetRoleClaims c
                  WHERE c.RoleId = r.Id AND c.ClaimType = 'permission' AND c.ClaimValue = 'onboarding.manage');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Onboarding data has no place in the old departure-only schema.
            migrationBuilder.Sql("DELETE FROM ChecklistRequests WHERE Kind = 1;");
            migrationBuilder.Sql("DELETE FROM ChecklistTemplates WHERE Kind = 1;");
            migrationBuilder.Sql("DELETE FROM AspNetRoleClaims WHERE ClaimType = 'permission' AND ClaimValue = 'onboarding.manage';");

            migrationBuilder.DropForeignKey("FK_ChecklistRequests_Employees_EmployeeId", "ChecklistRequests");
            migrationBuilder.DropForeignKey("FK_ChecklistRequests_AccountRequests_AccountRequestId", "ChecklistRequests");
            migrationBuilder.DropForeignKey("FK_ChecklistTasks_ChecklistRequests_ChecklistRequestId", "ChecklistTasks");
            migrationBuilder.DropForeignKey("FK_ChecklistTaskItems_ChecklistTasks_ChecklistTaskId", "ChecklistTaskItems");
            migrationBuilder.DropForeignKey("FK_ChecklistTemplates_Departments_DepartmentId", "ChecklistTemplates");
            migrationBuilder.DropForeignKey("FK_ChecklistTemplateItems_ChecklistTemplates_ChecklistTemplateId", "ChecklistTemplateItems");

            migrationBuilder.DropIndex("IX_ChecklistRequests_AccountRequestId", "ChecklistRequests");
            migrationBuilder.DropIndex("IX_ChecklistRequests_EmployeeId", "ChecklistRequests");
            migrationBuilder.DropIndex("IX_ChecklistRequests_Kind_EmployeeId_Status", "ChecklistRequests");
            migrationBuilder.DropIndex("IX_ChecklistRequests_RequestNo", "ChecklistRequests");
            migrationBuilder.DropIndex("IX_ChecklistTaskItems_ChecklistTaskId_SortOrder", "ChecklistTaskItems");
            migrationBuilder.DropIndex("IX_ChecklistTasks_AssignedUserId_Status", "ChecklistTasks");
            migrationBuilder.DropIndex("IX_ChecklistTasks_ChecklistRequestId_SortOrder", "ChecklistTasks");
            migrationBuilder.DropIndex("IX_ChecklistTemplateItems_ChecklistTemplateId_SortOrder", "ChecklistTemplateItems");
            migrationBuilder.DropIndex("IX_ChecklistTemplates_DepartmentId", "ChecklistTemplates");
            migrationBuilder.DropIndex("IX_ChecklistTemplates_Kind_DepartmentId", "ChecklistTemplates");

            migrationBuilder.AddColumn<string>(name: "DepartmentName", table: "ChecklistTemplates", type: "nvarchar(100)", maxLength: 100, nullable: true);
            migrationBuilder.Sql("UPDATE t SET DepartmentName = d.Name FROM ChecklistTemplates t JOIN Departments d ON d.Id = t.DepartmentId;");
            migrationBuilder.AlterColumn<string>(name: "DepartmentName", table: "ChecklistTemplates", type: "nvarchar(100)", maxLength: 100, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100, oldNullable: true);
            migrationBuilder.DropColumn(name: "DepartmentId", table: "ChecklistTemplates");
            migrationBuilder.DropColumn(name: "Kind", table: "ChecklistTemplates");

            migrationBuilder.Sql("UPDATE ChecklistRequests SET LastWorkingDate = ISNULL(LastWorkingDate, CreatedAt), LastEmploymentDate = ISNULL(LastEmploymentDate, CreatedAt);");
            migrationBuilder.AlterColumn<DateTime>(name: "LastWorkingDate", table: "ChecklistRequests", type: "datetime2", nullable: false,
                oldClrType: typeof(DateTime), oldType: "datetime2", oldNullable: true);
            migrationBuilder.AlterColumn<DateTime>(name: "LastEmploymentDate", table: "ChecklistRequests", type: "datetime2", nullable: false,
                oldClrType: typeof(DateTime), oldType: "datetime2", oldNullable: true);
            migrationBuilder.DropColumn(name: "AccountRequestId", table: "ChecklistRequests");
            migrationBuilder.DropColumn(name: "StartDate", table: "ChecklistRequests");
            migrationBuilder.DropColumn(name: "Kind", table: "ChecklistRequests");

            foreach (var (_, newName) in Tables) migrationBuilder.DropPrimaryKey($"PK_{newName}", newName);
            foreach (var (table, oldName, newName) in Columns) migrationBuilder.RenameColumn(newName, table, oldName);
            foreach (var (oldName, newName) in Tables) migrationBuilder.RenameTable(newName, newName: oldName);
            foreach (var (oldName, _) in Tables) migrationBuilder.AddPrimaryKey($"PK_{oldName}", oldName, "Id");

            migrationBuilder.CreateIndex(name: "IX_DepartureRequests_EmployeeId_Status", table: "DepartureRequests", columns: new[] { "EmployeeId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_DepartureRequests_RequestNo", table: "DepartureRequests", column: "RequestNo", unique: true);
            migrationBuilder.CreateIndex(name: "IX_DepartureTaskItems_DepartureTaskId_SortOrder", table: "DepartureTaskItems", columns: new[] { "DepartureTaskId", "SortOrder" });
            migrationBuilder.CreateIndex(name: "IX_DepartureTasks_AssignedUserId_Status", table: "DepartureTasks", columns: new[] { "AssignedUserId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_DepartureTasks_DepartureRequestId_SortOrder", table: "DepartureTasks", columns: new[] { "DepartureRequestId", "SortOrder" });
            migrationBuilder.CreateIndex(name: "IX_DepartureTaskTemplateItems_DepartureTaskTemplateId_SortOrder", table: "DepartureTaskTemplateItems", columns: new[] { "DepartureTaskTemplateId", "SortOrder" });
            migrationBuilder.CreateIndex(name: "IX_DepartureTaskTemplates_DepartmentName_IsDeleted", table: "DepartureTaskTemplates", columns: new[] { "DepartmentName", "IsDeleted" }, unique: true);

            migrationBuilder.AddForeignKey(name: "FK_DepartureRequests_Employees_EmployeeId", table: "DepartureRequests", column: "EmployeeId",
                principalTable: "Employees", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_DepartureTasks_DepartureRequests_DepartureRequestId", table: "DepartureTasks", column: "DepartureRequestId",
                principalTable: "DepartureRequests", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(name: "FK_DepartureTaskItems_DepartureTasks_DepartureTaskId", table: "DepartureTaskItems", column: "DepartureTaskId",
                principalTable: "DepartureTasks", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(name: "FK_DepartureTaskTemplateItems_DepartureTaskTemplates_DepartureTaskTemplateId", table: "DepartureTaskTemplateItems", column: "DepartureTaskTemplateId",
                principalTable: "DepartureTaskTemplates", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        }
    }
}
