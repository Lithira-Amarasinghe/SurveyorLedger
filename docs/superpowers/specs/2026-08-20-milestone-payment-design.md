# Milestone Payment Linking — Design Spec

Date: 2026-08-20
Status: Approved (pending written-spec review)

## Purpose

Milestones today (`Milestone.cs`, shipped 2026-08-11) are pure progress tracking - no
money involved. Billing already exists independently at the Job level (`Invoice`,
`Quotation`, `Payment`, all keyed off `JobId`). This spec connects the two: a milestone
can carry a fee, that fee can become a real line item on a quotation or invoice, and a
milestone's progress can be gated on that invoice reaching a required payment state.

Everything here builds on two constraints discovered by reading the existing billing
code, not assumed:
- `InvoiceService.UpdateAsync` replaces the entire `LineItems` collection on every save
  (delete-all-insert-all) and locks the whole invoice - line items included - the
  moment any `Payment` exists (`EnsureOnlyDueDateChanged`). Line items have no durable
  identity across edits by design.
- `Payment` attaches only to `Invoice`, never to `Quotation` (`Quotation` has no
  `Payments` relation at all) and never to an individual line item - only to the
  invoice as a whole.

Both constraints shape the design below: no proportional per-line-item payment
allocation (the codebase has no concept of that anywhere), and no payment gating tied
to quotations (there's nothing to gate on until an invoice exists).

## Data model

### `Milestone.Amount` (new, nullable `decimal`)
The authored fee for this milestone. One-directional: this value seeds a line item
when a milestone is billed, but editing that line item afterward never writes back to
`Milestone.Amount`. It always reads exactly what it was last set to - no hidden sync
with whatever the invoice says.

### `InvoiceLineItem.MilestoneId` / `QuotationLineItem.MilestoneId` (new, nullable FK)
Tags a line item as originating from a specific milestone. Tracked independently on
each entity (a milestone can have an active quotation-stage tag and an active
invoice-stage tag at the same time - different lifecycle stages, not competing
claims).

**Constraint: at most one active link per milestone per document type.** A milestone
cannot be tagged on two different active invoices' line items simultaneously (same
rule for quotations, independently). Rationale: without this, "what does this
milestone's payment state mean" becomes ambiguous the moment it's split across
invoices with different statuses - the codebase has no mechanism to allocate partial
payments across line items, so a 1:many milestone-to-invoice relationship can't be
answered correctly. If a milestone genuinely needs rebilling, the old link must be
cleared (line item edited to drop the tag, or that invoice cancelled) before a new one
can be created. This is enforced, not just documented - see Validation below.

A milestone *can* have one line item on a quotation and, separately, one on an
invoice - conversion (below) is what normally connects them.

### Quotation→Invoice conversion carries the tag
`QuotationService.ConvertToInvoiceAsync` already copies `Description`/`Quantity`/
`UnitPrice` from each quotation line item to a new invoice line item (line 168,
existing code). This spec adds `MilestoneId` to that copy. A milestone tagged at the
quotation stage arrives already tagged on the resulting invoice - no manual re-linking
step for the user. (The at-most-one-active-link rule still applies independently to
each document, so this only works cleanly when the quotation itself follows the rule,
which it does by construction.)

### `MilestonePaymentRequirement` (new table)
```
Id
MilestoneId    FK -> Milestone
TargetStatus   string  (the Milestone.Status value this gate blocks entry into)
RequiredState  string  ("Invoiced" | "PartiallyPaid" | "FullyPaid")
CreatedAt
```
Generalized on purpose, per explicit requirement: rather than fixed boolean flags
("payment before start" / "payment after completion"), this is a small ordered rule
set so a future milestone status (or a stricter/looser rule on an existing one) needs
zero schema change - just a different row. `RequiredState` maps directly onto the
linked invoice's existing `Status` field:
- `Invoiced` — satisfied once the invoice exists and is `Sent` or further along
  (not `Draft`).
- `PartiallyPaid` — satisfied once the invoice status is `PartiallyPaid` or `Paid`.
- `FullyPaid` — satisfied only once the invoice status is `Paid`.

