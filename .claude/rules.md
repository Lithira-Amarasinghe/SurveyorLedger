# Implementation Rules & Coding Standards

## Clean Code Principles

- No over-engineering: simplest solution that works first
- Reuse existing patterns/utilities before writing new ones
- Deletion > addition; boring > clever
- No abstractions without concrete usage (no interface with one impl, no factory for one product)
- Shortest working diff wins, but only after understanding the whole problem
- Mark deliberate simplifications with `// rule:` comment naming the ceiling (e.g., `// rule: sync only, upgrade if throughput matters`)

## Project Structure

**Monorepo layout:**
- `/api` — .NET 9 backend (Controllers → Services → Data layer)
- `/ui` — Angular 21 frontend (Standalone components, signals)
- Root only for config (CLAUDE.md, git, CI/CD)

**Code isolation:** API and UI are independent deployables. No circular dependencies. API runs on :5296, UI on :4200.

## Backend (.NET)

**Architecture:**
- Clean layers: Controllers → Services → Data layer (EF Core)
- Services own business logic; controllers route only
- Entities in SurveyorLedger.Data; DTOs/requests/responses in SurveyorLedger.API
- Custom exceptions inherit AppException; error middleware catches and formats

**Auth/RBAC:**
- JWT in header, httpOnly cookie for refresh (hybrid pattern)
- TenantMiddleware extracts WorkspaceId, scopes queries
- Casbin.NET 2.0 for role-based access (loaded from DB at startup)
- System roles: Admin, Manager, Surveyor, Client

**Database:**
- EF Code-First migrations tracked in Migrations/
- Soft-delete: IsActive flags, query filters exclude inactive records
- Multi-tenant: shared DB, query filters isolate tenants
- ConnectionStrings from environment (never hardcoded)

**Testing:**
- Unit: Services, token generation, crypto
- Integration: Testcontainers.MsSql (real DB, not mocks)
- All controllers + services tested

## Frontend (Angular)

**Setup:**
- Standalone components (no NgModule)
- Signals for state (InputSignal, computed)
- RxJS for HTTP + state streams
- Tailwind CSS (utility-first, high density) + Angular Material

**Architecture:**
- Pages: Auth (public), Workspace, Profile (/app, guarded)
- Shell: App-shell (sidebar, topbar) wraps /app/*
- Services: AuthService, WorkspaceService (DI + HTTP)
- Guards: authGuard redirects 401 to /auth/login with returnUrl

**Styling:**
- Tailwind config: custom palette (#9E0031 primary, #44001A–#8E0045 variants), high-density spacing
- Material for tables, dialogs, dropdowns
- No custom CSS unless Material doesn't cover it
- Dark mode ready (not required v1)

**HTTP & Auth:**
- JwtInterceptor attaches Bearer token to all requests
- AuthService stores JWT in localStorage (sessionStorage optional v2)
- 401 response → logout + redirect

## Git & PR Workflow

**Commits:** Conventional (feat:, fix:, test:, docs:, refactor:, chore:). Keep messages short.

**Branches:** Feature branches off main, PR back to main.

**Before PR:**
- All tests pass locally
- No console errors/warnings
- Code follows project patterns
- Single logical change per PR

## No Out-of-Scope Features

Skip in Phase 1/2:
- Jobs, Surveys (Phase 3)
- RBAC UI (Phase 3)
- Billing, payment gateway
- Password reset, social auth
- Org management beyond workspace
- Custom roles (v1 system roles only)

## Documentation

- CLAUDE.md ≤ 200 lines (overview + quick start)
- UI_IMPLEMENTATION_GUIDE.md — page specs, components, scope
- .claude/rules.md — this file (coding standards, patterns)
- Code is the source of truth; don't duplicate git history in comments
- Only comment the WHY if non-obvious

## Development Setup

```bash
# API
cd api
dotnet build
dotnet run --project src/SurveyorLedger.API

# UI
cd ui
ng serve

# Both via Claude Code
.claude/launch.json: "SurveyorLedger API" + "SurveyorLedger UI"
```

## Process Discipline

**Plan mode:** required before touching code for — new endpoint/feature, schema/migration change, cross-layer change (API+UI), anything touching auth/tenant isolation. Skip for: single-file fix, typo, config tweak, adding a test. Use `superpowers:brainstorming` first if requirements aren't nailed down, then `EnterPlanMode`.

**Long/multi-session work:** if context is filling on a long debugging or multi-file session, use the `headroom` skill to compress old tool output/conversation before it gets truncated — cheaper than losing early context to auto-compaction.

**Skills over subagents:** for review-shaped checks (layering, tenant isolation, migration correctness) run the `api-layer-review` / `migration-check` skills inline instead of spawning a subagent — same checklist rigor, no dispatch overhead.

## When in Doubt

1. **Read existing code first** — patterns, structure, naming
2. **Reuse before building** — stdlib, existing utils, existing services
3. **Ask why, not what** — WHY is this feature needed? Is it in scope?
4. **Minimal first** — ship the lazy version, upgrade if required
5. **Test the golden path + one edge case** — not exhaustive coverage
