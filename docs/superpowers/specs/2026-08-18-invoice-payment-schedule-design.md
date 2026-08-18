# Invoice payment schedule (Finance plan, part 3)

## Context

Last piece of Income per the finance plan. Invoices already support partial payments
against a single DueDate (status becomes PartiallyPaid automatically). Firms billing in
installments (deposit / milestone / final) need to plan and see per-installment due dates,
not just one date for the whole invoice.

## Decisions

- **New owned collection `InvoiceInstallment` on `Invoice`** - `Id`, `Amount`, `DueDate`.
  Same `OwnsMany` shape as `InvoiceLineItem`, including the explicit
  Remove-old/Add-new-as-`EntityState.Added` pattern `InvoiceService.UpdateAsync` already
  uses for line items (reassigning the collection reference alone doesn't track correctly
  - documented in `InvoiceService.UpdateAsync`'s existing comment).
- **Validation**: sum of installment amounts must equal the invoice's computed total,
  checked only when the schedule is written (create/update `InvoiceRequest.Installments`).
  Not re-validated if line items/tax/discount change afterward - schedule can drift from
  the invoice total after an edit; accepted limitation, not enforced automatically.
- **No new permission** - covered by existing `invoice.create`/`invoice.edit`, since
  setting the schedule is part of managing the invoice.
- **Installment status is computed, never stored**: order by `DueDate`, walk cumulative
  installment amount against the invoice's cumulative `AmountPaid`
  (`ComputeInvoiceTotals`) - `Paid` once that running total covers the installment,
  `Overdue` if its `DueDate` has passed and it isn't yet covered, else `Pending`.
  `ComputeInvoiceTotals`/`Invoice.IsOverdue` itself is unchanged - the schedule is a
  planning/display layer on top, not a new source of truth for the invoice's own status.
- **API**: `InvoiceRequest` gains `Installments: List<InstallmentDto> { Amount, DueDate }`
  (optional - an invoice with no schedule behaves exactly as today).
  `InvoiceResponse` gains `Installments: List<InstallmentResponse> { Amount, DueDate,
  Status }` with `Status` computed server-side as above.
- **UI**: installment editor in `invoice-form-modal` (add/remove rows, live sum-vs-total
  validation before submit, same list-editing shape as `LineItemEditorComponent`), plus a
  small schedule table displayed wherever an invoice is already shown (job page Billing
  card, invoice list detail) - due date / amount / status per row.

## Migration

One EF Core migration adding the `InvoiceInstallments` table (owned entity, FK to
`InvoiceId`, cascade delete with the parent invoice). No permission changes. Generated via
`dotnet ef migrations add`, never hand-edited.

## Out of scope

- Auto-matching payments to specific installments (deliberately free-form, per prior
  decision).
- Changing `Invoice.IsOverdue`/`DueDate` semantics.
- Reminder emails per installment - not requested, would be a follow-up on top of the
  existing `SendAsync` email path.
- Reports section (separate, later sub-project).
