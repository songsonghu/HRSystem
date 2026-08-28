/* =============================================================================
   HR System - Database creation script
   Target : Microsoft SQL Server 2019+
   Purpose: Create the HRSystem database and all business tables.
   Note   : ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, ...) and the
            HangFire schema are created automatically by EF Core migrations /
            Hangfire at first run. This script covers the business tables so the
            schema can also be reviewed / deployed independently.
   ============================================================================= */

IF DB_ID('HRSystem') IS NULL
BEGIN
    CREATE DATABASE [HRSystem];
END
GO

USE [HRSystem];
GO

/* ---------------------------------------------------------------- Departments */
IF OBJECT_ID('dbo.Departments', 'U') IS NULL
CREATE TABLE dbo.Departments
(
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
    Name        NVARCHAR(100)  NOT NULL,
    Code        NVARCHAR(50)   NULL,
    HeadUserId  NVARCHAR(450)  NULL,          -- Identity user id / email of dept head
    IsActive    BIT            NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT(1),
    CreatedBy   NVARCHAR(450)  NULL,
    CreatedAt   DATETIME2      NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT(SYSUTCDATETIME()),
    ModifiedBy  NVARCHAR(450)  NULL,
    ModifiedAt  DATETIME2      NULL,
    IsDeleted   BIT            NOT NULL CONSTRAINT DF_Departments_IsDeleted DEFAULT(0)
);
GO

/* --------------------------------------------------------------- AccountTypes */
IF OBJECT_ID('dbo.AccountTypes', 'U') IS NULL
CREATE TABLE dbo.AccountTypes
(
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountTypes PRIMARY KEY,
    Code               NVARCHAR(50)  NOT NULL,
    Name               NVARCHAR(100) NOT NULL,
    Description        NVARCHAR(500) NULL,
    ResponsibleDeptId  INT           NOT NULL,
    IsActive           BIT           NOT NULL CONSTRAINT DF_AccountTypes_IsActive DEFAULT(1),
    SortOrder          INT           NOT NULL CONSTRAINT DF_AccountTypes_SortOrder DEFAULT(0),
    CreatedBy          NVARCHAR(450) NULL,
    CreatedAt          DATETIME2     NOT NULL CONSTRAINT DF_AccountTypes_CreatedAt DEFAULT(SYSUTCDATETIME()),
    ModifiedBy         NVARCHAR(450) NULL,
    ModifiedAt         DATETIME2     NULL,
    IsDeleted          BIT           NOT NULL CONSTRAINT DF_AccountTypes_IsDeleted DEFAULT(0),
    CONSTRAINT UQ_AccountTypes_Code UNIQUE (Code),
    CONSTRAINT FK_AccountTypes_Departments FOREIGN KEY (ResponsibleDeptId)
        REFERENCES dbo.Departments(Id)
);
GO

/* ------------------------------------------------------------------ Employees */
IF OBJECT_ID('dbo.Employees', 'U') IS NULL
CREATE TABLE dbo.Employees
(
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    EmployeeNo  NVARCHAR(50)  NOT NULL,
    Name        NVARCHAR(100) NOT NULL,
    Email       NVARCHAR(200) NULL,
    Department  NVARCHAR(100) NULL,
    Position    NVARCHAR(100) NULL,
    JoinDate    DATETIME2     NOT NULL,
    ResignDate  DATETIME2     NULL,
    Status      INT           NOT NULL CONSTRAINT DF_Employees_Status DEFAULT(0), -- 0=Active,1=Resigned
    CreatedBy   NVARCHAR(450) NULL,
    CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_Employees_CreatedAt DEFAULT(SYSUTCDATETIME()),
    ModifiedBy  NVARCHAR(450) NULL,
    ModifiedAt  DATETIME2     NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Employees_IsDeleted DEFAULT(0),
    CONSTRAINT UQ_Employees_EmployeeNo UNIQUE (EmployeeNo)
);
GO

/* ------------------------------------------------------------- AccountRequests */
IF OBJECT_ID('dbo.AccountRequests', 'U') IS NULL
CREATE TABLE dbo.AccountRequests
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountRequests PRIMARY KEY,
    RequestNo    NVARCHAR(30)  NOT NULL,
    EmployeeId   INT           NOT NULL,
    RequestType  INT           NOT NULL,   -- 0=Onboard,1=Add,2=Remove,3=Offboard
    Status       INT           NOT NULL CONSTRAINT DF_AccountRequests_Status DEFAULT(0), -- 0=Draft...4=Closed
    Remark       NVARCHAR(1000) NULL,
    AppliedBy    NVARCHAR(450) NULL,
    AppliedAt    DATETIME2     NULL,
    CompletedAt  DATETIME2     NULL,
    CreatedBy    NVARCHAR(450) NULL,
    CreatedAt    DATETIME2     NOT NULL CONSTRAINT DF_AccountRequests_CreatedAt DEFAULT(SYSUTCDATETIME()),
    ModifiedBy   NVARCHAR(450) NULL,
    ModifiedAt   DATETIME2     NULL,
    IsDeleted    BIT           NOT NULL CONSTRAINT DF_AccountRequests_IsDeleted DEFAULT(0),
    CONSTRAINT UQ_AccountRequests_RequestNo UNIQUE (RequestNo),
    CONSTRAINT FK_AccountRequests_Employees FOREIGN KEY (EmployeeId)
        REFERENCES dbo.Employees(Id)
);
GO

