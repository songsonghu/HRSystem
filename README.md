# HR System

An employee onboarding, **account-provisioning workflow**, and offboarding system
built on **ASP.NET Core 8.0 (MVC + Razor Pages)** using **Clean Architecture**.

> Root namespace: `HRSystem` · Comments in English · Front-end: Razor/MVC views.

---

## ✨ Features (5 modules)

| # | Module | Description |
|---|--------|-------------|
| 1 | **Users, Roles & Permissions** | ASP.NET Core Identity. Admins manage users (disable, reset password, link to an employee) and roles; each role is a set of permissions checked by policy. |
| 2 | **Employee Management** | Create / edit / (soft) delete employees, search & filter. |
| 3 | **Account Provisioning Workflow** | HR raises a request → fans out into per-account-type items → dispatched to 5 responsible departments → each opens the account & submits → master status auto-recomputes. Triggers new-employee email + department-head emails. Scanned signed approval upload. |
| 4 | **In-service Add / Remove** | Same request pipeline with `RequestType = Add / Remove`, updating the account ledger. |
| 5 | **Offboarding & Export** | List all active accounts of a leaver and export an Excel de-provisioning checklist (ClosedXML). |

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
| `account-requests.manage` | Account requests (create / submit / track) | Admin, HR |
| `departures.manage` | Departure requests (create / submit / finalize) | Admin, HR |
| `offboarding.export` | Employee account list & Excel export | Admin, HR |
| `tasks.process` | My Tasks / My Departure Tasks | Admin, DeptHead |

Default roles and users are only seeded into an empty database; after that they are managed in the UI.

---

## 🔄 Account request state machine

**Item status:** `NotStarted → WIP → Completed` (with side paths `KIV`, `Rejected`).
**Master status:** `Draft → Submitted → InProgress → Completed → Closed`, recomputed
from the aggregate of all items by `WorkflowService`.

See `docs/DESIGN.md` for the full flow and ER overview.

---

## 📝 Notes for production

- Replace placeholder department `HeadUserId` emails with real Identity users.
- Configure real SMTP settings under the `Smtp` section.
- Point `FileStorage:RootPath` to a secured SMB/blob location.
- All key actions are written to `AuditLogs` for compliance traceability.
