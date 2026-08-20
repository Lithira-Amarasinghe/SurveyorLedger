# Milestone Fee Ceiling, Expenses, and Profitability

## Problem

The milestone-payment feature shipped earlier assumed a milestone is billed exactly once, through exactly one route (one `InvoiceLineItem.MilestoneId` tag, enforced as a hard uniqueness rule; one linked invoice for gating). That assumption breaks under the real requirement: a milestone's fee is a *ceiling*, billable partially and progressively, through either a quotation or a direct invoice (never double-counted between the two), with its own expenses tracked separately for profitability.

## Scope

Job-scoped, building directly on the quotation-invoice line traceability feature just shipped. No workspace-level billing. No per-line payment allocation (Payments stay invoice-level — see Revenue definition below).

## Committed-amount model (the fee ceiling)

`Milestone.Amount` (already exists) is the planned/maximum fee, nullable — a milestone can have no fee at all, in which case there's no ceiling to enforce.

New `MilestoneService.GetCommittedAmountAsync(Guid jobId, Guid milestoneId, Guid? excludingQuotationId = null, Guid? excludingInvoiceId = null) -> decimal` sums:
- Every quotation line tagged with this `MilestoneId`, across quotations that are `IsActive` and whose `Status` is not `Rejected` or `Expired` (Draft/Sent/Accepted all count toward the ceiling — this is what makes overlapping drafts safe: two drafts can each partially quote the milestone as long as their sum stays under the fee).
- Plus every invoice line tagged with this `MilestoneId` **where `QuotationLineId` is null** — a line billed through a quotation is already counted via that quotation line; counting the resulting invoice line too would double-charge the ceiling.

Both `QuotationService.ValidateLineItemsAsync` and `InvoiceService.ValidateLineItemsAsync` replace their current milestone-uniqueness checks (which allowed only one line ever) with this ceiling check: `thisLineAmount + GetCommittedAmountAsync(excluding the document being saved) > Milestone.Amount` → `ValidationException`. Skipped entirely when `Milestone.Amount` is null.

This is the single mechanism that satisfies "don't allow the same fee to be accidentally billed through both quotation and direct invoice" — both routes draw from the same ceiling, so double-billing is caught the moment the sum would exceed the planned fee, regardless of which route it comes through.

## Auto-linking MilestoneId through a quotation-drawn invoice line

Reverses the "no auto-copy" rule from the prior spec: when `InvoiceService` builds/updates an invoice line whose `QuotationLineId` is set, and that quotation line carries a `MilestoneId`, the invoice line's `MilestoneId` is auto-copied from it. If the request explicitly sets a *different* `MilestoneId` on a line that also has `QuotationLineId`, that's a `ValidationException` (contradictory tagging). This keeps milestone rollups (committed amount, profitability, payment gating) a single-field query instead of requiring a join through the quotation on every read.

## Expense → Milestone link and profitability

`Expense` gains `public Guid? MilestoneId { get; set; }` (nullable scalar FK, no nav — same pattern as `MilestoneId` everywhere else in this codebase). Expenses stay optional and milestone-agnostic by default; tagging one to a milestone is opt-in at record time.

New `MilestoneService.ComputeProfitability(Guid jobId, Guid milestoneId) -> (decimal Revenue, decimal Expenses, decimal Profit)`:
- `Revenue` = sum of invoiced amounts against the milestone — every invoice line tagged with this `MilestoneId` (both quotation-drawn and direct), regardless of the invoice's payment status. A quotation line is a proposal, not revenue, so it's excluded here even though it counts toward the ceiling above.
- `Expenses` = sum of `Expense.Amount` where `Expense.MilestoneId` matches.
- `Profit = Revenue - Expenses`.

Revenue is invoiced-amount, not paid-amount: `Payment` is recorded per-invoice as a whole document, not per-line, so there's no reliable way to know how much of one milestone's line within a multi-line invoice has actually been collected. Building that allocation is out of scope here.

