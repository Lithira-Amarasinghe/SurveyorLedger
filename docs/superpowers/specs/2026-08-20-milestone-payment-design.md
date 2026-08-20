# Milestone Payment Linking — Design Spec

Date: 2026-08-20 (revised)
Status: Approved (pending written-spec review)

## Purpose

Milestones (`Milestone.cs`, shipped 2026-08-11) are pure progress tracking today - no
money involved. Billing exists independently at the Job level (`Invoice`, `Quotation`,
`Payment`, all keyed off `JobId`). This spec connects the two - a milestone can carry a
fee, that fee becomes a real line item on a quotation or invoice, milestone progress
can optionally be gated on payment state - and, because the existing quotation→invoice
flow was too narrow to support that ("one quotation becomes exactly one invoice, then
it's locked"), restructures billing document creation into a real advance/milestone/
final billing workflow along the way.

Grounded in two constraints read directly from the existing code, not assumed:
- `InvoiceService.UpdateAsync` replaces the entire `LineItems` collection on every save
  and locks the whole invoice - line items included - the moment any `Payment` exists.
  Line items have no durable identity across edits.
- `Payment` attaches only to `Invoice`, never to `Quotation` or to an individual line
  item - only to the invoice as a whole. No proportional per-line-item payment
  allocation exists anywhere in this codebase, and this spec doesn't introduce one.

## Part 1 — Milestone fee and payment gating

### `Milestone.Amount` (new, nullable `decimal`)
The authored fee. Optional - most milestones may have none. One-directional: seeds a
line item when billed, but editing that line item afterward never writes back to
`Milestone.Amount`. It always reads exactly what it was last set to.

### `InvoiceLineItem.MilestoneId` / `QuotationLineItem.MilestoneId` (new, nullable FK)
Tags a line item as originating from a milestone. Tracked independently per entity - a
milestone can have an active quotation-stage tag and an active invoice-stage tag
simultaneously (different lifecycle stages). **At most one active link per milestone
per document type** - a milestone cannot be tagged on two different active invoices'
line items at once (independently, same rule for quotations). Enforced in
`ValidateLineItems` on both services: reject if a submitted `MilestoneId` is already
tagged on a different active document. Without this, "is this milestone paid" has no
well-defined answer, since payments aren't allocated below the invoice level.

### `MilestonePaymentRequirement` (new table) - fully user-defined, not fixed gates
```
Id
MilestoneId    FK -> Milestone
TargetStatus   string  (the Milestone.Status value this rule blocks entry into)
RequiredState  string  ("Invoiced" | "PartiallyPaid" | "FullyPaid")
CreatedAt
```
No fixed "before start / after completion" pair, and no assumption that having a fee
implies being gated. This is an open rule list the user builds themselves, per
milestone: any of the milestone's statuses, any required payment state, as many rules
as they want, or none at all. Zero rules (the default, and expected to stay the
default for most milestones - including every milestone with no fee at all) means the
milestone transitions completely freely regardless of billing state. A milestone with
a linked invoice but no rules is exactly as unblocked as one with neither.

`RequiredState` maps onto the linked invoice's existing `Status`:
- `Invoiced` — the linked invoice exists and is `Sent` or further along.
- `PartiallyPaid` — invoice status is `PartiallyPaid` or `Paid`.
- `FullyPaid` — invoice status is `Paid`.

`MilestoneService.UpdateStatusAsync`: before applying a transition, load rules where
`TargetStatus == status`. No rules → proceed. Rules present → resolve the milestone's
linked invoice (via its tagged line item); no invoice linked treats every
`RequiredState` as unmet. On failure, `ValidationException` naming exactly what's
missing (*"...requires the linked invoice to be fully paid before..."*). Runs
alongside, not instead of, the existing job-access check.

`GetPaymentRequirementsAsync` / `SetPaymentRequirementsAsync` (replaces the milestone's
full rule set in one call, `job.edit`-gated) and `GetPaymentStatusAsync` (returns
`{ Amount, LinkedInvoiceId?, LinkedInvoiceNumber?, InvoiceStatus?, NextGate? }` for the
UI to render without re-deriving the logic) round out `MilestoneService`.

**UI:** an open add/remove rule list on each milestone (collapsed by default) -
*"Requires [any of this milestone's statuses] → [None / Invoiced / Partially paid /
Fully paid]"*, add as many rows as wanted. Matches the schema 1:1 - no artificial UI
narrowing.

## Part 2 — Quotation → many Invoices

