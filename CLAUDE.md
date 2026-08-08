# SurveyorLedger: Multi-Tenant SaaS Platform

Survey job management. Register → verify email → create workspace → manage jobs.
**Phase 1: API (✅ complete). Phase 2: Angular UI (🏗️ in progress).**

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
```

## Folders

```
api/src/
  SurveyorLedger.API/         Controllers, Services, Middleware
  SurveyorLedger.Data/        DbContext, Entities, Migrations
  SurveyorLedger.Core/        Constants, Enums, Exceptions
ui/src/app/
  pages/                      Auth, Workspace, Profile
  shell/                      Sidebar, Topbar, CommandPalette
  core/                       Services, Guards, Interceptors
```

## Auth

Register (OTP or password) → Email verification → Login → JWT + refresh cookie.

## Detailed Docs

- **API endpoints & config:** See git history or ask
- **UI specs:** See [UI_IMPLEMENTATION_GUIDE.md](UI_IMPLEMENTATION_GUIDE.md)
- **Coding rules:** See [.claude/rules.md](.claude/rules.md)
- **Architecture:** Clean layers (controllers → services → data layer)
