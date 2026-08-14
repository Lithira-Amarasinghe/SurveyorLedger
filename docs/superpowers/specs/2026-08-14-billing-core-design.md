# Billing Core: Revenue & Collection (Phase 1)

Backend only (DB + Services + API). No UI in this phase.

## Context

Payment feature request covers ~8 subsystems (revenue/pricing, payment collection,
expenses, staff payments, profitability, dashboard, client finance, automation). Too
large for one spec. This is phase 1: the foundation everything else depends on —
Client, Quotation, Invoice, Payment. Later phases (expenses, staff payroll,
profitability, dashboards) build on top of this and get their own specs.

No `Client` entity exists in the codebase today, and no billing/payment code exists
at all. `Job` has no client reference yet.

## Decisions

- **New lean `Client` entity**, not reuse of `User`. `User` is an auth/login account
  (password, workspace membership, RBAC) — wrong shape for a billing contact that
  never logs in. `Client` holds only contact/billing info.
- **Free-form line items**, not a pricing rules engine. Quote/Invoice = list of
  `{Description, Quantity, UnitPrice}`. Covers standard/custom/service-based/land-size
  pricing without building a calculation engine. Fee templates (future) = a saved set
  of line items to copy from.
- **Payment methods phase 1**: Cash, Bank transfer (+ proof file upload), Cheque.
  Online gateway explicitly deferred (user's own list marks it "future").
- **Single flat tax rate** per Quotation/Invoice (not per-line-item, not multi-tax).
- **No Milestone linkage.** Payments/invoices are independent of the existing
  `Milestone` entity in this phase.
- **Receipts are not a stored entity.** A `ReceiptNumber` is stamped onto `Payment` at
  creation; the receipt document is generated on demand (same pattern as the existing
  land-print PDF generation), not persisted as its own row.

## Entities

All workspace-scoped directly via `WorkspaceId` (like `Land`, not transitively via
`Job` like `Milestone`) — because `Quotation.JobId` and `Invoice.QuotationId` are
nullable, so tenant filtering can't rely on a parent chain always being present.

### Client
- `Id`, `WorkspaceId`
- `Name`, `Phone`, `Email` (nullable), `Address` (reuse existing `Address` owned type)
- `IsActive`, `CreatedAt`, `UpdatedAt`

### Quotation
- `Id`, `WorkspaceId`, `ClientId`, `JobId` (nullable — quote can precede job creation)
- `Number` (per-workspace sequential, `Q-0001`)
- `LineItems` (owned collection: `Description`, `Quantity`, `UnitPrice`)
- `TaxRatePercent`
- `Status`: `Draft`, `Sent`, `Accepted`, `Rejected`, `Expired`
- `ValidUntil` (informational only, no auto-expiry job in phase 1)
- `RevisionNumber` (int, starts at 0; bumped when line items are edited after
  `Status` has reached `Sent`. Covers "revision charges" cheaply — no new entity)
- `CreatedAt`, `UpdatedAt`, `IsActive`

### Invoice
- `Id`, `WorkspaceId`, `ClientId`, `JobId`, `QuotationId` (nullable — set if converted
  from a quotation)
- `Number` (per-workspace sequential, `INV-0001`)
- `LineItems` (owned collection, same shape as Quotation)
- `TaxRatePercent`, `DiscountAmount`
- `Status`: `Draft`, `Sent`, `PartiallyPaid`, `Paid`, `Overdue`, `Cancelled`
- `DueDate`
- `CreatedAt`, `UpdatedAt`, `IsActive`
- Computed (not stored): `Total = Σ(line items) - Discount + Tax`,
  `AmountPaid = Σ(Payments)`, `Balance = Total - AmountPaid`.
  `Overdue` is computed at read time (`Status` in `Sent`/`PartiallyPaid` AND
  `DueDate` passed), not a stored transition.

### Payment
- `Id`, `WorkspaceId`, `InvoiceId`
- `Amount`, `Method`: `Cash`, `BankTransfer`, `Cheque`
- `ReceivedAt`, `ReferenceNumber` (nullable — cheque #/txn ref)
- `ProofFilePath` (nullable — reuses existing Document-style file storage)
- `ReceiptNumber` (per-workspace sequential, `RCP-0001`, stamped at creation)
- `RecordedBy` (UserId)
- `CreatedAt`

## Status derivation rules

- Recording a `Payment` recalculates `Invoice.AmountPaid`. Server sets
  `Status = PartiallyPaid` if `0 < AmountPaid < Total`, `Paid` if `AmountPaid >= Total`.
  Client cannot set these two statuses directly.
- `DaysOverdue` (computed, read-only): `today - DueDate` in days, only meaningful
  when `Status` is effectively `Overdue`; otherwise 0. No stored field, no
  scheduled job — feeds future reminder automation without building it now.
- `Quotation` → `Invoice`: explicit `POST /{id}/convert-to-invoice` copies
  Client/LineItems/TaxRate onto a new Invoice, sets `Quotation.Status = Accepted`.
  Converting an already-converted (or non-Sent/Draft) quotation is a 400.

## API surface

New controllers, following existing Controller → Service → Data layering and tenant
middleware:

- `ClientsController`: CRUD + list (paged, like existing Land/Job list endpoints),
  `GET /{id}/balance` (Σ `Invoice.Balance` across client's active invoices — reuses
  the Balance calc already defined, no new storage), `GET /{id}/payments` (all
  payments across the client's invoices, newest first — pure query)
- `QuotationsController`: CRUD + `POST /{id}/convert-to-invoice`
- `InvoicesController`: CRUD, `POST /{id}/payments`, `GET /{id}/payments`,
  payment proof file upload (reuse file-storage approach from `Document`)

## Error handling

- Payment `Amount` > current `Balance` → 400 (no overpayment allowed in phase 1)
- Convert quotation not in `Draft`/`Sent` status → 400
- Delete/cancel an `Invoice` that has `Payment` rows → 409; only `IsActive =false`
  soft-delete allowed once payments exist (same convention as Land/Job)
- Any cross-entity reference (`ClientId`, `JobId`, `QuotationId`) not belonging to the
  caller's `WorkspaceId` → 404, not 403 (existing tenant-isolation convention)

## Testing

Service-level tests per new service, mirroring `LandPhotoServiceTests.cs`:
- CRUD + tenant isolation for Client, Quotation, Invoice
- Status derivation (PartiallyPaid/Paid transitions)
- Convert-quotation-to-invoice flow, including the already-converted rejection
- Overpayment rejection
- Numbering sequence correctness (per-workspace, no collisions)
- Client balance/payments aggregation across multiple invoices
- RevisionNumber bump on post-Sent line item edit
- DaysOverdue calculation at/around DueDate boundary

## Out of scope (future phases)

Expenses, staff payments/payroll, profitability calculations, financial dashboard,
fee templates, payment reminders/automation (actually sending them), online payment
gateway, WhatsApp/email notifications, receipt/invoice PDF templates (UI phase).
