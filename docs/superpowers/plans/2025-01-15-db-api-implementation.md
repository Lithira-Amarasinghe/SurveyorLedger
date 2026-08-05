# SurveyorLedger: Database + API Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` per task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Build multi-tenant SaaS API and database (landing → auth → workspace creation → stop).

**Architecture:** .NET 9 monolith, EF Core + SQL Server, Casbin RBAC, Azure ACS email, JWT + cookies.

**Tech Stack:** .NET 9, SQL Server 2022, EF Core 9, Casbin.NET, Azure.Communication.Email, xUnit.

---

## Task Summary (16 tasks total)

1. Project setup (.NET 9, NuGet, Docker SQL Server)
2. Core models & constants (error codes, enums, exceptions)
3. Database entities (User, Workspace, Role, Permission, UserAccess, etc.)
4. DbContext & configurations (fluent mappings, indexes, relationships)
5. Initial migration (create database)
6. DTOs & API models (Auth, User, Workspace requests/responses)
7. PasswordService & AuthService (register, login, OTP verify)
8. EmailService (Azure ACS OTP + verification emails)
9. TokenService (JWT generation, refresh, cookie handling)
10. CasbinService (load rules, enforce permissions)
11. WorkspaceService (list, create workspaces)
12. AuthController (register, login, verify-otp, refresh-token)
13. UserController (get profile, update profile)
14. WorkspaceController (list, create, get by ID)
15. TenantMiddleware & ErrorHandlingMiddleware (tenant isolation, error JSON)
16. Unit & integration tests (all services, controllers)

---

## Task Details

*(See below for each task. Each task includes:)*
- Files to create/modify
- Interfaces (consumes/produces)
- Step-by-step (code blocks provided in agent dispatch, not here)

### Task 1: Project Setup & Dependencies
Files: .sln, .csproj files, docker-compose.yml, appsettings.json
Steps: Create solution, add projects, install NuGet packages, start SQL Server

### Task 2: Core Models & Constants
Files: Constants.cs, Enums.cs, AppException.cs
Steps: Define error codes, claim names, permissions, roles, custom exceptions

### Task 3: Database Entities
Files: User.cs, Workspace.cs, Role.cs, Permission.cs, UserAccess.cs, AuthToken.cs, EmailVerification.cs, AuditLog.cs
Steps: Create entity classes with relationships and navigation properties

### Task 4: DbContext & Configurations
Files: ApplicationDbContext.cs, EntityConfigurations/*.cs
Steps: Configure entities, set indexes, relationships, global filters

### Task 5: Initial Migration
Files: Migrations/InitialCreate.cs
Steps: Create migration, apply to database, verify tables

### Task 6: DTOs & API Models
Files: Responses/ApiResponse.cs, Auth/*.cs, User/*.cs, Workspace/*.cs
Steps: Create request/response classes for all endpoints

### Task 7: PasswordService & AuthService
Files: PasswordService.cs, AuthService.cs
Steps: Implement password hashing (BCrypt), register, login, OTP verification

### Task 8: EmailService
Files: EmailService.cs
Steps: Integrate Azure ACS, send OTP + verification emails

### Task 9: TokenService
Files: TokenService.cs
Steps: Generate JWT (access + refresh), set httpOnly cookies

### Task 10: CasbinService
Files: CasbinService.cs
Steps: Load Casbin rules from DB, enforce (user, role, permission, scope) checks

### Task 11: WorkspaceService
Files: WorkspaceService.cs
Steps: Create workspace, assign admin role, list user workspaces

### Task 12: AuthController
Files: Controllers/AuthController.cs
Steps: POST register, login, verify-otp, refresh-token endpoints with validation

### Task 13: UserController
Files: Controllers/UserController.cs
Steps: GET profile, PUT update profile endpoints

### Task 14: WorkspaceController
Files: Controllers/WorkspaceController.cs
Steps: GET list, POST create, GET by ID endpoints with tenant isolation

### Task 15: Middleware
Files: Middleware/TenantMiddleware.cs, ErrorHandlingMiddleware.cs
Steps: Extract tenant from request, filter queries by scope; catch exceptions, return JSON

### Task 16: Tests
Files: AuthServiceTests.cs, AuthControllerTests.cs, etc.
Steps: Unit tests (services), integration tests (with Testcontainers SQL Server)

---

## Execution

Full code blocks provided when agent executes each task. No code in this plan file.

**Approach?** Say `subagent-driven` (fresh agent per task, review gates) or `inline` (batch this session).
