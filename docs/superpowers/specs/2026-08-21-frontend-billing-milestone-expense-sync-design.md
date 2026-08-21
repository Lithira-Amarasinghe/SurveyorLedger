# Frontend Sync: Quotation-Invoice Line Traceability, Milestone Fee Ceiling, Workspace Expenses

## Problem

Three backend features shipped this session with no frontend: per-line quotation-invoice traceability, the milestone fee ceiling (committed/remaining, multi-invoice gating), and workspace-level expenses. Frontend types/services are stale against the current API shape (`LineItem` missing `id`/`quotationLineId`, `Invoice.quotationId` still present though removed backend-side, `Milestone` missing `committedAmount`/`remainingAmount`, `MilestonePaymentStatus` still single-invoice shaped, `Expense` missing `milestoneId` and no workspace-level methods).

## Scope

Angular UI only. No backend changes (the one real backend gap found while planning - `MilestoneService.UpdateAsync` not validating a fee reduction against committed amount - is tracked as a separate follow-up task, not part of this spec).

## 1. Type/service sync

- `ui/src/app/core/billing.service.ts`:
  - `LineItem` gains `id?: string`, `quotationLineId?: string`.
  - `Quotation.lineItems` type changes from `LineItem[]` to a new `QuotationLineItemView[]` (`{ id, description, quantity, unitPrice, milestoneId?, invoicedAmount, remainingAmount }`) - read shape only, matches `QuotationLineItemResponse`. `QuotationRequest.lineItems` stays `LineItem[]` (write shape, unaffected).
  - `Invoice`/`InvoiceRequest`: remove `quotationId`.
- `ui/src/app/core/milestone.service.ts`:
  - `Milestone` gains `committedAmount: number`, `remainingAmount: number | null`.
  - `MilestonePaymentStatus` replaces `linkedInvoiceId`/`linkedInvoiceNumber`/`invoiceStatus` with `linkedInvoices: LinkedInvoiceSummary[]` (`{ invoiceId, number, status }`), adds `committedAmount: number`, `remainingAmount: number | null`.
- `ui/src/app/core/expense.service.ts`:
  - `Expense.jobId` becomes `string | null`, gains `milestoneId: string | null`.
  - `ExpenseRequest` gains `milestoneId?: string`.
  - New methods on `ExpenseService`: `getAllWorkspaceLevel(workspaceId)`, `getWorkspaceLevelById`, `createWorkspaceLevel`, `updateWorkspaceLevel`, `deleteWorkspaceLevel`, `uploadWorkspaceLevelReceipt`, `workspaceLevelReceiptUrl` - each hitting `/workspace/{id}/expense[...]`, mirroring the existing job-scoped method shapes exactly minus the `jobId` parameter.

## 2. Invoice line editor - per-line quotation-source picker

`LineItemEditorComponent` (`ui/src/app/shared/line-item-editor/line-item-editor.component.ts`):

