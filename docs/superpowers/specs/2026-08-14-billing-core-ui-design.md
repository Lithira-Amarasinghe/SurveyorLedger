# Billing Core UI (Phase 1)

Frontend for the billing backend shipped in
`docs/superpowers/specs/2026-08-14-billing-core-design.md`. Covers Client,
Quotation, Invoice, and Payment — all three resources in one pass since they're
small and tightly linked (quote → invoice → payment).

## Context

Existing patterns to follow exactly (from Land/Job):
- `core/<feature>.service.ts` — typed `HttpClient` wrapper, one method per API
  endpoint, mirrors the controller 1:1. No component calls `HttpClient` directly.
- `pages/<feature>/<feature>-list.component.ts` — Material table + search + create
  button.
- `pages/<feature>/<feature>-detail-panel/` — view/edit form, used both as a route
  and embeddable panel (see `land-detail-panel`).
- Errors are handled per-component: a local `error` signal set from
  `err.error?.message` on the HTTP error response, rendered inline. No global
  toast/snackbar exists in this codebase — the interceptor only handles 401 refresh.
- Print output uses a dedicated route with no shell chrome and `window.print()`
  (see `land-print.component.ts`), not a PDF library.

## Navigation & routes

- New sidebar section "Billing" (peer of Land/Job): Clients, Quotations, Invoices.
- `/app/billing/clients`, `/app/billing/quotations`, `/app/billing/invoices` — list
  routes, each opening a detail panel/modal for view-edit, matching Land's split.
- Print routes, standalone (no shell): `/print/invoice/:id`, `/print/quotation/:id`,
  `/print/receipt/:paymentId`.
- `job-detail.component.ts` gets a new "Billing" tab: shows quotations/invoices for
  that job, filtered client-side from the full workspace lists by `jobId` (no
  backend change — list sizes are small in phase 1, matches how the job detail page
  already composes existing list data for other tabs).

## Services

Three new files under `ui/src/app/core/`:
- `client.service.ts` — `search(query?)`, `create(request)`, `get(id)`,
  `update(id, request)`, `delete(id)`, `getBalance(id)`, `getPayments(id)`
- `quotation.service.ts` — `search(clientId?)`, `create(request)`, `get(id)`,
  `update(id, request)`, `delete(id)`, `convertToInvoice(id, request)`
- `invoice.service.ts` — `search(clientId?)`, `create(request)`, `get(id)`,
  `update(id, request)`, `delete(id)`, `recordPayment(id, formData)`,
  `getPayments(id)`

Each mirrors its controller's DTO shapes (`ClientResponse`, `QuotationResponse`,
`InvoiceResponse`, `PaymentResponse`, `LineItemDto`, etc.) as TypeScript interfaces,
same convention as `land.service.ts`'s `Land`/`LandRequest`.

## Components

**Clients** (`pages/billing/clients/`)
- List: table (name, phone, email, outstanding balance), search box, create button
- Detail panel: name/phone/email/address form; read-only "Outstanding Balance" and
  "Payment History" sections (calls `getBalance`/`getPayments`, no separate route)

**Quotations** (`pages/billing/quotations/`)
- List: table (number, client, total, status, valid until), search/filter by client
- Detail panel: client picker (searchable, matches `owner-picker`'s combobox
  pattern), line-item editor (add/remove rows: description/qty/unit price), tax
  rate, valid-until date, status dropdown (Draft/Sent/Accepted/Rejected/Expired)
- "Convert to Invoice" button (visible when status is Draft/Sent) — opens a small
  confirm dialog for due date + discount amount, then calls
  `convertToInvoice` and navigates to the new invoice

**Invoices** (`pages/billing/invoices/`)
- List: table (number, client, total, balance, status, due date), search/filter by
  client
- Detail panel: same line-item editor as Quotation, discount amount, due date,
  status dropdown (Draft/Sent/Cancelled only — Paid/PartiallyPaid are server-set
  and shown read-only when active)
- Payments sub-list (receipt #, amount, method, date) with a "Record Payment"
  button opening a modal: amount, method dropdown (Cash/BankTransfer/Cheque),
  received date, reference number, optional proof file upload — submits as
  `FormData` to match the controller's `[FromForm]` binding

**Print pages** (`pages/billing/print/`)
- `invoice-print.component.ts`, `quotation-print.component.ts`,
  `receipt-print.component.ts` — standalone routes, no shell, print-styled
  (`@media print`), triggered via `window.print()`, same structure as
  `land-print.component.ts`

## Error handling

Every form (create/edit/record-payment/convert) uses the existing per-component
`error` signal pattern: catch the HTTP error, set
`error.set(err.error?.message ?? 'fallback message')`, render inline above the
submit button. Covers the backend's typed errors (400 validation, 403 forbidden,
404 not found, 409 conflict on delete-with-payments) uniformly — no new error
UI concept introduced.

## Out of scope

Real PDF generation/download, client-scoped billing portal (Client role gets
view-only per backend RBAC, no dedicated portal UI), reminders/notifications,
expense/payroll/dashboard UI (later phases), fee templates.