/* --------------------------------------------------------- AccountRequestItems */
IF OBJECT_ID('dbo.AccountRequestItems', 'U') IS NULL
CREATE TABLE dbo.AccountRequestItems
(
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountRequestItems PRIMARY KEY,
    RequestId       INT           NOT NULL,
    AccountTypeId   INT           NOT NULL,
    AssignedDeptId  INT           NOT NULL,
    AssignedUserId  NVARCHAR(450) NULL,
    Status          INT           NOT NULL CONSTRAINT DF_ARI_Status DEFAULT(0), -- 0=NotStarted...4=Rejected
    AccountValue    NVARCHAR(200) NULL,
    ResultRemark    NVARCHAR(1000) NULL,
    HandledBy       NVARCHAR(450) NULL,
    HandledAt       DATETIME2     NULL,
    CreatedBy       NVARCHAR(450) NULL,
    CreatedAt       DATETIME2     NOT NULL CONSTRAINT DF_ARI_CreatedAt DEFAULT(SYSUTCDATETIME()),
    ModifiedBy      NVARCHAR(450) NULL,
    ModifiedAt      DATETIME2     NULL,
    IsDeleted       BIT           NOT NULL CONSTRAINT DF_ARI_IsDeleted DEFAULT(0),
    CONSTRAINT FK_ARI_Requests FOREIGN KEY (RequestId)
        REFERENCES dbo.AccountRequests(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ARI_AccountTypes FOREIGN KEY (AccountTypeId)
        REFERENCES dbo.AccountTypes(Id),
    CONSTRAINT FK_ARI_Departments FOREIGN KEY (AssignedDeptId)
        REFERENCES dbo.Departments(Id)
);
GO

/* ----------------------------------------------------------------- Attachments */
IF OBJECT_ID('dbo.Attachments', 'U') IS NULL
CREATE TABLE dbo.Attachments
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attachments PRIMARY KEY,
    RequestId    INT           NOT NULL,
    FileName     NVARCHAR(260) NOT NULL,
    FilePath     NVARCHAR(500) NOT NULL,
    ContentType  NVARCHAR(120) NULL,
    FileSize     BIGINT        NOT NULL CONSTRAINT DF_Attachments_FileSize DEFAULT(0),
    CreatedBy    NVARCHAR(450) NULL,
    CreatedAt    DATETIME2     NOT NULL CONSTRAINT DF_Attachments_CreatedAt DEFAULT(SYSUTCDATETIME()),
    ModifiedBy   NVARCHAR(450) NULL,
    ModifiedAt   DATETIME2     NULL,
    IsDeleted    BIT           NOT NULL CONSTRAINT DF_Attachments_IsDeleted DEFAULT(0),
    CONSTRAINT FK_Attachments_Requests FOREIGN KEY (RequestId)
        REFERENCES dbo.AccountRequests(Id) ON DELETE CASCADE
);
GO

/* ------------------------------------------------------------- EmployeeAccounts */
IF OBJECT_ID('dbo.EmployeeAccounts', 'U') IS NULL
CREATE TABLE dbo.EmployeeAccounts
(
    Id                    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeAccounts PRIMARY KEY,
    EmployeeId            INT           NOT NULL,
    AccountTypeId         INT           NOT NULL,
    AccountValue          NVARCHAR(200) NULL,
    Status                INT           NOT NULL CONSTRAINT DF_EA_Status DEFAULT(0), -- 0=Active,1=Disabled
    OpenedAt              DATETIME2     NOT NULL CONSTRAINT DF_EA_OpenedAt DEFAULT(SYSUTCDATETIME()),
    DisabledAt            DATETIME2     NULL,
    SourceRequestItemId   INT           NULL,
    CreatedBy             NVARCHAR(450) NULL,
    CreatedAt             DATETIME2     NOT NULL CONSTRAINT DF_EA_CreatedAt DEFAULT(SYSUTCDATETIME()),
    ModifiedBy            NVARCHAR(450) NULL,
    ModifiedAt            DATETIME2     NULL,
    IsDeleted             BIT           NOT NULL CONSTRAINT DF_EA_IsDeleted DEFAULT(0),
    CONSTRAINT FK_EA_Employees FOREIGN KEY (EmployeeId)
        REFERENCES dbo.Employees(Id) ON DELETE CASCADE,
    CONSTRAINT FK_EA_AccountTypes FOREIGN KEY (AccountTypeId)
        REFERENCES dbo.AccountTypes(Id)
);
GO
CREATE INDEX IX_EmployeeAccounts_Emp_Type ON dbo.EmployeeAccounts (EmployeeId, AccountTypeId);
GO

/* ------------------------------------------------------------------- AuditLogs */
IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
CREATE TABLE dbo.AuditLogs
(
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
    UserId      NVARCHAR(450) NULL,
    UserName    NVARCHAR(256) NULL,
    Action      NVARCHAR(100) NOT NULL,
    EntityName  NVARCHAR(100) NULL,
    EntityId    NVARCHAR(50)  NULL,
    Detail      NVARCHAR(MAX) NULL,
    Timestamp   DATETIME2     NOT NULL CONSTRAINT DF_AuditLogs_Timestamp DEFAULT(SYSUTCDATETIME())
);
GO
CREATE INDEX IX_AuditLogs_Timestamp ON dbo.AuditLogs (Timestamp);
GO

PRINT 'HRSystem business tables created successfully.';
GO
