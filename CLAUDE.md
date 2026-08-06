# SurveyorLedger: Multi-Tenant SaaS Platform

## Project

Survey job management platform. Users register → verify email (OTP/password) → create workspace → manage jobs within workspace. **Phase 1: Backend API (complete). Phase 2: Angular UI (in progress).**

## Tech Stack

**Backend (API)**
- **.NET 9** (monolithic API, running on http://localhost:5296)
- **SQL Server 2022 LocalDB** (shared multi-tenant DB)
- **EF Core 9** (ORM, migrations)
- **Casbin.NET 2.0.0** (RBAC enforcement)
- **Azure Communication Services** (email OTP)
- **JWT + httpOnly cookies** (hybrid auth)
- **xUnit** (testing)

**Frontend (UI)**
- **Angular 21** (standalone components, signals)
- **Angular Material** (base UI components)
- **Tailwind CSS** (utility density, custom spacing)
- **RxJS** (state management, HTTP)
- **ng serve** (localhost:4200)

## Architecture

Single .NET 9 API, clean architecture layers:
- **Controllers** → API endpoints
- **Services** → business logic (Auth, Email, Token, RBAC)
- **Data layer** → EF Core DbContext, entities, migrations
- **Models** → DTOs, requests, responses

Multi-tenant: shared DB, tenant isolation via query filters + middleware. Workspace = tenant.

## Key Patterns

**Authentication:** Register (OTP or password) → Email verification → Login → JWT token + refresh cookie.

**RBAC:** Casbin enforces (user, role, permission, scope). Scopes: Workspace, Job, Organization (extensible). System roles: Admin, Manager, Surveyor, Client.

**Data isolation:** TenantMiddleware extracts WorkspaceId from request header/token, queries filtered by scope. Users see only their workspaces + assigned jobs.

**Error handling:** Custom AppException → ErrorHandlingMiddleware → JSON {code, message, details}.

## Folders

```
api/                          Backend .NET project
├── src/
│   ├── SurveyorLedger.API/      Controllers, Services, Models, Middleware
│   ├── SurveyorLedger.Data/     DbContext, Entities, Migrations
│   └── SurveyorLedger.Core/     Constants, Enums, Exceptions
├── tests/
│   └── SurveyorLedger.API.Tests/
└── SurveyorLedger.sln

ui/                           Frontend Angular project
├── src/
│   ├── app/                     Components, pages, guards, interceptors
│   ├── assets/
│   └── styles.scss              Global Tailwind imports
├── angular.json
└── package.json
```

## UI Design (Phase 2)

**Style:** Strategic Minimalism via component libraries (no custom patterns).

**Palette:**
- Base: Grays (#f5f5f5 light, #1f1f1f dark)
- Accent (primary buttons, focus): #9E0031 (vibrant wine red)
- Secondary/danger variants: #8E0045, #770058, #600047, #44001A (muted burgundy scale)

**Density:** High — tight typography, crisp functional borders, compact spacing.

**Layout:**
- Collapsible sidebar (left, hidden on mobile)
- Global topbar (logo, user menu, Cmd+K search)
- Command palette (Cmd+K) for navigation

**Progressive Disclosure:** Settings in tabs/modals, not inline; hide optional fields.

## UI Scope (Phase 2)

Pages implemented:
- **Auth:** Register, Verify OTP, Login (no social auth, no password reset)
- **App Shell:** Sidebar, topbar, command palette skeleton
- **Workspace:** List view, Create modal
- **Profile:** User profile view/edit

**NOT implemented** (API support missing): Jobs, Surveys, RBAC UI, Org management, Billing.

## Database

**Schema:** Users, Workspaces, Subscriptions, Roles, Permissions, RolePermissions, UserAccess (polymorphic scope), AuthTokens, EmailVerifications, AuditLogs.

**Migrations:** EF Code-First, migrations tracked in `Migrations/` folder.

**Query filters:** User.IsActive, Workspace.IsActive, UserAccess.IsActive soft-delete.

## Development

**LocalDB Setup (one-time):**
```bash
sqllocaldb create MSSQLLocalDB      # if needed
sqllocaldb start MSSQLLocalDB
```

**API (terminal 1):**
```bash
cd api
dotnet build
dotnet test
dotnet run --project src/SurveyorLedger.API
# Runs on http://localhost:5296
```

**UI (terminal 2):**
```bash
cd ui
npm install                        # if node_modules missing
ng serve
# Runs on http://localhost:4200
```

**Migrations:**
```bash
cd api
dotnet ef database update -p src/SurveyorLedger.Data -s src/SurveyorLedger.API
```

## Endpoints (By Task)

**Auth:** POST /api/auth/register, /api/auth/login, /api/auth/verify-otp, /api/auth/refresh-token

**User:** GET /api/users/profile, PUT /api/users/profile

**Workspace:** GET /api/workspaces, POST /api/workspaces/{id}, POST /api/workspaces

**Subscription:** GET /api/workspaces/{id}/subscription

## Config

`appsettings.json`:
- ConnectionStrings.DefaultConnection → SQL Server
- JwtSettings → key, issuer, audience, expiration
- AzureCommunicationServices → endpoint, key, sender email
- OTP → expiration (minutes), max attempts

## Testing

Unit tests for Services (Auth, Email, Token, RBAC). Integration tests with Testcontainers.MsSql for real DB. All controllers tested.

## Deployment

API containerized (Dockerfile). DB migrations run on startup. Health check: GET /health. JWT secret + ACS key from environment variables (never in config).

## Notes

- No custom roles yet (v1 system roles only)
- No payment gateway (skip payment flow)
- Seed system roles + permissions on first run
- Casbin rules loaded from DB at startup
- Audit logs capture all workspace actions
