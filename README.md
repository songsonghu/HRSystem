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
| 3 | **Account Provisioning Workflow** | A request fans out into per-account-type items → dispatched to each responsible department's manager → they open the account & submit → master status auto-recomputes (safe under parallel updates). Notifies the employee and each manager. Scanned signed approval upload. |
| 4 | **Account requests (self-service)** | `Add / Remove` requests can be raised by HR (anyone), a department manager (their team, dispatched directly) or an employee for themself — which first needs their department manager's approval or rejection (with reason). |
| 5 | **Onboarding & Departure checklists** | HR starts onboarding (with the accounts to open) or departure (with the accounts to disable — every account the employee holds is ticked) for an employee; the accounts travel as a linked Onboard/Offboard account request; each active department checklist template becomes a task for that department's manager, processed in parallel on My Checklist Tasks; HR finalizes once all required tasks and the linked account request are done (departure then marks the employee resigned). Templates are managed per department on the Checklist Templates page. |
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

Roles are managed under **Administration → Roles & Permissions**; each role is granted a
subset of the fixed permissions in `HRSystem.Application/Security/Permissions.cs` (stored as
role claims). Changes reach signed-in users within about a minute. The built-in `Admin` role
always holds every permission and cannot be renamed or deleted.

| Permission | Grants | Default roles |
|------------|--------|---------------|
| `users.manage` | Users page | Admin |
| `roles.manage` | Roles & Permissions page | Admin |
| `system.jobs` | Hangfire dashboard `/hangfire` | Admin |
| `employees.manage` | Employees | Admin, HR |
| `departments.manage` | Departments and department managers | Admin, HR |
| `account-requests.manage` | Account requests for any employee (Offboard requests come only from departures) | Admin, HR |
| `onboarding.manage` | Onboarding requests and onboarding checklist templates | Admin, HR |
| `departures.manage` | Departure requests and departure checklist templates | Admin, HR |
| `offboarding.export` | Employee account list & Excel export | Admin, HR |

Default roles and users are only seeded into an empty database; after that they are managed in the UI.

Some access comes from the organization data rather than a permission: any user whose login is
linked to an employee can raise Add/Remove account requests for themself; the manager of a
department can raise them for its employees and approves the self-service ones; whoever an item
is assigned to can process it on My Tasks / My Checklist Tasks (no permission needed).

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

- Give every department that owns account types or a departure checklist a manager whose employee record is linked to a login; otherwise requests for it cannot be submitted.
- Configure real SMTP settings under the `Smtp` section.
- Point `FileStorage:RootPath` to a secured SMB/blob location.
- All key actions are written to `AuditLogs` for compliance traceability.
