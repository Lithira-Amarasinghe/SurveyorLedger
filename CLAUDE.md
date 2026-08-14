# SurveyorLedger: Multi-Tenant SaaS Platform

Survey job management. Register → verify email → create workspace → manage jobs, land records, documents, milestones.
Check `ui/src/app/pages/` for pages built so far, `api/src/SurveyorLedger.API/Controllers/` for endpoints — don't assume from this doc.
Feature history & design rationale: `docs/superpowers/plans/` and `docs/superpowers/specs/`.

## Stack

**Backend:** .NET 9, SQL Server LocalDB, EF Core 9, Casbin.NET 2.0, Azure ACS, JWT
**Frontend:** Angular 21, Material, Tailwind, RxJS

Multi-tenant: shared DB, workspace = tenant, query filters + TenantMiddleware for isolation.

## Quick Start

```bash
# LocalDB (once)
sqllocaldb start MSSQLLocalDB

# Terminal 1: API (localhost:5296)
cd api && dotnet run --project src/SurveyorLedger.API

# Terminal 2: UI (localhost:4200)
cd ui && ng serve

# Tests (scoped, not full suite per-change — see .claude/rules.md)
dotnet test --filter ClassName   # api
ng test --include **/component.spec.ts  # ui
```

## Folders

```
api/src/
  SurveyorLedger.API/         Controllers, Services, Middleware
  SurveyorLedger.Data/        DbContext, Entities, Migrations
  SurveyorLedger.Core/        Constants, Enums, Exceptions
ui/src/app/
  pages/                      Feature pages (one folder per feature)
  shell/                      Sidebar, Topbar, CommandPalette
  core/                       Services, Guards, Interceptors
  shared/                     Reusable components
```

## Auth

Register (OTP or password) → Email verification → Login → JWT + refresh cookie.

## Detailed Docs

- **API endpoints:** See `api/src/SurveyorLedger.API/Controllers/`
- **UI specs:** See [UI_IMPLEMENTATION_GUIDE.md](UI_IMPLEMENTATION_GUIDE.md)
- **Coding rules:** See [.claude/rules.md](.claude/rules.md)
- **Architecture:** Clean layers (controllers → services → data layer)
