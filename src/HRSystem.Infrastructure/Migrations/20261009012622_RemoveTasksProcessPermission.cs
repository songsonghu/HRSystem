using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTasksProcessPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Task pages are now open to whoever a task is assigned to, so the permission is gone.
            migrationBuilder.Sql("DELETE FROM AspNetRoleClaims WHERE ClaimType = 'permission' AND ClaimValue = 'tasks.process';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue)
SELECT r.Id, 'permission', 'tasks.process' FROM AspNetRoles r
WHERE r.Name = 'DeptHead'
  AND NOT EXISTS (SELECT 1 FROM AspNetRoleClaims c WHERE c.RoleId = r.Id AND c.ClaimValue = 'tasks.process');");
        }
    }
}
