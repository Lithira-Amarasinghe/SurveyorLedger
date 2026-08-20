# Quotation-Invoice Line Traceability

## Problem

Quotation and Invoice are financially disconnected below the whole-document level. `Invoice.QuotationId` links a whole invoice to a whole quotation, but there's no way to say "this invoice line bills 40,000 of this specific 80,000 quotation line" — so partial billing, milestone-style progressive billing, and additional (non-quotation) charges on the same invoice can't be tracked or validated per-line. `QuotationService.UpdateAsync` also regenerates every line's `Id` on every edit (`LineItems.Clear()` + `new Guid()`), which would break any line-level reference the moment it's added.

## Scope

Job-scoped only. Quotation and Invoice both keep a required `JobId`; no workspace-level (job-less) billing in this pass. Tenant isolation continues to route through `Job.WorkspaceId`, unchanged.

## Data model

- `InvoiceLineItem` gains `Guid? QuotationLineId` (scalar FK, no EF navigation — same pattern already used for `MilestoneId`).
- `Invoice.QuotationId` and its `Quotation? Quotation` navigation are removed. Quotation linkage now lives per-line only (`InvoiceLineItem.QuotationLineId`), never at the invoice level. An invoice can freely mix lines from multiple quotations, one quotation, or none.
- `QuotationLineItem`/`InvoiceLineItem` keep `Quantity` + `UnitPrice`; a line's "Amount" is `Quantity * UnitPrice`. No new amount field.
- `Payment` is unchanged (`Invoice 1─* Payment`).
- `MilestoneId` and `QuotationLineId` are independent, orthogonal fields on a line. No auto-copy between them, no forced pairing — a line can carry either, both, or neither.

## Quotation line identity stability

`QuotationService.UpdateAsync` stops doing wholesale `LineItems.Clear()` + regenerate-all. New update logic:

- Request item with an `Id` matching a current line → update in place (`Description`/`Quantity`/`UnitPrice`/`MilestoneId`), `Id` preserved.
- Request item with no `Id`, or an `Id` not found among current lines → new line, `Id = Guid.NewGuid()`.
- Current line missing from the request (i.e. being removed), or whose new `Quantity * UnitPrice` would drop below its already-invoiced amount → reject the whole update with `ValidationException` if that line has any invoiced amount against it (sum of `Quantity * UnitPrice` across all `InvoiceLineItem`s on active invoices with `QuotationLineId` pointing at it, > 0).

This makes a quotation line's `Id` a stable identity once anything has been billed against it.

## Invoice-side validation (InvoiceService)

New check in `ValidateLineItemsAsync`, parallel to the existing milestone-uniqueness check:

- For each invoice line with `QuotationLineId` set:
  - Resolve the target quotation line. It must belong to a quotation whose `JobId` matches the invoice's `JobId` — otherwise `ValidationException`.
  - Sum this line's own amount plus every *other* active invoice's amount already billed against the same `QuotationLineId`. If the sum exceeds the quotation line's `Quantity * UnitPrice`, reject with `ValidationException` naming the quotation line and the overage.

This mirrors the existing "milestone already billed on invoice X" pattern but sums instead of exclusivity-checks, since partial billing across multiple invoices is the whole point.

## Billing progress (read side)

- `QuotationService.ComputeBillingProgress(quotation)` (existing, quotation-level `InvoicedAmount`/`RemainingAmount`) is unchanged.
- New `ComputeLineProgress(quotationLineId) -> (InvoicedAmount, RemainingAmount)` on `QuotationService`, using the same active-invoice-lines-referencing-this-QuotationLineId query as the validation check above.
- `QuotationResponse`'s line DTO gains `InvoicedAmount`/`RemainingAmount` per line, populated via `ComputeLineProgress` in `QuotationsController.ToResponse`, so the UI can show per-line billed progress without an extra round trip.

## API surface changes

- `InvoiceRequest.QuotationId` removed.
- Shared `LineItemDto` (used by both invoice and quotation create/update) gains `QuotationLineId` alongside the existing `MilestoneId`.
- `QuotationsController.ToResponse` line projection gains `InvoicedAmount`/`RemainingAmount`.
- No new controller endpoints — existing create/update/get endpoints on both `InvoicesController` and `QuotationsController` carry the new fields.

## Migration

Single EF migration (`dotnet ef migrations add`, generated not hand-edited): drop `Invoices.QuotationId` column/FK, add `InvoiceLineItems.QuotationLineId` column. No backfill of old `Invoice.QuotationId` values to line level — no reliable 1:1 mapping exists retroactively, and this feature is unshipped, so acceptable data loss on that one column.

## Testing

- Quotation line update preserves `Id` for matched lines, assigns new `Id` for new lines.
- Quotation update rejected when it removes or shrinks a line that already has invoiced amount against it.
- Invoice line linked to a quotation line: two invoices partially billing the same quotation line sum correctly; a third invoice pushing the sum past the quotation line's total is rejected.
- Invoice line's `QuotationLineId` pointing at a quotation line belonging to a different job is rejected.
- Quotation response includes correct per-line `InvoicedAmount`/`RemainingAmount` after 0, 1, and 2 invoices bill against a line.
- Existing milestone-gating and milestone-uniqueness tests unaffected (independent field, no shared code path broken).

## Out of scope

- Workspace-level (job-less) quotations/invoices.
- Auto-copying `MilestoneId` from a linked quotation line onto the invoice line.
- Any change to `Payment` entity or payment recording flow.
- Frontend changes (planned separately).
