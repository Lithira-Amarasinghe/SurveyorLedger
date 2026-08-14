# Expenses & Staff Payments (Phase 2) — Backend

Backend only (DB + Services + API). No UI in this phase — matches how billing
phase 1 was split.

## Context

Billing core (phase 1: Client/Quotation/Invoice/Payment) is done. This is the
other half of "revenue vs cost" — both this phase's entities feed Profitability
and the Financial Dashboard, which are the next phase after this one and cannot
be built without these existing first.

## Decisions

- **Expense categories are a fixed set**: Travel, Equipment, Printing,
  ThirdPartyFees, GovernmentCharges, Miscellaneous — enables category breakdown
  reporting later without data cleanup.
- **No expense approval workflow.** Recorded directly. This app's RBAC is flat
  (Admin/Surveyor/Client per workspace or job scope) with no approval-chain
  concept anywhere else in the codebase; adding one here would be new
  architecture for a single feature.
- **Staff payment amount is manual, not calculated.** A flat `Amount` tagged
  with a `Type` (Salary/Commission/Bonus/ProfitShare). No percentage-of-revenue
  formula — matches the free-form-line-item philosophy already used for
  Quotation/Invoice (no pricing rules engine there either).
- **Receipt attachment is optional**, same pattern as `Payment.ProofFilePath` —
  reuses `IFileStorageService`, no new infrastructure.
- **Both entities require a `JobId`.** Expenses and staff payouts only make
  sense as "cost of doing this job" — unlike Quotation/Invoice, which bill a
  client and only optionally reference a job. This also means both nest under
  the existing job URL structure (`/api/workspace/{id}/jobs/{jobId}/...`),
  matching `Milestone`/`Document`, rather than sitting at the top level like
  `Client`/`Invoice`.

## Entities

### Expense
- `Id`, `WorkspaceId`, `JobId` (required FK to `Job`)
- `Category`: `Travel` | `Equipment` | `Printing` | `ThirdPartyFees` |
  `GovernmentCharges` | `Miscellaneous`
- `Amount`, `Description`
- `IncurredDate`
- `ReceiptFilePath` (nullable — optional upload)
- `RecordedBy` (UserId), `CreatedAt`
- No `IsActive`/soft-delete — an expense is a ledger entry; wrong entries are
  hard-deleted (same reasoning as `LandSurvey`/`LandDeed`: "corrects a
  mis-entered record, not meaningful history to preserve once wrong").

### StaffPayment
- `Id`, `WorkspaceId`, `JobId` (required FK to `Job`), `UserId` (which staff
  member — must be a workspace member, same validation shape as
  `Land.OwnerId`'s "must be an existing active account" check)
- `Type`: `Salary` | `Commission` | `Bonus` | `ProfitShare`
- `Amount`, `PaidDate`, `Notes` (nullable)
- `RecordedBy` (UserId), `CreatedAt`
- Hard delete, same reasoning as Expense.

## API surface

Both nest under the job, not top-level:

- `ExpensesController` at `/api/workspace/{workspaceId}/jobs/{jobId}/expenses`:
  CRUD, `POST /{id}/receipt` (upload), `GET /{id}/receipt` (download blob) —
  reuses `IFileStorageService` exactly like `LandService.UploadPhotoAsync`.
- `StaffPaymentsController` at
  `/api/workspace/{workspaceId}/jobs/{jobId}/staff-payments`: CRUD.

## RBAC

New resources: `expense`, `staffpayment` (both distinct from any existing
resource name — checked against the current permission set, no collision like
the `client`/`billingclient` one from phase 1).

- `expense.view/create/edit/delete`: Admin full; Surveyor view/create/edit (no
  delete) — field staff record their own costs, matches how Surveyor gets
  `land.create`/`land.edit` today; Client gets nothing (financial data).
- `staffpayment.view/create/edit/delete`: Admin only for create/edit/delete —
  payroll is a stricter surface than expenses. Surveyor gets `view` only (and
  only their own payments — enforced in the service layer by filtering
  `UserId == callerUserId` when the caller lacks a `view_all`-equivalent grant,
  same shape as `ScopedAccessService.HasViewAllAsync`). Client gets nothing.

## Error handling

- `JobId` in the URL not belonging to the caller's `WorkspaceId` → 404
  (existing tenant-isolation convention).
- `StaffPayment.UserId` not an existing active account → 400
  (`ValidationException`), same shape as `LandService.ValidateOwnerAsync`.
- Receipt upload: same extension/size validation as `LandService.UploadPhotoAsync`
  (images + PDF allowed for receipts, unlike land photos which are image-only;
  size cap reuses `DocumentService.MaxFileSizeBytes`).

## Testing

Service-level tests per new service, mirroring `InvoiceServiceTests.cs`:
- CRUD + tenant isolation for both entities
- `JobId` required, rejected if it belongs to another workspace
- `StaffPayment.UserId` validation (must be an existing active account)
- RBAC: Surveyor can create/edit Expense but not delete; Surveyor sees only
  their own StaffPayments unless granted `view_all`; Client is forbidden from
  both entirely
- Receipt upload/download round-trip

## Out of scope (future phase)

Profitability calculation (job revenue − expenses − staff payments), Financial
Dashboard, expense approval workflow, percentage-based commission calculation,
Expense Budget vs Actual reporting, UI (separate follow-up spec/plan, same
split as billing phase 1).
