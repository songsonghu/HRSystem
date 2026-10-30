# HR System

An employee onboarding, **account-provisioning workflow**, and offboarding system
built on **ASP.NET Core 8.0 (MVC + Razor Pages)** using **Clean Architecture**.

> Root namespace: `HRSystem` · Comments in English · Front-end: Razor/MVC views.

---

## ✨ Features

| # | Module | Description |
|---|--------|-------------|
| 1 | **Users, Roles & Permissions** | ASP.NET Core Identity. Admins manage users (disable, reset password, link to an employee) and roles; each role is a set of permissions checked by policy. |
| 2 | **Organization & Employees** | Departments with a manager (an employee) and their members; employee records (staff no., name, gender, position, department, email, category, join date, attachments) with search and soft delete. Account and departure tasks for a department are assigned to its manager's login. |
| 3 | **Account Provisioning Workflow** | A request fans out into per-account-type items → each goes to the responsible department, where any of its **handlers** opens/disables the account (Account Checklist) and any of its **approvers** approves or returns it (Account Approval) → master status auto-recomputes once every item is approved (safe under parallel updates). Notifies the employee, handlers and approvers. Scanned signed approval upload. |
| 4 | **Account requests (self-service)** | `Add / Remove` requests can be raised by HR (anyone), a department manager (their team, dispatched directly) or an employee for themself — which first needs their department manager's approval or rejection (with reason). |
| 5 | **Onboarding & Departure** | Onboarding opens the new joiner's accounts (a linked Onboard account request); HR finalizes it once they are approved. A departure disables the accounts the employee holds (linked Offboard request) and gives each department with a departure template a checklist task: its handlers complete it (Departure Checklist), its approvers approve or return it (Departure Approval), then the head of the leaver's department gives the final approval (marking the employee resigned). |
| 6 | **Account list & Export** | List an employee's active accounts and export them to Excel (ClosedXML); linked from the departure page. |

---

## 🏗️ Architecture (Clean Architecture)

```
├─ src/
│  ├─ HRSystem.sln
│  ├─ HRSystem.Domain          # Entities, Enums, base types (no external deps)
│  ├─ HRSystem.Application     # DTOs, validators, interfaces, services, workflow state machine
│  ├─ HRSystem.Infrastructure  # EF Core 8, Identity, Email(MailKit+Hangfire), Files, Reports
│  └─ HRSystem.Web             # MVC Controllers/Views + Identity Razor Pages
├─ database/                   # SQL scripts (create / seed / queries)
├─ docs/                       # Design document
├─ global.json
└─ README.md
```

Dependency direction: **Web → Application → Domain ← Infrastructure**.

---

## 🧰 Tech Stack

- ASP.NET Core 8.0 MVC + Razor Pages, Bootstrap 5
- Entity Framework Core 8 (Code First) + SQL Server
- ASP.NET Core Identity (roles / policies)
- FluentValidation (input validation, `HRSystem.Application/DTOs/Validators`)
- MailKit + **Hangfire** (background email queue with retries)
- ClosedXML (Excel export)
- Serilog (file + console logging)

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server 2019+ (or SQL Server Express / LocalDB)

### 1. Configure the connection string
Edit `src/HRSystem.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=HRSystem;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

### 2. Restore & create the EF Core migration

```bash
dotnet restore src/HRSystem.sln
dotnet tool install --global dotnet-ef      # if not installed

# Add a migration (run from repo root)
dotnet ef migrations add <Name> \
  --project src/HRSystem.Infrastructure \
  --startup-project src/HRSystem.Web
```

> The app also calls `DbSeeder.SeedAsync` on startup, which runs
> `Database.Migrate()` automatically and seeds roles, the default admin,
> departments, and account types. Hangfire creates its own `HangFire` schema.

### 3. Run

```bash
dotnet run --project src/HRSystem.Web
```

Browse to `https://localhost:7080`.

### Default admin login
```
Email:    admin@hrsystem.local
Password: Admin@12345
```

---

## 🧪 Running tests

```bash
dotnet test src/HRSystem.sln
```

`tests/HRSystem.Tests` contains unit tests (no database needed) and integration tests that host the
real app — migrations, seeding, permissions and the account/onboarding/departure workflows,
including deterministic tests for two departments finishing at the same moment. Integration tests
run only when `HRSYSTEM_TEST_SQL` holds a SQL Server connection string whose login may create
databases; each run creates a throw-away `HRSystem_Test_*` database and drops it afterwards.
Otherwise they are reported as skipped.

