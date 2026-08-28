# HR System

An employee onboarding, **account-provisioning workflow**, and offboarding system
built on **ASP.NET Core 8.0 (MVC + Razor Pages)** using **Clean Architecture**.

> Root namespace: `HRSystem` · Comments in English · Front-end: Razor/MVC views.

---

## ✨ Features (5 modules)

| # | Module | Description |
|---|--------|-------------|
| 1 | **Login / Authorization** | ASP.NET Core Identity with 3 roles: `Admin`, `HR`, `DeptHead`. Policy-based authorization. |
| 2 | **Employee Management** | Create / edit / (soft) delete employees, search & filter. |
| 3 | **Account Provisioning Workflow** | HR raises a request → fans out into per-account-type items → dispatched to 5 responsible departments → each opens the account & submits → master status auto-recomputes. Triggers new-employee email + department-head emails. Scanned signed approval upload. |
| 4 | **In-service Add / Remove** | Same request pipeline with `RequestType = Add / Remove`, updating the account ledger. |
| 5 | **Offboarding & Export** | List all active accounts of a leaver and export an Excel de-provisioning checklist (ClosedXML). |

---

## 🏗️ Architecture (Clean Architecture)

```
HRSystem.sln
├─ src/
│  ├─ HR.Domain          # Entities, Enums, base types (no external deps)
│  ├─ HR.Application     # DTOs, Interfaces, Services, Workflow state machine
│  ├─ HR.Infrastructure  # EF Core 8, Identity, Email(MailKit+Hangfire), Files, Reports
│  └─ HR.Web             # MVC Controllers/Views + Identity Razor Pages
├─ database/             # SQL scripts (create / seed / queries)
├─ docs/                 # Design document
├─ HRSystem.sln
├─ .gitignore
└─ README.md
```

Dependency direction: **Web → Application → Domain ← Infrastructure**.

---

## 🧰 Tech Stack

- ASP.NET Core 8.0 MVC + Razor Pages, Bootstrap 5
- Entity Framework Core 8 (Code First) + SQL Server
- ASP.NET Core Identity (roles / policies)
- MailKit + **Hangfire** (background email queue with retries)
- ClosedXML (Excel export)
- Serilog (file + console logging)

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server 2019+ (or SQL Server Express / LocalDB)

### 1. Configure the connection string
Edit `src/HR.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=HRSystem;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

### 2. Restore & create the EF Core migration

```bash
dotnet restore
dotnet tool install --global dotnet-ef      # if not installed

# Create the initial migration (run from repo root)
dotnet ef migrations add InitialCreate \
  --project src/HR.Infrastructure \
  --startup-project src/HR.Web
```

> The app also calls `DbSeeder.SeedAsync` on startup, which runs
> `Database.Migrate()` automatically and seeds roles, the default admin,
> departments, and account types. Hangfire creates its own `HangFire` schema.

### 3. Run

```bash
dotnet run --project src/HR.Web
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

| Area | Admin | HR | DeptHead |
|------|:----:|:--:|:--------:|
| Employees | ✅ | ✅ | – |
| Requests (create/submit/track) | ✅ | ✅ | – |
| My Tasks (open accounts) | ✅ | – | ✅ |
| Offboarding export | ✅ | ✅ | – |
| Hangfire dashboard `/hangfire` | ✅ | – | – |

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
