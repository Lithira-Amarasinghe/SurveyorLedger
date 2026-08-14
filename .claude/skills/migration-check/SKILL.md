---
name: migration-check
description: Use before or after generating an EF Core migration in this repo, or when an entity changes shape. Verifies migration matches entity, tenant/soft-delete filters are intact, and naming follows convention.
---

# Migration Check

Run against new/changed files in `api/src/SurveyorLedger.Data/Migrations/` and the entity that triggered them.

## Correctness
- Migration's `Up()`/`Down()` match the entity change exactly (no drift from a stale `dotnet ef migrations add`)
- `Down()` actually reverses `Up()` — no missing column/table drop
- No hand-edits to a migration file — regenerate via `dotnet ef migrations add` instead

## Multi-tenant conventions
- New entity with a `WorkspaceId` FK has the EF query filter configured in `DbContext.OnModelCreating`
- New entity needing soft-delete has `IsActive` column + matching query filter
- Index added on `WorkspaceId` (or composite with it) if the entity will be queried per-tenant at scale

## Naming
- Migration name is descriptive PascalCase matching the change (e.g. `AddJobStatusToSurveyJob`, not `Migration1`)

## Before applying
- `dotnet ef migrations script` reviewed for destructive ops (column/table drop) against a table likely to hold data
- Confirm target DB is LocalDB dev instance, not shared/staging, before `dotnet ef database update`

Report findings as `file — issue — fix`. Flag destructive-looking migrations explicitly, don't auto-apply.

After applying: verify with `dotnet ef migrations list` + one scoped integration test touching the entity, not full suite.
