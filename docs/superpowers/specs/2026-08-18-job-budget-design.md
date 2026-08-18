# Job budget (Finance plan, part 1)

## Context

First piece of a larger finance plan (Budget / Income / Expenses / Profit-Loss / Reports).
Income (Quotation/Invoice/Payment) and Expenses (Expense/StaffPayment) already exist. This
spec covers Budget: an estimated fee and estimated cost per job, admin-only, feeding the
Profit/Loss view built later.

## Decisions

- **New entity `JobBudget`**, 1:1 with `Job` via `JobId` (PK + FK), not columns on `Job` -
  keeps `Job` free of finance-specific fields per user direction. No row exists until an
  Admin sets one (`GET` returns null, not a zeroed record).
  - `JobId` (PK, FK to Job)
  - `EstimatedFee` decimal(18,2)
  - `EstimatedCost` decimal(18,2)
  - `UpdatedAt`, `UpdatedBy` (Person)
- **Expected profit is computed** (`EstimatedFee - EstimatedCost`), never stored - same
  convention as `InvoiceService.ComputeInvoiceTotals`.
- **Permissions: full CRUD set**, not just view/edit - `budget.view`, `budget.create`,
  `budget.edit`, `budget.delete`. Matches the existing `expense.*`/`staffpayment.*` shape so
  the permission model stays uniform and future roles (e.g. Finance) can be granted a subset
  later without a schema change. All four granted to Admin only for now; no other role gets
  any of them.
- **Workspace-level check, not job-scoped** - `EnsureAllowedAsync(callerId, "budget", action,
  workspaceId)`, same pattern as `ManageMembers`. Budget is Admin-only regardless of which
  job, so job-level scoping (`EnsureJobAccessAsync`) would add complexity with no behavioral
  difference today.
- **Kept out of `JobResponse`** - a separate `GET/PUT/DELETE
  /workspace/{workspaceId}/job/{jobId}/budget` endpoint, so Surveyor/Client never receive
  estimated fee/cost even as an unused field. `JobResponse` gains `canViewBudget` /
  `canEditBudget` booleans (same convention as existing `canManageParticipants`) so the UI
  knows whether to render the card without a failing probe request.
- **UI**: new "Budget" card on job-detail, rendered only when `canViewBudget`. Shows
  Estimated fee / Estimated cost (editable inline, same save/discard pattern as the job
  header, only if `canEditBudget`) and a computed Expected profit line.

## Migration

One EF Core migration adding `JobBudgets` table + `budget.view/create/edit/delete`
permission seed rows (`HasData`) + Admin role-permission grants (`HasData`). Generated via
`dotnet ef migrations add`, never hand-edited.

## Out of scope

- Per-category budget breakdown (staff/subcontractor/equipment/etc.) - flagged as a possible
  future extension, not built now.
- Budget history/audit trail beyond `UpdatedAt`/`UpdatedBy`.
- Variance reporting (estimated vs actual) - part of the later Profit/Loss and Reports
  sub-projects, not this one.