- New `@Input() quotationLines: (QuotationLineItemView & { quotationNumber: string })[] = []` - this job's active quotation lines with remaining balance, fetched by the invoice form page from all of the job's non-Rejected/Expired quotations.
- Each row gains a "Source" `<select>` (only rendered when `quotationLines.length > 0`, same conditional pattern the milestone dropdown already uses): `No quotation (direct)` or `{quotationNumber}: {description} — {remainingAmount} remaining`.
- Picking a quotation line: auto-fills `description` from the source line, sets `quotationLineId`, auto-sets `milestoneId` from the source line's milestone (read-only display, not editable - matches the backend auto-copy and its conflict rejection) via a locked/disabled milestone `<select>` showing the inherited value.
- Picking "No quotation (direct)": clears `quotationLineId`, milestone `<select>` becomes freely editable again (direct-billing path).
- Unit price input for a sourced line gets a client-side `max` hint (the line's `remainingAmount`) and inline text under the field ("max 40,000 remaining") - a soft pre-check, not a hard block, since the server is final authority and remaining amounts can shift between page load and submit (another invoice created concurrently).

## 3. Milestone row - compact inline ceiling bar + quick actions

Job detail (`ui/src/app/pages/job/job-detail.component.ts`), milestone row:

- Money chip becomes: amount label + a slim 4px progress bar (`committedAmount / amount`), using existing `neutral`/`primary` color tokens (`bg-neutral-200` track, `bg-primary-500` fill) - no new palette. Renders only when `milestone.amount` is set; otherwise shows plain "No fee set" text, unchanged from a fee-less milestone's current look.
- Clicking the bar expands the row (same expand mechanism the payment-rules `banknote` toggle already uses) to show:
  - `Remaining: {remainingAmount}` (or "Fully committed" badge when `remainingAmount === 0`).
  - Linked invoices list: each a small row with invoice number (link to the invoice), a status chip, reusing `InvoiceListComponent`'s existing status-chip styling.
- Quick actions (new, addressing "what's convenient beyond what's specified"): when a milestone has `remainingAmount > 0` (or no fee set), two small text links appear next to the bar:
  - **"Quote this"** → navigates to `billing/quotations/new` with `jobId` + `milestoneId` + `prefillAmount=remainingAmount` query params, so the new-quotation form opens with one line pre-filled (title = milestone title, milestone tag set, amount = remaining if a fee exists).
  - **"Bill directly"** → same pattern into `billing/invoices/new`, pre-filling a direct (no `quotationLineId`) line.
  - When `remainingAmount === 0` and a fee is set, both links are replaced by a static "Fully committed" label - prevents the user attempting an action the ceiling will just reject, and explains why the buttons disappeared.
- Milestone edit form (create/edit inline form already in the row): when editing an existing milestone's `Amount` field, show `Committed: {committedAmount}` as helper text under the input. If the user types a value below `committedAmount`, show an inline warning ("Already committed {committedAmount} - the backend does not yet block this, but reducing below it will make the ceiling inconsistent with what's already billed.") without disabling the Save button - a heads-up, not a block, since the backend gap is tracked separately (see Scope).

## 4. Workspace-level expenses - new Billing tab

- `BillingTabsComponent` (`ui/src/app/pages/billing/billing-tabs.component.ts`) gains a fourth tab: `Expenses`, linking to `/app/workspace/:id/billing/expenses`, same `routerLinkActive` styling as the other three.
- New route + component `WorkspaceExpenseListComponent` (`ui/src/app/pages/billing/expenses/workspace-expense-list.component.ts`), mirroring `QuotationListComponent`'s structure: card/table, columns `Date | Category | Payee | Amount | Actions` (no Job column - workspace-level by definition), "+ Expense" button opening a create modal, Edit/Delete actions per row matching the job-scoped table's existing action-link style.
- Convenience additions on this list (both here and on the job-scoped expense table in job-detail, for consistency):
  - **Category filter** `<select>` above the table (client-side filter over the already-fetched list - no new endpoint), default "All categories".
  - **Running total footer row** in the table showing the sum of the currently-filtered rows - useful for a quick financial glance without leaving the page.
- Reuses `ExpenseFormModalComponent` for create/edit: `@Input() jobId` becomes optional (`jobId?: string`); when absent, the component calls the new workspace-level `ExpenseService` methods instead of the job-scoped ones, and the (new, see below) milestone dropdown is simply not rendered (milestones require a job).
- Job-scoped expense tab inside Job detail stays on its existing job-scoped routes/methods, untouched structurally - only gains the milestone column/filter below.

## 5. Job-scoped expense form + table gain milestone tagging

- `ExpenseFormModalComponent` gains `@Input() milestones: Milestone[] = []` (job-detail already holds this list for the milestone section, passed straight through) and a milestone `<select>` row, same optional-dropdown pattern as `LineItemEditorComponent`'s ("No milestone" default, only rendered when `milestones.length > 0` and `jobId` is present).
- Job-detail's expense table gains a `Milestone` column: shows the tagged milestone's title as a small chip, or "—". Clicking the chip filters the table to that milestone (client-side, toggle - click again to clear) - convenience for reviewing one milestone's costs without leaving the job page.

## Testing

Angular UI in this codebase has no established unit-test convention visible in the explored files (component specs weren't found alongside the read components) - manual verification via the dev server preview is the testing approach here, consistent with how prior UI-only changes in this session were verified (`ng build` + browser check). Specifically verify:

- Invoice line editor: picking a quotation-line source auto-fills and locks milestone; switching back to "No quotation" unlocks it.
- Milestone row: progress bar renders correctly at 0%, partial, and 100% committed; quick actions disappear and "Fully committed" shows at 100%.
- Quote-this/Bill-directly links correctly prefill the new quotation/invoice form via query params.
- Workspace expense list: create, edit, delete, category filter, running total all functional; job-scoped expense list unaffected.
- Milestone dropdown in expense form only shows for job-scoped expenses, never workspace-level ones.
- `ng build` clean, no TypeScript errors.

## Out of scope

- The `MilestoneService.UpdateAsync` fee-reduction validation gap (separate backend follow-up).
- Any change to the print views (`invoice-print`, `quotation-print`, `receipt-print`).
- Mobile-specific layout work beyond what the existing Tailwind classes already provide responsively.