## Payment-gating adaptation (breaking change to the existing feature)

`MilestoneService.FindLinkedInvoiceAsync` (singular, assumes exactly one invoice per milestone) becomes `FindLinkedInvoicesAsync` (plural, returns every active invoice carrying a line tagged with this `MilestoneId`). Gate satisfaction (`IsRequirementSatisfied`) becomes aggregate across that list:
- `"Invoiced"` — at least one linked invoice is Sent, PartiallyPaid, or Paid.
- `"PartiallyPaid"` — sum of `AmountPaid` across all linked invoices is greater than 0.
- `"FullyPaid"` — the milestone is fully committed (`GetCommittedAmountAsync == Milestone.Amount`, only meaningful when `Amount` is set) **and** every linked invoice is Paid.

`MilestonePaymentStatusResponse` changes from a single `LinkedInvoiceId`/`LinkedInvoiceNumber`/`InvoiceStatus` to a list of linked invoices, and gains `CommittedAmount`/`RemainingAmount` (from `GetCommittedAmountAsync`).

## Quotation revision / supersession

No new mechanism needed. The existing `RevisionNumber` bump (on editing a Sent+ quotation) plus the ceiling excluding `Rejected`/`Expired` quotations already gives the right behavior: a superseded quotation must be explicitly moved to `Rejected` by the user (existing `Status` field, no new state machine) to free up its committed amount for a replacement. This is a manual step, not automatic — matches "avoid ambiguous overlapping charges" without inventing an auto-supersede side effect.

## Migration

One migration, `AddMilestoneIdToExpense`: nullable `Guid` column `MilestoneId` on `Expenses`, plus an index on `Expenses.MilestoneId` (the profitability query filters by it) and on `InvoiceLineItems.MilestoneId`/`QuotationLineItems.MilestoneId` if not already indexed (the new ceiling query becomes a hot path on every quotation/invoice save — check current indexes before assuming these need adding).

## API surface changes

- `MilestoneResponse` (or wherever the milestone DTO lives) gains `CommittedAmount`/`RemainingAmount` via `GetCommittedAmountAsync`.
- `ExpenseRequest`/`ExpenseResponse` gain `MilestoneId`.
- `MilestonePaymentStatusResponse` restructures to a list of linked invoices (see above) plus `CommittedAmount`/`RemainingAmount`.
- New `GET /jobs/{jobId}/milestones/{milestoneId}/profitability` returning `{ Revenue, Expenses, Profit }`.

## Testing

- Ceiling: a quotation line plus a direct invoice line together summing under the fee succeed; together summing over the fee, the second one rejected, regardless of which order (quotation-then-invoice or invoice-then-quotation).
- Ceiling: a quotation-drawn invoice line does not double-count against the ceiling (its quotation line already counted).
- Ceiling: milestone with no `Amount` allows unlimited quotation/invoice lines tagged to it.
- Auto-copy: creating an invoice line with `QuotationLineId` pointing at a milestone-tagged quotation line results in the invoice line's `MilestoneId` being set automatically; explicitly setting a conflicting `MilestoneId` on such a line is rejected.
- Two Draft quotations can each partially quote the same milestone as long as their sum stays under the fee; a third that would push the sum over is rejected.
- Expense tagged to a milestone; profitability computed correctly (Revenue from invoice lines, Expenses from tagged expenses, Profit = difference).
- Payment gating: two invoices both linked to the same milestone; `"PartiallyPaid"` satisfied once either has a payment; `"FullyPaid"` requires the milestone fully committed and both invoices Paid.
- Existing quotation-invoice line traceability tests (over-billing at the quotation-line level) remain unaffected — this is a separate, additive ceiling layered on top, not a replacement.

## Out of scope

- Per-line payment allocation (Revenue stays invoiced-amount, not paid-amount).
- Auto-supersede of a previous quotation when a revision is accepted (manual `Status = Rejected` instead).
- Workspace-level billing.
- Frontend changes (planned separately).
