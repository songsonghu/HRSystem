# HR System — Design Document

## 1. Overview
The HR System manages the full account lifecycle for employees:

1. **Login / Authorization** (Admin / HR / DeptHead)
2. **Employee Management** (CRUD, soft delete)
3. **Account Provisioning Workflow** (onboarding, multi-party approval/opening)
4. **In-service Add / Remove** account changes
5. **Offboarding** account listing & Excel export

It is built on **ASP.NET Core 8.0** using **Clean Architecture** to keep the
domain pure, the application logic testable, and infrastructure replaceable.

---

## 2. Layered architecture

```
Web (MVC + Razor Pages, Identity)
        │  depends on
Application (DTOs, Interfaces, Services, WorkflowService)
        │  depends on
Domain (Entities, Enums)      ◄── Infrastructure (EF Core, Identity, Email, Files, Reports)
```

- **Domain** has no external dependencies.
- **Application** defines interfaces (`IAppDbContext`, `IEmailService`,
  `IFileStorageService`, `IReportService`, `IAuditService`, `ICurrentUser`) and
  implements use-case services.
- **Infrastructure** implements those interfaces (EF Core `AppDbContext`,
  MailKit/Hangfire email, local file storage, ClosedXML reports).
- **Web** wires everything with DI and hosts controllers/views.

---

## 3. Data model (ER overview)

```
Departments 1───* AccountTypes
Employees   1───* AccountRequests 1───* AccountRequestItems *───1 AccountTypes
AccountRequests 1───* Attachments
Employees   1───* EmployeeAccounts *───1 AccountTypes
AuditLogs (standalone)
AspNetUsers / AspNetRoles (Identity)
```

Key tables:
- **AccountRequest** – master ticket (RequestNo, Type, Status, Employee).
- **AccountRequestItem** – one line per account type, independently handled by
  the responsible department. This is the unit of the parallel workflow.
- **EmployeeAccount** – standing ledger of accounts an employee actually holds;
  the source of truth for offboarding export.
- **AuditLog** – immutable compliance trail.

---

## 4. Account provisioning workflow (Modules 3 & 4)

### 4.1 End-to-end sequence
```
HR creates request (Draft)
   → selects employee, request type, account types, remark
   → uploads scanned signed approval, adds notes
   → clicks "Submit"
        • master status → Submitted
        • each item is assigned to its responsible department head (snapshot)
        • email to the new employee (welcome / acknowledgement)
        • consolidated email to each responsible department head
Department head logs in → "My Tasks"
   → opens account, fills Account Value, sets Status, adds Result Remark → Submit
        • on Completed → write/update EmployeeAccounts ledger
        • WorkflowService recomputes master status
Admin → "Requests" dashboard: sees everyone's status & progress
```

### 4.2 Item state machine (`WorkflowService`)
```
NotStarted ──► WIP ──► Completed (terminal)
     │          │
     ├──► KIV ◄─┘
     └──► Rejected ──► (WIP on appeal)
```
All transitions pass through `WorkflowService.EnsureItemTransition` so illegal
jumps are rejected centrally.

### 4.3 Master status derivation
`WorkflowService.EvaluateRequestStatus(items)`:
- all items Completed/Rejected → **Completed**
- any item WIP/Completed/KIV → **InProgress**
- otherwise → **Submitted**

### 4.4 Account type ↔ department mapping (configurable)
Stored in `AccountTypes.ResponsibleDeptId` — adding a new account type is data
configuration only, no code change.

| Account Type | Responsible Department |
|--------------|------------------------|
| PC Domain Account | IT Infrastructure |
| E-mail Account | IT Mail |
| Ayers Account | Trading Systems |
| IBO Account | Back Office |
| VPN Account | Network Security |

---

## 5. Ledger effects by request type
| RequestType | On item Completed |
|-------------|-------------------|
| Onboard / Add | Insert (or reactivate) an **Active** EmployeeAccount |
| Remove / Offboard | Set the matching EmployeeAccount to **Disabled** |

---

## 6. Offboarding & export (Module 5)
- `OffboardingService.GetActiveAccountsAsync` lists all `Active` accounts.
- `ExportExcelAsync` → `ReportService.BuildOffboardingWorkbook` (ClosedXML)
  produces a formatted **de-provisioning checklist** `.xlsx`.
- Optionally raise an `Offboard` request to drive per-department disabling.

---

## 7. Cross-cutting concerns
- **Security**: ASP.NET Core Identity, role policies (`RequireAdmin/HR/DeptHead`),
  anti-forgery tokens on all POST forms.
- **Async email**: MailKit send scheduled via **Hangfire** (`AutomaticRetry`),
  so user actions are never blocked by SMTP latency.
- **Auditing**: every significant action writes an `AuditLog` row.
- **File storage**: scanned approvals kept outside the DB (metadata only).
- **Logging**: Serilog to console + rolling files.
- **Soft delete**: employees are flagged, never physically removed.

---

## 8. Project layout
```
src/
  HR.Domain/         Common, Enums, Entities
  HR.Application/    Common, DTOs, Interfaces, Services, DependencyInjection
  HR.Infrastructure/ Persistence (DbContext, Configs, Seeder), Identity, Services, DI
  HR.Web/            Controllers, Views, Areas/Identity, Services (CurrentUser), Program.cs
database/            01_create_database.sql, 02_seed_data.sql, 03_useful_queries.sql
docs/                DESIGN.md
```

---

## 9. Extensibility ideas
- Add SSO / AD (LDAP) authentication for corporate networks.
- Add reminders (Hangfire recurring job) for overdue items.
- Add PDF export (QuestPDF) alongside Excel.
- Add REST API surface for integration with other systems.