```powershell
$env:HRSYSTEM_TEST_SQL = "Server=localhost;User Id=...;Password=...;TrustServerCertificate=True"
dotnet test src/HRSystem.sln
```

GitHub Actions (`.github/workflows/ci.yml`) builds and runs all tests, against a SQL Server
container, on every push and pull request.

---

## 🗃️ Database scripts (optional / manual deployment)

If you prefer to create the schema by hand instead of EF migrations, run in order:

1. `database/01_create_database.sql` – creates the database & business tables
2. `database/02_seed_data.sql` – seeds departments, account types, sample employees
3. `database/03_useful_queries.sql` – handy operational queries

> Identity & Hangfire tables are still created by the app at first run.

---

## 🔐 Roles & permissions

Roles are managed under **Settings → Roles & Permissions**; each role is granted a
subset of the fixed permissions in `HRSystem.Application/Security/Permissions.cs` (stored as
role claims). Changes reach signed-in users within about a minute. The built-in `Admin` role
always holds every permission and cannot be renamed or deleted.

| Permission | Grants | Default roles |
|------------|--------|---------------|
| `users.manage` | Users page | Admin |
| `roles.manage` | Roles & Permissions page | Admin |
| `system.jobs` | Hangfire dashboard `/hangfire` | Admin |
| `checklist-approvers.manage` | Choose each department's handlers and approvers on Settings → Templates for Accounts / Templates for Departure (others with access see them read-only) | Admin |
| `employees.manage` | Employees | Admin, HR |
| `departments.manage` | Departments and department managers | Admin, HR |
| `account-requests.manage` | Account requests for any employee (Offboard requests come only from departures); Settings → Templates for Accounts (account types per department) | Admin, HR |
| `onboarding.manage` | Onboarding requests | Admin, HR |
| `departures.manage` | Departure requests; Settings → Templates for Departure | Admin, HR |
| `offboarding.export` | Employee account list & Excel export | Admin, HR |

Default roles and users are only seeded into an empty database; after that they are managed in the UI.

Some access comes from the organization data and the department settings rather than a permission:
any user whose login is linked to an employee can raise Add/Remove account requests for themself, and
the manager of a department can raise them for its employees and approves the self-service ones.

Department work is done by logins, set per department on **Settings → Templates for Accounts** and
**Templates for Departure** (next to the department's account types or checklist):
several **handlers** and several **approvers** for account requests, and again for departure checklists.
Any handler can process the department's work (Account Checklist / Departure Checklist); once it is
completed, any approver approves it or returns it with a reason (Account Approval / Departure Approval).
Approvers can also process work themselves. A department with nobody in a role uses its manager's login.
A departure, once every department task is approved, goes to the head of the leaver's department for
final review under Departure Approval: they approve it (completing it and marking the employee resigned)
or return a department's task with a reason. When the leaver is that head, HR finalizes it instead.
On a departure task every checklist item must be marked either Done or N/A before the task can be
completed. Items ticked "Attachment" in the departure template take one JPG/PNG/PDF file (up to 5 MB),
stored in the `ChecklistTaskItems.AttachmentContent` column and viewable by the department's handlers and
approvers, the final approver and HR.

---

## 🔄 Account request state machine

**Item status:** `NotStarted → WIP → Completed` (with side paths `KIV`, `Rejected`).
**Master status:** `Draft → [PendingApproval →] Submitted → InProgress → Completed → Closed`
(self-service requests pass through `PendingApproval` and may end as `Rejected`). After
dispatch it is recomputed from the aggregate of all items by `WorkflowService`; the request's
row version makes parallel updates by different departments retry instead of overwriting each other.

See `docs/DESIGN.md` for the full flow and ER overview.

---

## 📝 Notes for production

- Give every department that owns account types or a departure checklist handlers and approvers on its Templates for Accounts / Templates for Departure page (or a manager whose employee record is linked to a login); otherwise requests for it cannot be submitted.
- Configure real SMTP settings under the `Smtp` section.
- Point `FileStorage:RootPath` to a secured SMB/blob location.
- All key actions are written to `AuditLogs` for compliance traceability.
