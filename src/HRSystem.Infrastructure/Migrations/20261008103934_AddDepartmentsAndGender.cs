using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentsAndGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ManagerEmployeeId",
                table: "Departments",
                type: "int",
                nullable: true);

            // Department text -> FK, matched by trimmed, case-insensitive name. Unmatched rows
            // stay NULL and are shown as "Unassigned" for HR to fix by hand.
            migrationBuilder.Sql(@"
UPDATE e SET DepartmentId = d.Id
FROM Employees e
JOIN Departments d ON UPPER(d.Name) = UPPER(LTRIM(RTRIM(e.Department)));");

            // Department head (login) -> manager (employee), where that login is linked to an employee.
            migrationBuilder.Sql(@"
UPDATE d SET ManagerEmployeeId = e.Id
FROM Departments d
JOIN Employees e ON e.UserId = d.HeadUserId AND e.IsDeleted = 0;");

            migrationBuilder.Sql(@"
INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue)
SELECT r.Id, 'permission', 'departments.manage'
FROM AspNetRoles r
WHERE r.Name = 'HR'
  AND NOT EXISTS (SELECT 1 FROM AspNetRoleClaims c
                  WHERE c.RoleId = r.Id AND c.ClaimType = 'permission' AND c.ClaimValue = 'departments.manage');");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "HeadUserId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_ManagerEmployeeId",
                table: "Departments",
                column: "ManagerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_Employees_ManagerEmployeeId",
                table: "Departments",
                column: "ManagerEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Departments_DepartmentId",
                table: "Employees",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Departments_Employees_ManagerEmployeeId",
                table: "Departments");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Departments_DepartmentId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Departments_ManagerEmployeeId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Departments_Name",
                table: "Departments");

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Employees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeadUserId",
                table: "Departments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE e SET Department = d.Name
FROM Employees e JOIN Departments d ON d.Id = e.DepartmentId;");

            migrationBuilder.Sql(@"
UPDATE d SET HeadUserId = e.UserId
FROM Departments d JOIN Employees e ON e.Id = d.ManagerEmployeeId;");

            migrationBuilder.Sql("DELETE FROM AspNetRoleClaims WHERE ClaimType = 'permission' AND ClaimValue = 'departments.manage';");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ManagerEmployeeId",
                table: "Departments");
        }
    }
}
