# SurveyorLedger: Multi-Tenant SaaS API

## Project

Survey job management platform. Users register → verify email (OTP/password) → create workspace → manage jobs within workspace. API + database only (UI out of scope).

## Tech Stack

- **.NET 9** (monolithic API)
- **SQL Server 2022** (shared multi-tenant DB)
- **EF Core 9** (ORM, migrations)
- **Casbin.NET** (RBAC enforcement)
- **Azure Communication Services** (email OTP)
- **JWT + httpOnly cookies** (hybrid auth)
- **xUnit + Testcontainers** (testing)

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
src/
  SurveyorLedger.API/         Controllers, Services, Models, Middleware
  SurveyorLedger.Data/        DbContext, Entities, Migrations, Configurations
  SurveyorLedger.Core/        Constants, Enums, Custom Exceptions
tests/
  SurveyorLedger.API.Tests/
  SurveyorLedger.Data.Tests/
```

## Database

**Schema:** Users, Workspaces, Subscriptions, Roles, Permissions, RolePermissions, UserAccess (polymorphic scope), AuthTokens, EmailVerifications, AuditLogs.

**Migrations:** EF Code-First, migrations tracked in `Migrations/` folder.

**Query filters:** User.IsActive, Workspace.IsActive, UserAccess.IsActive soft-delete.

## Development

```bash
# Start SQL Server
docker-compose up -d

# Run migrations
dotnet ef database update -p src/SurveyorLedger.Data -s src/SurveyorLedger.API

# Start API (localhost:5001)
dotnet run -p src/SurveyorLedger.API

# Run tests
dotnet test
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
