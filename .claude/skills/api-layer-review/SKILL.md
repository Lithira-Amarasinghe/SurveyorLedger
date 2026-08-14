---
name: api-layer-review
description: Use when writing or reviewing .NET API code in this repo — new controller/service/endpoint, auth/tenant changes, or before committing backend work. Checks Controllers→Services→Data layering, tenant isolation, and RBAC correctness.
---

# API Layer Review

Run this checklist against changed files under `api/src/`.

## Layering
- Controller has no business logic — routes + validation + calls service only
- Service owns business logic, not controller or repository
- Entities live in `SurveyorLedger.Data`; DTOs/requests/responses in `SurveyorLedger.API`
- No circular deps between layers

## Tenant isolation
- Any query touching tenant-scoped data goes through `TenantMiddleware`-derived `WorkspaceId` — no raw unscoped query
- New entity with tenant data has EF query filter excluding other workspaces
- Soft-delete entities use `IsActive` flag + query filter, not hard delete

## Auth / RBAC
- Endpoint has correct `[Authorize]` / Casbin policy check for the roles it should allow (Admin, Manager, Surveyor, Client)
- New role/permission combination registered in Casbin policy source, not hardcoded in controller

## Errors
- Custom exceptions inherit `AppException`
- No raw `throw new Exception(...)` — use typed exception so middleware formats it

## Config
- No hardcoded connection strings, secrets, or URLs — pulled from environment/config

Report findings as `path:line — issue — fix`, most severe first. Skip files with no violations, don't praise.

After fixes: run scoped tests only (see rules.md "Per-change verification"), not full suite.
