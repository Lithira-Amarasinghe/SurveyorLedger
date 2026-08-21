# Workspace letterhead for invoice/quotation PDFs

## Problem

Invoice and quotation PDFs (`PdfService.GenerateInvoicePdf`/`GenerateQuotationPdf`) currently render
a bare functional table with no company identity — just "Invoice {number}" as a plain header.
For these to work as real legal/billing documents, they need a letterhead: the issuing
company's logo, name, address, and contact details, consistently applied to every generated
PDF for a workspace.

## Scope

- Logo image + company text fields (name, address, phone, email, registration/tax number),
  editable once per workspace, rendered into the PDF header of every invoice and quotation.
- A new Workspace Settings page to edit it.
- Workspace admin/owner only can edit.
- Out of scope: per-document custom letterheads, a layout/color editor, letterhead on other
  document types (land documents, reports) — those already have their own generation paths
  and aren't part of this billing-document flow.

## Data model

Fields added directly to the existing `Workspace` entity (a workspace has exactly one
letterhead, no need for a separate table):

```csharp
public string? LetterheadCompanyName { get; set; }
public string? LetterheadAddress { get; set; }      // free-text, multi-line
public string? LetterheadPhone { get; set; }
public string? LetterheadEmail { get; set; }
public string? LetterheadRegistrationNumber { get; set; }
public string? LetterheadLogoPath { get; set; }      // storage path, like Expense.ReceiptFilePath
```

All nullable — a workspace with nothing set renders PDFs exactly as today (falls back to the
current plain "Invoice {number}" header), so this is purely additive.

## Backend

**`IWorkspaceService`** gains:
- `GetLetterheadAsync(workspaceId, callerUserId)` → returns the fields (view-gated, same as
  other workspace reads).
- `UpdateLetterheadAsync(workspaceId, callerUserId, request)` → updates the text fields.
  Gated by a new Casbin permission `workspace.manage_settings`, seeded to the Admin role only
  (mirrors the existing `manage_members` pattern) via a new migration.
- `UploadLetterheadLogoAsync(workspaceId, callerUserId, file)` / `DeleteLetterheadLogoAsync(...)` →
  reuses `IFileStorageService` the same way `ExpenseService.UploadReceipt` does; allowed types
  restricted to `.png/.jpg/.jpeg` (a logo, not a generic document), same size cap as existing
  file uploads (`DocumentService.MaxFileSizeBytes`).

**New endpoints** on `WorkspaceController`: `GET/PUT /workspace/{id}/letterhead`,
`POST/DELETE /workspace/{id}/letterhead/logo`.

**`PdfService`**: both `GenerateInvoicePdf`/`GenerateQuotationPdf` gain an optional
`WorkspaceLetterhead?` parameter (a small record: company name/address/phone/email/reg number
+ logo bytes, already loaded by the caller — `PdfService` stays storage-agnostic). When
present, the page header renders the logo (if any) beside the company block, with document
title/number below it; when absent, falls back to today's plain title. `InvoiceService`/
`QuotationService` load the workspace + logo bytes (via `IFileStorageService`) once per PDF
call and pass them through — same shape as how `invoice.Job` is already loaded for the
existing "Job: ..." line.

## Frontend

**New page** `workspace/:id/settings` (Angular route + component, added to the workspace
sidebar nav, visible only when `workspace.canManageSettings` — mirrors how Members/Roles
links are already gated by their own capability flags on the workspace-with-access response).

Form: logo upload with image preview (same file-input pattern as `ExpenseFormModalComponent`'s
receipt field), company name, address (textarea), phone, email, registration number, Save
button. No new picker/modal needed — it's a single settings form like the existing Budget
edit block on the job page.

## Testing

- Backend: `WorkspaceServiceTests` — permission gate (non-admin rejected), letterhead
  round-trip (set → get), logo upload replaces old file, PDF generation with/without
  letterhead present (smoke-test that it doesn't throw and includes expected text).
- No new frontend spec files per this repo's existing frontend testing posture (component
  specs aren't broadly used for form pages in this codebase already).
