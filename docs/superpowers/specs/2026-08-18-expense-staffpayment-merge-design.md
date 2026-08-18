# Merge StaffPayment into Expense (Finance plan, part 2)

## Context

Original Expense categories (Travel/Equipment/Printing/ThirdPartyFees/GovernmentCharges/
Misc) don't match the finance plan's cost categories (Staff/Subcontractor/Equipment/
Material/Transport/Other), and StaffPayment (built last session, separate entity) is
really just the "Staff cost" category with a payee. This spec retires StaffPayment and
folds it into Expense as one category, so Expenses becomes the single cost ledger the
finance plan expects.

## Decisions

- **Expense categories become**: `StaffCost`, `Subcontractor`, `Equipment`, `Material`,
  `Transport`, `Other`. Old set (Travel/Printing/ThirdPartyFees/GovernmentCharges/Misc)
  dropped - dev-only DB, no backfill needed (established convention).
- **Expense gains two nullable columns**: `PayeeId` (Guid?, FK to `Person`) and
  `PayeeType` (string?, one of `Salary`/`Commission`/`Bonus`/`ProfitShare` - StaffPayment's
  old `Type` values, renamed to avoid clashing with `Category`). Both required together
  when `Category == "StaffCost"`, both must be null otherwise - validated in
  `ExpenseService`, same shape as `EnsureClientHoldsBillingRoleOnJobAsync`'s pairing checks.
- **Visibility**: new `expense.view_all` permission (mirrors the retired
  `staffpayment.view_all`), Admin only. `ExpenseService.GetAllAsync` filters out other
  people's `StaffCost` rows for callers without `expense.view_all` - `PayeeId !=
  callerPersonId` rows with `Category == "StaffCost"` are excluded, every other category
  stays visible to anyone with `expense.view`. Same own-only shape `StaffPaymentService`
  used, just row-scoped only within one category instead of a whole separate resource.
- **`StaffPayment` fully retired**: entity, `StaffPaymentService`/`IStaffPaymentService`,
  `StaffPaymentController`, DTOs, `staffpayment.*` permissions and their role-permission
  grants, all removed. UI: `staff-payment.service.ts`, `staff-payment-form-modal`, and the
  job-detail "Staff payments" card removed; `expense-form-modal` gains a Payee
  person-picker + Payee type select that only render when Category = Staff cost.
- **Job-detail financial summary**: the separate "Staff payments" line is dropped; its
  total folds into the existing "Expenses" total (StaffCost is now just another Expense
  category, no special-casing in the summary computation).

## Migration

One EF Core migration: drop `StaffPayments` table, drop `staffpayment.*` permission +
role-permission seed rows, add `PayeeId`/`PayeeType` columns to `Expenses`, update
`expense.*` category constraint/allowed-values (enforced in C#, not a DB check
constraint - no schema change needed there), add `expense.view_all` permission +
Admin-only grant. Generated via `dotnet ef migrations add`, never hand-edited.

## Out of scope

- Payment schedule / installments on Invoice - the other Income sub-project, not this one.
- Any change to Quotation/Invoice/Payment.
- Historical StaffPayment data migration into Expense rows - dev-only DB, existing rows
  are dropped with the table per the established no-backfill convention.
