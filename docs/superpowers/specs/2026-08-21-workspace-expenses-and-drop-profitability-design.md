# Workspace-Level Expenses, Drop Milestone Profitability

## Problem

Two corrections to the just-shipped milestone fee-ceiling feature: (1) Expense should support workspace-level records (not tied to any job), not just job-scoped ones; (2) the per-milestone profitability calc (Revenue - Expenses) was unwanted scope, remove it.

## Expense workspace-level support

`Expense.JobId` becomes nullable (`Guid?`). `JobId == null` means workspace-level - not tied to any job. `Expense.MilestoneId` must be null when `JobId` is null (a milestone belongs to a job); validated at write time.

Tenant isolation is unaffected: `Expense.WorkspaceId` is already a first-class column (unlike Invoice/Quotation, which derive tenant scope transitively through `Job.WorkspaceId`), so making `JobId` optional here doesn't touch the tenant-isolation boundary at all.

Existing job-nested routes/service methods (`/workspace/{id}/job/{jobId}/expense`, `ExpenseService.CreateAsync(workspaceId, callerUserId, jobId, request)` etc.) stay exactly as they are - zero risk to what's already built. New sibling routes on the same `ExpenseController`: `/workspace/{id}/expense` (`GET`, `GET/{id}`, `POST`, `PUT/{id}`, `DELETE/{id}`, `POST/{id}/receipt`, `GET/{id}/receipt`) for workspace-level expenses, where `JobId` is simply absent.

`ExpenseService` gains workspace-level counterparts of each method (`CreateWorkspaceLevelAsync`, `GetAllWorkspaceLevelAsync`, etc.) that skip `FindJobAsync` and set `JobId = null` on the entity, reusing the same category/payee validation and the same `EnsureAllowedAsync(callerUserId, "expense", action, workspaceId)` permission check the job-scoped methods already use (job-scoped `EnsureJobAccessAsync` was never used for expenses - permission is already workspace-wide RBAC, so no permission-model change is needed for the workspace-level path).

`GetAllAsync`/`GetAllWorkspaceLevelAsync` are two separate queries, not one method with an optional filter - job-scoped listing filters `WHERE JobId = @jobId`, workspace-level listing filters `WHERE JobId IS NULL AND WorkspaceId = @workspaceId`. Keeping them separate avoids a single method silently mixing scopes depending on a caller-supplied flag.

## Drop milestone profitability

Remove entirely:
- `IMilestoneService.ComputeProfitabilityAsync` (interface method and implementation) in `MilestoneService.cs`.
- `GET .../milestone/{id}/profitability` endpoint in `MilestoneController.cs`.
- `MilestoneProfitabilityResponse` DTO.
- `MilestoneProfitabilityTests.cs`.

`GetCommittedAmountAsync`/`EnsureWithinFeeCeilingAsync` (the fee-ceiling enforcement) are unrelated to profit and stay exactly as they are.

## Migration

One migration, `MakeExpenseJobIdNullable`: alters `Expenses.JobId` to nullable. The existing FK (`Expenses.JobId -> Jobs.Id`, `DeleteBehavior.Restrict`) stays, EF just needs `IsRequired(false)` added to the entity configuration for the nullable FK to generate correctly.

## Testing

- Create a workspace-level expense (no `JobId`), list it via the new workspace-level `GetAll`, confirm it does NOT appear in any job's job-scoped list.
- Create a job-scoped expense, confirm it does NOT appear in the workspace-level list.
- Workspace-level expense request with `MilestoneId` set is rejected (milestone requires a job).
- Existing job-scoped expense tests (`ExpenseServiceTests` or equivalent) remain green, unmodified behavior.
- Confirm `MilestoneProfitabilityTests.cs` is gone and no other test references `ComputeProfitabilityAsync`/`MilestoneProfitabilityResponse`.

## Out of scope

- Any other entity going workspace-level (Invoice/Quotation stay job-scoped per the earlier decision).
- StaffCost payee visibility rules - unchanged, apply identically to workspace-level expenses.
