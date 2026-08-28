/* =============================================================================
   HR System - Useful operational queries
   ============================================================================= */

USE [HRSystem];
GO

/* 1) Offboarding: list all active accounts held by an employee (by EmployeeNo). */
DECLARE @EmployeeNo NVARCHAR(50) = N'E0001';

SELECT  at.Name          AS AccountType,
        ea.AccountValue,
        CASE ea.Status WHEN 0 THEN 'Active' ELSE 'Disabled' END AS AccountStatus,
        ea.OpenedAt
FROM    dbo.EmployeeAccounts ea
JOIN    dbo.Employees e     ON e.Id = ea.EmployeeId
JOIN    dbo.AccountTypes at  ON at.Id = ea.AccountTypeId
WHERE   e.EmployeeNo = @EmployeeNo
  AND   ea.Status = 0                 -- Active only
ORDER BY at.SortOrder;
GO

/* 2) Admin dashboard: status of every request with progress. */
SELECT  r.RequestNo,
        e.Name          AS Employee,
        r.RequestType,
        r.Status,
        SUM(CASE WHEN i.Status IN (2,4) THEN 1 ELSE 0 END) AS DoneItems,  -- Completed/Rejected
        COUNT(i.Id)     AS TotalItems,
        r.AppliedAt
FROM    dbo.AccountRequests r
JOIN    dbo.Employees e            ON e.Id = r.EmployeeId
LEFT JOIN dbo.AccountRequestItems i ON i.RequestId = r.Id
GROUP BY r.RequestNo, e.Name, r.RequestType, r.Status, r.AppliedAt
ORDER BY r.AppliedAt DESC;
GO

/* 3) Pending items grouped by responsible department. */
SELECT  d.Name AS Department,
        at.Name AS AccountType,
        e.Name  AS Employee,
        r.RequestNo
FROM    dbo.AccountRequestItems i
JOIN    dbo.AccountRequests r ON r.Id = i.RequestId
JOIN    dbo.Employees e       ON e.Id = r.EmployeeId
JOIN    dbo.AccountTypes at    ON at.Id = i.AccountTypeId
JOIN    dbo.Departments d      ON d.Id = i.AssignedDeptId
WHERE   i.Status NOT IN (2,4)       -- not Completed/Rejected
  AND   r.Status <> 0               -- not Draft
ORDER BY d.Name, r.RequestNo;
GO
