/* =============================================================================
   HR System - Seed data script
   Purpose: Insert baseline departments and account types.
   Note   : Identity roles (Admin/HR/DeptHead) and the default admin user are
            seeded programmatically by DbSeeder on application startup.
   ============================================================================= */

USE [HRSystem];
GO

/* ----------------------------------------------------------- Departments seed */
IF NOT EXISTS (SELECT 1 FROM dbo.Departments)
BEGIN
    INSERT INTO dbo.Departments (Name, Code, HeadUserId, IsActive)
    VALUES
        (N'IT Infrastructure', N'ITINFRA', N'itinfra.head@hrsystem.local',    1),
        (N'IT Mail',           N'ITMAIL',  N'itmail.head@hrsystem.local',     1),
        (N'Trading Systems',   N'TRADE',   N'trade.head@hrsystem.local',      1),
        (N'Back Office',       N'BACKOFC', N'backoffice.head@hrsystem.local', 1),
        (N'Network Security',  N'NETSEC',  N'netsec.head@hrsystem.local',     1);
END
GO

/* --------------------------------------------------------- AccountTypes seed */
IF NOT EXISTS (SELECT 1 FROM dbo.AccountTypes)
BEGIN
    INSERT INTO dbo.AccountTypes (Code, Name, ResponsibleDeptId, SortOrder, IsActive)
    SELECT N'PCDomain', N'PC Domain Account', d.Id, 1, 1 FROM dbo.Departments d WHERE d.Code = N'ITINFRA'
    UNION ALL
    SELECT N'Email',    N'E-mail Account',    d.Id, 2, 1 FROM dbo.Departments d WHERE d.Code = N'ITMAIL'
    UNION ALL
    SELECT N'Ayers',    N'Ayers Account',     d.Id, 3, 1 FROM dbo.Departments d WHERE d.Code = N'TRADE'
    UNION ALL
    SELECT N'IBO',      N'IBO Account',       d.Id, 4, 1 FROM dbo.Departments d WHERE d.Code = N'BACKOFC'
    UNION ALL
    SELECT N'VPN',      N'VPN Account',       d.Id, 5, 1 FROM dbo.Departments d WHERE d.Code = N'NETSEC';
END
GO

/* ------------------------------------------------------------ Sample employees */
IF NOT EXISTS (SELECT 1 FROM dbo.Employees)
BEGIN
    INSERT INTO dbo.Employees (EmployeeNo, Name, Email, Department, Position, JoinDate, Status)
    VALUES
        (N'E0001', N'John Chan',   N'john.chan@example.com',   N'Finance', N'Analyst',       SYSUTCDATETIME(), 0),
        (N'E0002', N'Mary Wong',   N'mary.wong@example.com',   N'IT',      N'Developer',     SYSUTCDATETIME(), 0);
END
GO

PRINT 'HRSystem seed data inserted successfully.';
GO
