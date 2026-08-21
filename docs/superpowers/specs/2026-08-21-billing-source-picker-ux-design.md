# Billing Line-Sourcing Picker, Milestone Panel, Click-Through Navigation

## Problem

Four frontend corrections against the billing UX shipped this session: (1) quotation/milestone-sourced invoice lines shouldn't show an editable Quantity field, only additional/direct lines should; (2) milestone rows should lose the "Quote this"/"Bill directly" quick actions now that sourcing happens inside the invoice/quotation form; (3) the milestone detail panel should show linked quotations as well as linked invoices; (4) the per-line milestone/quotation dropdowns are bad UX and need replacing with a proper tabbed picker, plus bulk-add actions; (5) list rows (invoices, quotations, milestones) should be fully clickable, not just specific sub-elements; (6) the client picker disappears from the invoice/quotation form entirely.

## Scope

Frontend only, depends on the backend spec (`2026-08-21-billing-clientless-workspace-level-design.md`) already being implemented — `ClientId` gone from `Invoice`/`Quotation`/`InvoiceRequest`/`QuotationRequest`, `MilestonePaymentStatus.linkedQuotations` available.

## 1. Quantity hidden for sourced lines

In `LineItemEditorComponent`, the Quantity `<input>` only renders when a line has neither `milestoneId` nor `quotationLineId` set. A sourced line shows a fixed `×1` label in its place instead (quantity is always sent as `1` for sourced lines — unit price stays editable for partial draws, matching the existing ceiling/remaining-amount validation).

## 2. Remove milestone quick actions

`job-detail.component.ts`'s milestone row drops the "Quote this"/"Bill directly" links entirely (added in the prior session, now superseded). Sourcing a milestone into a document happens from inside the invoice/quotation form via the new picker (section 4), not by deep-linking from the milestone row.

## 3. Milestone panel shows linked quotations

The milestone row's expanded detail panel gains a second list, "Linked quotations", using `MilestonePaymentStatus.linkedQuotations` (added backend-side) — same chip style as the existing linked-invoices list, each linking to that quotation's edit page.

## 4. Billing source picker (replaces the per-line dropdowns)

New `BillingSourcePickerComponent` (modal), opened via a "+ Add from…" button next to line-item-editor's existing "+ Add line item":

- Two tabs: **Milestones** and **Quotations**.
- **Milestones tab**: lists the job's milestones with `remainingAmount > 0` (or no fee set), each row shows title + remaining amount, clicking adds one direct-billed line (`milestoneId` set, no `quotationLineId`) to the parent form and closes the picker.
- **Quotations tab**: two-level - first a list of the job's non-Rejected/Expired quotations, clicking one drills into its lines (remaining amount per line, zero-remaining lines filtered out); clicking a line adds one sourced line (`quotationLineId` + inherited `milestoneId`) and closes the picker.
- Two bulk buttons, one per tab: **"Add all milestones"** (Milestones tab - one direct line per remaining-fee milestone) and **"Add all lines"** (Quotations tab, shown once a quotation is selected - every remaining line from that quotation). Both skip anything already at zero remaining (same filter as the single-item lists), so a second bulk-add after some lines are already present never double-adds an already-fully-added source.
- `LineItemEditorComponent` drops its `milestones`/`quotationLines` dropdown inputs entirely (the picker replaces them) but keeps read-only display of a sourced line's inherited milestone name/quotation number as plain text next to the line, so the user can see what a line is sourced from without a dropdown.

## 5. Click-through navigation

Invoice list, quotation list, and milestone rows: the whole row navigates on click (a `(click)` handler on the `<tr>`/row container calling `router.navigate`, since `routerLink` can't target a `<tr>` directly), while any action buttons/links inside the row (Print, Send, Edit, Delete, Create invoice) call `$event.stopPropagation()` so clicking them doesn't also trigger the row navigation.

## 6. Client picker removed

`BillingRecipientPickerComponent` is removed from `billing-document-form-page.component.ts`'s template entirely, along with `clientId` from the component's state and both request payloads. `SendDocumentModalComponent` (used for the Send action) is unaffected - it already resolves recipients by role, not by a stored client.

## Testing

- Creating an invoice/quotation via the new picker: single milestone add, single quotation-line add, bulk-add-all-milestones, bulk-add-all-quotation-lines - each produces the correct line shape and correct remaining-amount filtering (already-sourced items don't reappear in the picker after being added, since the picker's "remaining" numbers should reflect what's already been added to the in-progress form, not just what's already saved server-side).
- A sourced line shows no Quantity input; an additional line does.
- Milestone row has no Quote this/Bill directly links; its expand panel shows both linked quotations and linked invoices.
- Clicking anywhere on an invoice/quotation/milestone row navigates correctly; clicking an action button inside a row does not also navigate.
- Invoice/quotation forms have no client field anywhere; submit succeeds without one.
- `ng build` clean.

## Out of scope

- Any further backend change beyond what the backend spec already covers.
- Mobile-specific layout beyond existing responsive Tailwind classes.
