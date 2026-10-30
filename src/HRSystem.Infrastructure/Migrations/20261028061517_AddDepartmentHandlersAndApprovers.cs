using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <summary>
    /// Departments get several handlers and approvers per area instead of one assignee per task.
    /// Each departure template's approver becomes that department's departure handler, and work that
    /// was already finished counts as approved so open requests can still be finalized.
    /// </summary>
    public partial class AddDepartmentHandlersAndApprovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepartmentAssignees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Area = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentAssignees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentAssignees_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentAssignees_DepartmentId_Area_Role_UserId",
                table: "DepartmentAssignees",
                columns: new[] { "DepartmentId", "Area", "Role", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentAssignees_UserId_Area_Role",
                table: "DepartmentAssignees",
                columns: new[] { "UserId", "Area", "Role" });

            // Departure template approver (Kind 2) -> departure (Area 2) handler (Role 1).
            migrationBuilder.Sql(@"
INSERT INTO DepartmentAssignees (DepartmentId, Area, Role, UserId)
SELECT DISTINCT DepartmentId, 2, 1, ApproverUserId
FROM ChecklistTemplates
WHERE Kind = 2 AND IsDeleted = 0 AND ApproverUserId IS NOT NULL;");

            migrationBuilder.DropIndex(
                name: "IX_ChecklistTasks_AssignedUserId_Status",
                table: "ChecklistTasks");

            migrationBuilder.DropIndex(
                name: "IX_AccountRequestItems_AssignedDeptId",
                table: "AccountRequestItems");

            migrationBuilder.DropColumn(
                name: "ApproverUserId",
                table: "ChecklistTemplates");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "AssignedUserName",
                table: "ChecklistTasks");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "AccountRequestItems");

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "ChecklistTasks",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByName",
                table: "ChecklistTasks",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "ChecklistTasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "AccountRequestItems",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByName",
                table: "AccountRequestItems",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "AccountRequestItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "AccountRequestItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "AccountRequestItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTasks_AssignedDepartmentId_Status",
                table: "ChecklistTasks",
                columns: new[] { "AssignedDepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequestItems_AssignedDeptId_Status",
                table: "AccountRequestItems",
                columns: new[] { "AssignedDeptId", "Status" });

            // Finished before approvals existed: completed / not applicable tasks (2, 4) and
            // completed / rejected items (2, 4) count as approved.
            migrationBuilder.Sql(@"
UPDATE ChecklistTasks
SET ApprovedAt = COALESCE(CompletedAt, SYSUTCDATETIME()), ApprovedByName = N'(before approvals)'
WHERE Status IN (2, 4);

UPDATE AccountRequestItems
SET ApprovedAt = COALESCE(HandledAt, SYSUTCDATETIME()), ApprovedByName = N'(before approvals)'
WHERE Status IN (2, 4);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChecklistTasks_AssignedDepartmentId_Status",
                table: "ChecklistTasks");

            migrationBuilder.DropIndex(
                name: "IX_AccountRequestItems_AssignedDeptId_Status",
                table: "AccountRequestItems");

            migrationBuilder.DropColumn(name: "ApprovedBy", table: "ChecklistTasks");
            migrationBuilder.DropColumn(name: "ApprovedByName", table: "ChecklistTasks");
            migrationBuilder.DropColumn(name: "ApprovedAt", table: "ChecklistTasks");
            migrationBuilder.DropColumn(name: "ApprovedBy", table: "AccountRequestItems");
            migrationBuilder.DropColumn(name: "ApprovedByName", table: "AccountRequestItems");
            migrationBuilder.DropColumn(name: "ApprovedAt", table: "AccountRequestItems");
            migrationBuilder.DropColumn(name: "ReturnReason", table: "AccountRequestItems");
            migrationBuilder.DropColumn(name: "ReturnedAt", table: "AccountRequestItems");

            migrationBuilder.AddColumn<string>(
                name: "ApproverUserId",
                table: "ChecklistTemplates",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedUserId",
                table: "ChecklistTasks",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedUserName",
                table: "ChecklistTasks",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedUserId",
                table: "AccountRequestItems",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.DropTable(
                name: "DepartmentAssignees");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTasks_AssignedUserId_Status",
                table: "ChecklistTasks",
                columns: new[] { "AssignedUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequestItems_AssignedDeptId",
                table: "AccountRequestItems",
                column: "AssignedDeptId");
        }
    }
}