### The problem with today's flow
`Invoice.QuotationId` is already nullable and unrestricted - schema-wise, many invoices
per quotation already works. What blocks it is `QuotationService.ConvertToInvoiceAsync`
being a one-shot special action that clones every line item and flips
`Quotation.Status` to `Accepted` (a terminal-feeling status the UI then hides "Convert"
behind). There's no way today to draw an advance now and the remainder later, and no
way to add a fee to that invoice that wasn't already on the quotation.

### Revised model
- `Quotation.Status` reverts to meaning client approval only (`Draft/Sent/Accepted/
  Rejected/Expired`), independent of billing progress - set manually via the quotation
  form, same as today, just no longer auto-set by a conversion action.
- New computed `QuotationService.ComputeBillingProgress(quotation)` →
  `(InvoicedAmount, RemainingAmount)`, summing `Total` across every active `Invoice`
  with that `QuotationId` (same "compute, don't store" pattern as
  `ComputeInvoiceTotals` - no new stored running total to drift).
- **The dedicated convert-to-invoice endpoint and modal are removed.** Creating an
  invoice from a quotation becomes a normal `POST /invoices`, with a new optional
  `QuotationId` on `InvoiceRequest`. When present, the invoice-creation UI (see Part 3)
  offers the quotation's line items as pickable source rows - the user selects which
  ones to draw into *this* invoice (advance/milestone/final), each retaining its
  `MilestoneId` tag if it had one, and can add ordinary new line items alongside them
  (the "additional fees not on the quotation" case). Nothing forces all-or-nothing
  inclusion anymore.
- `InvoiceService.ValidateLineItems`/`CreateAsync`: if `QuotationId` is set, verify it
  belongs to the same `JobId` and is active (404/400 otherwise) - no other constraint;
  an invoice against a quotation can be any subset of its lines plus extras, and
  multiple invoices can each draw from the same quotation independently.
- Quotation→invoice `MilestoneId` propagation (Part 1) now happens implicitly: since
  the invoice-creation UI seeds its line items directly from the quotation's own line
  items (each still carrying its tag), no separate copy step is needed - it's the same
  general line-item flow, not a special conversion path.

## Part 3 — Full-page billing UI, reused for both document types

### Why modals don't fit anymore
`InvoiceFormModalComponent` and `QuotationFormModalComponent` are near-duplicate modals
(client picker, line-item editor, tax/discount-or-valid-until, status) already reused
by three different call sites (their own list pages, plus Job detail). Drawing invoices
from a quotation, picking milestones per line, and showing quotation billing progress
all need more room and more state than a modal comfortably holds, and duplicating that
across two components would double the surface to maintain.

### One shared routed page
New `BillingDocumentFormPageComponent`
(`pages/billing/document-form/billing-document-form-page.component.ts`), parameterized
by `documentType: 'invoice' | 'quotation'` via route `data`. Replaces both modals and
the separate convert modal entirely.

