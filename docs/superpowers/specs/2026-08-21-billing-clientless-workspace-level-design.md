# Drop ClientId, Workspace-Level Quotations/Invoices, Linked-Quotations on Milestones

## Problem

Three backend corrections requested against the billing feature set shipped this session: (1) `ClientId` on Quotation/Invoice is an unnecessary manual-selection step — access is already governed by job-scoped permissions, not by matching a stored client; (2) the workspace Billing tab needs to support genuinely job-less quotations/invoices, not just job-scoped ones created from a workspace-level entry point; (3) a milestone's expanded detail panel only shows linked invoices, not linked quotations.

## Scope

Backend only. The frontend rework (line-item source picker, milestone panel UI, click-through navigation) is a separate, sequential spec that depends on this one's shape.

## 1. Drop `ClientId`

Remove `Quotation.ClientId`/`Client` nav and `Invoice.ClientId`/`Client` nav entirely. Remove from `QuotationRequest`/`QuotationResponse`/`InvoiceRequest`/`InvoiceResponse`. Remove `EnsureClientHoldsBillingRoleOnJobAsync` calls from `CreateAsync`/`UpdateAsync` in both services (the method itself is deleted — it validated `ClientId`, nothing else depends on it).

Access is unaffected: `EnsureJobAccessAsync` (job-scoped CRUD) already requires the caller to hold a job-scoped `UserAccess` row, which is exactly how Client/Finance role holders get access today — `ClientId` never gated anything, it only recorded which specific person the document was nominally addressed to. `SendAsync` already resolves eligible recipients by querying Client/Finance role holders on the job directly, independent of `ClientId` — unaffected.

PDF generation (`IPdfService.GenerateQuotationPdf`/`GenerateInvoicePdf`) drops its "Bill To" client-name section, replaced with the job's title/number (job-scoped) or the workspace name (workspace-level, see below).

Migration: drop `ClientId` column + FK from `Quotations` and `Invoices`.

## 2. Workspace-level Quotations/Invoices

`Quotation.JobId`/`Invoice.JobId` become nullable (`Guid?`); their `Job` navs become nullable. Both entities gain a direct `WorkspaceId` column — tenant isolation was transitive through `Job.WorkspaceId`, which no longer works once `JobId` can be null. On create: if `JobId` is provided, `WorkspaceId` is resolved from that job (and validated to belong to the caller's workspace, as today); if `JobId` is null, `WorkspaceId` is the route's `workspaceId` directly.

Access: job-scoped docs keep `EnsureJobAccessAsync`. Workspace-level docs (`JobId == null`) use `EnsureAllowedAsync(callerUserId, "quotation"/"invoice", action, workspaceId)` — the existing `quotation.*`/`invoice.*` permissions (seeded in `SeedBillingPermissions`) already cover this; no new permission migration needed.

Numbering and uniqueness switch from `(JobId, Number)` to `(WorkspaceId, Number)` — the old unique index is dropped, a new one added. `NextNumberAsync`/`NextInvoiceNumberAsync` count by `WorkspaceId` directly instead of `Job.WorkspaceId`.

A workspace-level line (parent document has `JobId == null`) cannot carry `MilestoneId` or `QuotationLineId` — both concepts (milestone fee ceiling, quotation-line sourcing) are inherently job-scoped, since a milestone always belongs to a job. Rejected at line validation with a clear message, checked before any other line validation runs.

`SendAsync`'s recipient-eligibility check (Client/Finance role holders on the job) has no job to check for a workspace-level document — for those, eligibility becomes "any workspace member holding `quotation.view`/`invoice.view` at workspace level", i.e. anyone who could already see it via `EnsureAllowedAsync`.

## 3. Milestone linked quotations

New `MilestoneService.FindLinkedQuotationsAsync(Guid milestoneId) -> Task<List<Quotation>>`, mirroring `FindLinkedInvoicesAsync` — every active quotation carrying a line tagged with this `MilestoneId`. `MilestonePaymentStatus` gains `LinkedQuotations: List<LinkedQuotationSummary>` (`{ QuotationId, Number, Status }`), alongside the existing `LinkedInvoices`. `GetPaymentStatusAsync` populates both lists. `MilestonePaymentStatusResponse` gains the matching DTO field.

## Migration

One migration, `DropClientIdAddWorkspaceLevelBilling`:
- Drop `Quotations.ClientId`/`Invoices.ClientId` columns + FKs.
- Add `Quotations.WorkspaceId`/`Invoices.WorkspaceId` columns (backfilled from `Jobs.WorkspaceId` via the existing `JobId` for all current rows, since every existing row has a job today).
- Alter `Quotations.JobId`/`Invoices.JobId` to nullable.
- Drop the `(JobId, Number)` unique indexes, add `(WorkspaceId, Number)` unique indexes.

## Testing

- Job-scoped quotation/invoice create/update/view continues to work with no `ClientId` in the request/response.
- Workspace-level quotation/invoice: create with no `JobId`, accessible via workspace-level `quotation.view`/`invoice.view` permission, correctly numbered per-workspace.
- Workspace-level line with `MilestoneId` or `QuotationLineId` set is rejected.
- `SendAsync` on a workspace-level document resolves eligible recipients by workspace permission, not job role.
- `GetPaymentStatusAsync` returns both `LinkedQuotations` and `LinkedInvoices` for a milestone billed through both routes.
- Existing quotation-invoice-line-traceability and milestone-fee-ceiling tests (job-scoped) remain green, unaffected by the nullable `JobId`/dropped `ClientId`.

## Out of scope

- Frontend changes (separate spec, next).
- Any change to `Payment` or `Expense` (already workspace-level-capable from the prior feature).
