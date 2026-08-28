using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRSystem.Infrastructure.Migrations
{
    /// <summary>
    /// InitialCreate —— HR 账号开通管理系统初始数据库结构。
    /// 对应设计文档 ER 图：Department / AppUser / Role / UserRole / Employee /
    /// AccountType / AccountRequest / AccountRequestItem / EmployeeAccount /
    /// Attachment / Notification / AuditLog 共 12 张业务表。
    ///
    /// 注意：Department.ManagerUserId → AppUser、AppUser.EmployeeId → Employee、
    /// Employee.DepartmentId → Department 之间存在循环外键。为规避 SQL Server
    /// “multiple cascade paths” 限制，所有外键均使用 ReferentialAction.Restrict，
    /// 并将 Department.ManagerUserId 的外键延后到建表之后单独添加。
    /// </summary>
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============================================================
            // 1. Role（角色表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleId);
                });

            // ============================================================
            // 2. Department（部门表）—— ManagerUserId 外键延后添加
            // ============================================================
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    DepartmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ManagerUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.DepartmentId);
                });

            // ============================================================
            // 3. Employee（员工表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    EmployeeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    HireDate = table.Column<DateTime>(type: "date", nullable: true),
                    ResignDate = table.Column<DateTime>(type: "date", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.EmployeeId);
                    table.ForeignKey(
                        name: "FK_Employees_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ============================================================
            // 4. AppUser（系统账号表 / Identity）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AppUsers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ============================================================
            // 5. UserRole（用户-角色关联表，复合主键）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            // ============================================================
            // 6. AccountType（账号类型表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "AccountTypes",
                columns: table => new
                {
                    AccountTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RespDepartmentId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTypes", x => x.AccountTypeId);
                    table.ForeignKey(
                        name: "FK_AccountTypes_Departments_RespDepartmentId",
                        column: x => x.RespDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ============================================================
            // 7. AccountRequest（账号申请主表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "AccountRequests",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    // 0=Onboard, 1=Add, 2=Remove, 3=Offboard
                    RequestType = table.Column<byte>(type: "tinyint", nullable: false),
                    // 0=Draft,1=Submitted,2=InProgress,3=Completed,4=Cancelled
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountRequests", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_AccountRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountRequests_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ============================================================
            // 8. AccountRequestItem（申请明细 / 部门任务表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "AccountRequestItems",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    AccountTypeId = table.Column<int>(type: "int", nullable: false),
                    // 0=Create, 1=Delete
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    // 0=Pending,1=InProgress,2=Provisioned,3=Confirmed,4=Rejected
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    HandledByUserId = table.Column<int>(type: "int", nullable: true),
                    HandledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountRequestItems", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_AccountRequestItems_AccountRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AccountRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountRequestItems_AccountTypes_AccountTypeId",
                        column: x => x.AccountTypeId,
                        principalTable: "AccountTypes",
                        principalColumn: "AccountTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountRequestItems_AppUsers_HandledByUserId",
                        column: x => x.HandledByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ============================================================
            // 9. EmployeeAccount（员工已开通账号表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "EmployeeAccounts",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    AccountTypeId = table.Column<int>(type: "int", nullable: false),
                    AccountIdentifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    // 0=Active, 1=Disabled
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAccounts", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_EmployeeAccounts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeAccounts_AccountTypes_AccountTypeId",
                        column: x => x.AccountTypeId,
                        principalTable: "AccountTypes",
                        principalColumn: "AccountTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ============================================================
            // 10. Attachment（附件 / 扫描件表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    AttachmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_Attachments_AccountRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AccountRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            // ============================================================
            // 11. Notification（邮件通知记录表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    // 0=NewHireWelcome, 1=DeptManagerAlert, 2=Reminder, 3=Completed
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    // 0=Pending, 1=Sent, 2=Failed
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_Notifications_AccountRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AccountRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            // ============================================================
            // 12. AuditLog（操作审计表）
            // ============================================================
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditId);
                    table.ForeignKey(
                        name: "FK_AuditLogs_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            // ============================================================
            // 13. 延后添加 Department.ManagerUserId 外键（打破循环依赖）
            // ============================================================
            migrationBuilder.AddForeignKey(
                name: "FK_Departments_AppUsers_ManagerUserId",
                table: "Departments",
                column: "ManagerUserId",
                principalTable: "AppUsers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            // ============================================================
            // 索引 —— 唯一约束与外键索引
            // ============================================================
            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_ManagerUserId",
                table: "Departments",
                column: "ManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_EmployeeNo",
                table: "Employees",
                column: "EmployeeNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_UserName",
                table: "AppUsers",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Email",
                table: "AppUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EmployeeId",
                table: "AppUsers",
                column: "EmployeeId",
                unique: true,
                filter: "[EmployeeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTypes_RespDepartmentId",
                table: "AccountTypes",
                column: "RespDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequests_EmployeeId",
                table: "AccountRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequests_CreatedByUserId",
                table: "AccountRequests",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequests_Status",
                table: "AccountRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequestItems_RequestId",
                table: "AccountRequestItems",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequestItems_AccountTypeId",
                table: "AccountRequestItems",
                column: "AccountTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequestItems_HandledByUserId",
                table: "AccountRequestItems",
                column: "HandledByUserId");

            // 同一申请下同一账号类型不允许重复
            migrationBuilder.CreateIndex(
                name: "UX_AccountRequestItems_Request_Type",
                table: "AccountRequestItems",
                columns: new[] { "RequestId", "AccountTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAccounts_EmployeeId",
                table: "EmployeeAccounts",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAccounts_AccountTypeId",
                table: "EmployeeAccounts",
                column: "AccountTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_RequestId",
                table: "Attachments",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RequestId",
                table: "Notifications",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 先删除打破循环依赖的外键，避免删除 AppUsers 时报错
            migrationBuilder.DropForeignKey(
                name: "FK_Departments_AppUsers_ManagerUserId",
                table: "Departments");

            // 按依赖逆序删除表
            migrationBuilder.DropTable(name: "AuditLogs");
            migrationBuilder.DropTable(name: "Notifications");
            migrationBuilder.DropTable(name: "Attachments");
            migrationBuilder.DropTable(name: "EmployeeAccounts");
            migrationBuilder.DropTable(name: "AccountRequestItems");
            migrationBuilder.DropTable(name: "AccountRequests");
            migrationBuilder.DropTable(name: "AccountTypes");
            migrationBuilder.DropTable(name: "UserRoles");
            migrationBuilder.DropTable(name: "Roles");
            migrationBuilder.DropTable(name: "AppUsers");
            migrationBuilder.DropTable(name: "Employees");
            migrationBuilder.DropTable(name: "Departments");
        }
    }
}