A milestone with no requirement rows for a given `TargetStatus` transitions freely -
this is opt-in per milestone, not a global rule. Multiple rows are allowed (e.g.
`Invoiced` required before `InProgress`, `FullyPaid` required before `Completed`, both
active on the same milestone at once).

**UI surface is deliberately narrower than the schema.** The only two realistic gates
in this workflow are "before starting work" (`TargetStatus = InProgress`) and "before
marking done" (`TargetStatus = Completed`) - the milestone row exposes exactly two
dropdowns (`None | Invoiced | Partially paid | Fully paid` each) that read/write these
two rows. The generality is for the data model and `MilestoneService`, not for asking
users to configure an open-ended rule table they don't need yet.

## Backend logic

### `MilestoneService`
- `UpdateStatusAsync`: before applying the transition, load any
  `MilestonePaymentRequirement` rows where `TargetStatus == status`. For each, resolve
  the milestone's linked invoice (via its tagged `InvoiceLineItem`, if any) and compare
  its `Status` against `RequiredState`. If no invoice is linked at all, treat every
  `RequiredState` as unmet (can't be "Invoiced" with nothing invoiced). On failure,
  throw `ValidationException` naming exactly what's missing, e.g. *"This milestone
  requires the linked invoice to be fully paid before it can be marked Completed."*
  This check runs in addition to, not instead of, the existing job-access check - a
  Surveyor who's allowed to edit the job still can't bypass an unmet payment gate.
- New `GetPaymentRequirementsAsync` / `SetPaymentRequirementsAsync(milestoneId,
  List<{TargetStatus, RequiredState}>)` - the second replaces the milestone's full rule
  set in one call (mirrors how `InvoiceService.UpdateAsync` replaces line items
  wholesale; consistent with the codebase's existing pattern for small owned
  collections). Gated by `job.edit`, same as every other milestone mutation.
- New `GetPaymentStatusAsync(milestoneId)` → `{ Amount, LinkedInvoiceId?, LinkedInvoiceNumber?, InvoiceStatus?, NextGate? }` -
  one read the UI uses to render the money chip and lock icon without re-deriving the
  logic client-side. `NextGate` is the nearest unmet requirement for the milestone's
  *current* status (what would block the next transition), or `null` if nothing blocks it.

### `InvoiceService` / `QuotationService`
- `ValidateLineItems` (both services) gains one more check: for any submitted line item
  carrying a `MilestoneId`, reject if that milestone already has a different *active*
  line item elsewhere (a different invoice/quotation than the one being saved). Active
  means the other document's `IsActive` is true - a cancelled/deleted invoice's old tag
  doesn't block a new one.
- `EnsureOnlyDueDateChanged` (the "locked once paid" check) already compares line items
  by `(Description, Quantity, UnitPrice)` - extend the comparison tuple to include
  `MilestoneId`, so the milestone tag is equally locked once payments exist, consistent
  with everything else about a paid invoice being frozen.
- `ConvertToInvoiceAsync`: one-line change, add `MilestoneId = li.MilestoneId` to the
  existing line-item projection.

## API surface

New:
```
GET  /workspace/{id}/job/{jobId}/milestone/{milestoneId}/payment-requirements
PUT  /workspace/{id}/job/{jobId}/milestone/{milestoneId}/payment-requirements
GET  /workspace/{id}/job/{jobId}/milestone/{milestoneId}/payment-status
```
Changed (additive, non-breaking):
- `MilestoneRequest` / `MilestoneResponse` gain `Amount` (nullable decimal).
- `LineItemDto` (shared by `InvoiceRequest`/`QuotationRequest`/their responses) gains
  `MilestoneId` (nullable Guid).

## UI

### Job detail → Milestones section
- Money chip on each row: shows `Amount` (or, once billed, the linked line item's
  `Quantity * UnitPrice` - reads via `GetPaymentStatusAsync`, always the live invoice
  figure, never a stale copy).
- 🔒/🔓 indicator next to the status control: locked when `NextGate` is non-null for
  that milestone's current status, with a tooltip naming what's missing (matches the
  `ValidationException` message so the UI and the 400 the API would return always agree).
  Flips to 🔓 live once the linked invoice's status changes to satisfy it - no page
  reload needed if the invoice was just paid in another tab this session (re-fetch on
  focus is enough; a live socket is out of scope).
- Unbilled milestone: **"Bill this milestone"** button opens the existing
  `InvoiceFormModalComponent` (already reachable from Job detail's Billing section)
  with `fixedJobId` set and `lineItems` prefilled to
  `[{ description: title, quantity: 1, unitPrice: Amount ?? 0, milestoneId }]`. Same
  modal, same validation, same save path - this is a prefill, not a new form.
- Billed milestone (has an active link): chip becomes a link to the invoice/quotation
  it's on, and "Bill this milestone" disappears (only one active link allowed - covered
  by the same backend rule, but hiding the action here avoids the user hitting the 400
  in the first place).
- Small "Payment rules" affordance (collapsed by default, matches the rest of the
  page's disclosure pattern) exposing the two dropdowns described above, saved via
  `SetPaymentRequirementsAsync`. Hidden entirely for Client role, same as every other
  milestone control.

### `LineItemEditorComponent` (shared by invoice and quotation forms)
Each row gains an optional milestone picker: a `<select>` sourced from the current
job's milestones (only shown when the form has a resolved `jobId` - it already does,
via `fixedJobId` or the job dropdown), defaulting to "No milestone (other fee)". This
is the single shared editor both `InvoiceFormModalComponent` and
`QuotationFormModalComponent` already use, so both forms gain the picker for free from
one change.

### Invoice / Quotation detail (list-row expand, or the print view's line-item table)
Line items render in two groups instead of one flat list: **"Milestone charges"**
(grouped by milestone title, each sub-row a chip linking back to
`/app/workspace/{id}/jobs/{jobId}` with the milestone highlighted) then **"Other
fees"** (everything with no `MilestoneId`). Subtotal/tax/total math is unchanged -
this is a display grouping only, `ComputeInvoiceTotals` doesn't change.

## Validation & error handling

| Condition | Response |
|---|---|
| Line item's `MilestoneId` already active on another invoice/quotation | 400, `ValidationException`, names the conflicting document number |
| Status transition blocked by an unmet payment requirement | 400, `ValidationException`, names the requirement in plain language |
| `SetPaymentRequirementsAsync` called by a role without `job.edit` on this job | 403, same `ForbiddenException` path every other milestone mutation uses |
| Editing a paid invoice's line-item milestone tag | 409 `ConflictException`, same "locked once paid" message already shown for amount changes |
| Milestone deleted (soft-delete) while it has an active line-item link | Allowed - the link becomes orphaned-but-inert (line item keeps its historical `MilestoneId`, just no longer resolvable to a live milestone in the UI, shown as "milestone removed"). Matches how the rest of the system treats soft-deleted parents of still-referenced children (e.g. `JobLand`). |

## Testing

Backend (`MilestoneServiceTests.cs`, extended; new `MilestonePaymentTests.cs` mirroring
the existing `JobAccessScopingTests`/`MilestoneServiceTests` integration-test style
against real LocalDB):
- Billing a milestone links the line item; `GetPaymentStatusAsync` reflects it.
- Second attempt to bill the same milestone on a different invoice is rejected.
- Status transition blocked when a `FullyPaid` requirement is unmet; succeeds once the
  linked invoice reaches `Paid`.
- No requirement rows → transition always succeeds (opt-in behavior preserved).
- Quotation→Invoice conversion carries `MilestoneId` across.
- Editing a line item's `MilestoneId` after the invoice has payments is rejected
  (extends the existing `EnsureOnlyDueDateChanged` test coverage).
- Soft-deleting a linked milestone doesn't corrupt the invoice or block further payment
  on it.

Frontend: `ng build` clean; manual pass covering bill→gate→pay→unlock end to end, plus
the two-group invoice display with a mix of tagged and untagged line items.

## Out of scope (explicitly deferred)

- Per-line-item payment allocation / partial-line-item payment status - the codebase
  has no such concept anywhere and this spec doesn't introduce one.
- Multi-invoice milestones (rebilling without clearing the old link first).
- Live (websocket) unlock notification - polling/refetch-on-focus is enough for v1.
- A general-purpose rule-table editor for `MilestonePaymentRequirement` - UI only
  exposes the two realistic gates (before start, before complete).