Routes:
```
app/workspace/:id/billing/invoices/new
app/workspace/:id/billing/invoices/:invoiceId/edit
app/workspace/:id/billing/quotations/new
app/workspace/:id/billing/quotations/:quotationId/edit
```
No job-nested route variant - `jobId` travels as a `?jobId=` query param on `new`
(prefilled when launched from Job detail; the job picker shown today in the modals
still appears when it's absent, e.g. launched from the flat billing list) and is read
from the loaded document on `edit`. `?fromQuotation=<id>` (invoice-new only) triggers
the quotation-line-picker described in Part 2. `?milestoneId=<id>` (either type)
prefills one line item from that milestone the same way the old modal prefill worked.

**Back navigation:** always computed, never hardcoded per entry point - if a `jobId` is
known (query param, or the loaded document's own `JobId` on edit), Back routes to that
job's detail page; otherwise (creating fresh from the flat billing list, no job chosen
yet) Back routes to that list. One `goBack()` method, no route duplication for "came
from job" vs "came from list."

**Job detail integration:** the Billing section's "+ Invoice"/"+ Quotation" buttons and
each Milestone row's "Bill this milestone" action all become plain `routerLink`
navigations to the page above with the right query params, instead of opening a modal
and staying put. This matches the rest of the app's existing job-nested-page pattern
(Land detail already expands inline rather than modal-ing; this goes one step further
since billing documents are substantial enough to want their own URL, print link, and
back button).

**Line-item editor:** `LineItemEditorComponent` (shared by both document types
already) gains an optional per-row milestone `<select>`, sourced from the current job's
milestones, defaulting to "No milestone (other fee)" - the single shared component both
document types use, so both gain the picker from one change. When `fromQuotation` is
active, a second sub-list above it shows the quotation's own line items as checkboxes
("include in this invoice") rather than free-text rows - checking one appends it into
the same `lineItems` array the editor manages, so from that point on it behaves
identically to a manually-typed line (including being freely editable before save).

**Quotation list / detail:** each row shows the computed billing progress
(`InvoicedAmount` / quotation `Total`, e.g. "70,000 / 150,000 invoiced") next to Status.
"Create invoice" replaces "Convert to invoice" and always navigates to the new page
with `?fromQuotation=` set - available whenever the quotation isn't `Rejected`/
`Expired`, not gated to `Draft`/`Sent` only, since drawing a second or third invoice
against an already-`Accepted` quotation is now the normal case, not an edge case.

**Invoice list / detail:** rows whose `QuotationId` is set show a small chip
("from QUO-0003") linking to that quotation.

## Data model summary

| Change | Where |
|---|---|
| `Amount` (nullable decimal) | `Milestone` |
| `MilestoneId` (nullable FK, unique-among-active) | `InvoiceLineItem`, `QuotationLineItem` |
| `MilestonePaymentRequirement` | new table |
| `QuotationId` (nullable Guid) | `InvoiceRequest` (already existed on `Invoice`/`InvoiceResponse`, was previously only ever set internally by the now-removed convert action) |
| `ComputeBillingProgress(quotation)` | `QuotationService`, computed, not stored |

Removed: `ConvertToInvoiceAsync` and its `POST /quotations/{id}/convert-to-invoice`
endpoint, `ConvertQuotationRequest`, `ConvertQuotationModalComponent`,
`InvoiceFormModalComponent`, `QuotationFormModalComponent`. The auto `Quotation.Status
= "Accepted"` side effect that lived in the removed conversion method is not
replicated elsewhere - status changes are manual only, via the quotation form, as they
already were for every other status transition.

## API surface

New:
```
GET  /workspace/{id}/job/{jobId}/milestone/{milestoneId}/payment-requirements
PUT  /workspace/{id}/job/{jobId}/milestone/{milestoneId}/payment-requirements
GET  /workspace/{id}/job/{jobId}/milestone/{milestoneId}/payment-status
```
Changed (additive):
- `MilestoneRequest`/`MilestoneResponse` gain `Amount`.
- `LineItemDto` (shared by invoice/quotation request+response) gains `MilestoneId`.
- `InvoiceRequest` gains `QuotationId` (nullable).
- `QuotationResponse` (or wherever `Quotation` is returned) gains `InvoicedAmount`/
  `RemainingAmount`.

Removed: `POST /workspace/{id}/quotations/{id}/convert-to-invoice`.

## Validation & error handling

| Condition | Response |
|---|---|
| Line item's `MilestoneId` already active on another invoice/quotation | 400, names the conflicting document number |
| Status transition blocked by an unmet payment requirement | 400, names the requirement in plain language |
| `SetPaymentRequirementsAsync` without `job.edit` | 403 |
| `InvoiceRequest.QuotationId` set but belongs to a different job or is inactive | 400/404 |
| Editing a paid invoice's line-item milestone tag | 409, same "locked once paid" message already used for amount changes (comparison tuple in `EnsureOnlyDueDateChanged` extended to include `MilestoneId`) |
| Milestone soft-deleted while it has an active line-item link | Allowed - line item keeps its historical `MilestoneId`, shown as "milestone removed" in the UI, invoice/quotation itself unaffected |

## Testing

Backend, extending the existing LocalDB-integration-test style:
- Billing a milestone links the line item; a second attempt elsewhere is rejected.
- Freeform rule: a rule on a status the milestone never reaches never blocks anything;
  a rule on its next status blocks until satisfied, using each of the three
  `RequiredState` values.
- Zero rules on a milestone with a linked, unpaid invoice transitions freely.
- Two invoices created against the same quotation, drawing different line-item
  subsets; `ComputeBillingProgress` reflects both; a third invoice against the same
  quotation still succeeds (no "already converted" block).
- `InvoiceRequest.QuotationId` rejected when it belongs to a different job.
- Editing a line item's `MilestoneId` after payments exist is rejected.

Frontend: `ng build` clean; manual pass covering bill→gate→pay→unlock, a
quotation drawn into two separate invoices with an extra fee added to the second, and
back-navigation from both the job-launched and list-launched entry points.

## Out of scope (unchanged from prior draft)

- Per-line-item payment allocation.
- Live (websocket) unlock notification - refetch-on-focus is enough.
- Anything beyond the freeform rule list already covers - no separate "quick toggle"
  UI is needed now that the real thing is just as simple to use.
