# Workspace financial reports (Finance plan, part 4)

## Context

Last piece of the finance plan. Admin needs a business-wide view across every job:
financial summary, payment history, expense history, outstanding invoices. Everything
underlying this already exists (Invoice/Payment/Expense) - this is a read-only
aggregation layer plus CSV export, not new financial data.

## Decisions

- **New page** `/app/workspace/:id/reports`, Admin-only, linked from workspace nav.
- **New permission `report.view`**, Admin only - one resource for all four read
  endpoints, since they're one page at one access level.
- **Four sections**, each backed by its own service method/endpoint (not one giant
  payload) so the UI can load/refresh them independently and a slow query in one
  section never blocks the others:
  1. `GET /workspace/{id}/reports/summary?from&to` - totals across every job in range:
     invoiced, paid, outstanding, expenses, gross profit, margin.
  2. `GET /workspace/{id}/reports/payments?from&to&page&pageSize` - every `Payment` in
     range, newest first.
  3. `GET /workspace/{id}/reports/expenses?from&to&page&pageSize` - every `Expense` in
     range, newest first.
  4. `GET /workspace/{id}/reports/outstanding-invoices` - every invoice with balance > 0,
     no date filter (a balance is a current fact, not a historical one) and no pagination
     (bounded by open-invoice count, not transaction volume - revisit if that assumption
     stops holding).
- **Pagination on history endpoints** (payments/expenses) - `page` (default 1),
  `pageSize` (default 50, max 200, clamped not rejected). A real business accumulates
  many payment/expense rows; returning everything unbounded doesn't scale. Response
  includes `totalCount` so the UI can page.
- **Date range validation**: `from` must be `<= to` when both given - `ValidationException`
  otherwise, same as every other bad-input case in this codebase. Missing `from`/`to`
  means unbounded on that side.
- **All four queries workspace-scoped** via `.Where(x => x.Job.WorkspaceId == workspaceId)`
  - no per-job access filtering needed since only Admin holds `report.view`.
- **CSV export is client-side only** - the UI already has the fetched JSON array; builds
  the CSV string in Angular (proper quoting: fields containing `,`, `"`, or a newline get
  wrapped in `"..."` with internal `"` doubled - RFC 4180) and triggers a browser download.
  No new backend endpoint, no new dependency.

## Migration

One EF Core migration adding the `report.view` permission + Admin-only role-permission
grant. No entity/table changes - this reads existing data. Generated via
`dotnet ef migrations add`, never hand-edited.

## Out of scope

- PDF export (QuestPDF exists for invoice/quotation PDFs; a report PDF template is a
  separate follow-up if asked for).
- Scheduled/emailed reports.
- Non-Admin access to any report (Surveyor/Client/Finance get nothing here).
- Charts/graphs - tables only for this pass.
