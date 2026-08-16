# Job-scoped billing (Spec 2)

## Context

Spec 1 (Person/UserAccount split, merged) deliberately left `Invoice.ClientId`/
`Quotation.ClientId` pointing at `Person.Id` as a transitional shape - workspace-scoped
`Client` CRUD survived unchanged, and invoice/quotation access was still gated at
workspace scope, not job scope. This spec finishes the redesign: invoices and
quotations belong to a job, billing recipients are job participants (not a separate
`Client` concept), and admins can email a link + PDF to whichever job participants
should see it.

## Decisions

- **`Invoice`/`Quotation.JobId` becomes required** (currently nullable). A document
  with no job to belong to no longer makes sense once recipients are job participants.
- **`WorkspaceId` column dropped from `Invoice`/`Quotation`.** Tenant-scoping goes
  through `Job.WorkspaceId` in every query instead of a denormalized column - `.Where(i
  => i.Job.WorkspaceId == workspaceId)` keeps the same "every tenant-scoped query is
  filtered by workspace" guarantee this repo's rules require, it's just enforced via
  the join rather than a stored column.
- **`ClientId` stays a single required field** - the billed party, shown on the
  document. At creation/update time, the given `ClientId` must resolve to a `Person`
  who holds `Client` or `Finance` `UserAccess` on that specific `JobId` - validated,
  not just any `Person` in the system.
- **New system role `Finance`** - job-scoped only, permissions `invoice.view` /
  `quotation.view`, nothing else (no job edit, no document access, no milestone
  access). Lets an accounts-payable contact see bills without being the `Client`.
- **`Client` role gains `invoice.view` / `quotation.view`** added to its default
  permission set - a `Client` sees their own job's billing without needing `Finance`
  too.
- **Access control**: `InvoiceService`/`QuotationService`'s view/edit checks switch
  from `EnsureAllowedAsync(callerUserId, "invoice", action, workspaceId)` to
  `EnsureJobAccessAsync(callerUserId, workspaceId, jobId, action)` - the same
  workspace-wide-OR-job-scoped-grant pattern already used for `Milestone`/`Document`.
  Admin/Surveyor keep seeing every invoice in the workspace via their existing
  workspace-wide `invoice.view` grant; `Client`/`Finance` see only invoices on jobs
  they hold that role on.
- **`ClientService`/`ClientsController`/`ClientDtos.cs` deleted.** Adding a billing
  recipient means adding a `Person` to the job with role `Client` or `Finance`, via
  the existing `JobService.AddParticipantAsync` + invite-by-email fallback (already
  built this session, no new UI mechanism needed). The `"billingclient"` permission
  and its RBAC seed rows are removed.
- **Send flow**: `POST /invoices/{id}/send` and `POST /quotations/{id}/send`, body
  `{ recipientPersonIds: Guid[] }`. Each id must hold `Client` or `Finance` on the
  document's `JobId` (rejected otherwise). UI pre-selects every current Client/Finance
  participant on the job; admin can add/remove before sending. Each recipient gets one
  email containing (a) a link into the app at the document's job page and (b) a PDF
  attachment of the same document.
- **PDF generation**: no PDF library exists in the repo today. Adds **QuestPDF**
  (MIT-licensed Community edition) as a new dependency - a simple line-item table
  render sourced from `InvoiceService.ComputeInvoiceTotals`/the `Quotation` equivalent,
  not a fully styled template. `IEmailService` gains one new method (link + PDF bytes
  attachment), following the existing method-per-purpose pattern
  (`SendInviteEmailAsync`, etc.).

## Migration

One EF Core migration: `Invoice.JobId`/`Quotation.JobId` nullable → required (existing
dev-only rows with no job get dropped, per the "dev-only DB, clean migrations" rule
already established in Spec 1 - no backfill). `WorkspaceId` column dropped from both
tables. `RoleScope`/`Role`/`RolePermission` seed rows updated for the new `Finance`
role and `Client`'s added permissions. Generated via `dotnet ef migrations add`, never
hand-edited, same as every migration this session.

## Out of scope

- Any change to `Payment` (still workspace+invoice scoped, untouched by this spec).
- Recurring/scheduled invoices, partial-payment reminders, or any billing feature not
  explicitly requested above.
- PDF template styling/branding beyond a functional line-item table - a follow-up
  design pass, not blocking this spec.
