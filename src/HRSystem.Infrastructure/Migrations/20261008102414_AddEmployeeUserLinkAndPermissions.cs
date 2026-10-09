using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeUserLinkAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Employees",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_UserId",
                table: "Employees",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            // Existing databases: give the pre-existing HR / DeptHead roles the permissions that
            // match their former hard-coded access (fresh installs get these from DbSeeder).
            migrationBuilder.Sql(@"
INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue)
SELECT r.Id, 'permission', p.Name
FROM AspNetRoles r
JOIN (VALUES ('HR', 'employees.manage'),
             ('HR', 'account-requests.manage'),
             ('HR', 'departures.manage'),
             ('HR', 'offboarding.export'),
             ('DeptHead', 'tasks.process')) AS p(RoleName, Name)
  ON p.RoleName = r.Name
WHERE NOT EXISTS (SELECT 1 FROM AspNetRoleClaims c
                  WHERE c.RoleId = r.Id AND c.ClaimType = 'permission' AND c.ClaimValue = p.Name);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM AspNetRoleClaims WHERE ClaimType = 'permission';");

            migrationBuilder.DropIndex(
                name: "IX_Employees_UserId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Employees");
        }
    }
}
