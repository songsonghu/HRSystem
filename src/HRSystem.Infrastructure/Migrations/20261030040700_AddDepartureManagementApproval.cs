using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartureManagementApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChecklistRequestLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChecklistRequestId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Departments = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistRequestLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChecklistRequestLogs_ChecklistRequests_ChecklistRequestId",
                        column: x => x.ChecklistRequestId,
                        principalTable: "ChecklistRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistRequestLogs_ChecklistRequestId_At",
                table: "ChecklistRequestLogs",
                columns: new[] { "ChecklistRequestId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistRequestLogs_UserId_Action",
                table: "ChecklistRequestLogs",
                columns: new[] { "UserId", "Action" });

            // HR gives the new last approval by default; other roles (e.g. Managers) are granted it in the UI.
            migrationBuilder.Sql(@"
INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue)
SELECT r.Id, 'permission', 'departures.approve'
FROM AspNetRoles r
WHERE r.Name = 'HR'
  AND NOT EXISTS (SELECT 1 FROM AspNetRoleClaims c
                  WHERE c.RoleId = r.Id AND c.ClaimType = 'permission' AND c.ClaimValue = 'departures.approve');");

            // Departures of department heads waiting for HR to finalize now wait for HR / management approval.
            migrationBuilder.Sql("UPDATE ChecklistRequests SET Status = 6 WHERE Kind = 2 AND Status = 3 AND FinalApproverUserId IS NULL;");

            // Start the log of existing requests with the steps their columns still record.
            migrationBuilder.Sql(@"
INSERT INTO ChecklistRequestLogs (ChecklistRequestId, Action, Remark, UserId, UserName, At)
SELECT r.Id, 1, NULL, r.SubmittedBy, COALESCE(u.FullName, u.UserName), r.SubmittedAt
FROM ChecklistRequests r LEFT JOIN AspNetUsers u ON u.Id = r.SubmittedBy
WHERE r.SubmittedAt IS NOT NULL;

INSERT INTO ChecklistRequestLogs (ChecklistRequestId, Action, Remark, UserId, UserName, At)
SELECT r.Id, 9, r.CancelReason, r.CancelledBy, COALESCE(u.FullName, u.UserName), r.CancelledAt
FROM ChecklistRequests r LEFT JOIN AspNetUsers u ON u.Id = r.CancelledBy
WHERE r.CancelledAt IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Without the HR / management step, waiting departures go back to the head, or to HR when there is none.
            migrationBuilder.Sql("UPDATE ChecklistRequests SET Status = 3 WHERE Kind = 2 AND Status = 6;");
            migrationBuilder.Sql("DELETE FROM AspNetRoleClaims WHERE ClaimType = 'permission' AND ClaimValue = 'departures.approve';");

            migrationBuilder.DropTable(
                name: "ChecklistRequestLogs");
        }
    }
}
