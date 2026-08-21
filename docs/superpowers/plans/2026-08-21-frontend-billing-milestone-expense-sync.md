# Frontend Billing/Milestone/Expense Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the Angular frontend in sync with three backend features shipped this session (quotation-invoice line traceability, the milestone fee ceiling, workspace-level expenses) and add the convenience UI (per-line quotation-source picker, milestone quick-actions, category filters/running totals) that makes them usable.

**Architecture:** Update the three core services' types first (nothing downstream compiles correctly against stale types). Extend `LineItemEditorComponent` with a quotation-source picker. Rework `BillingDocumentFormPageComponent` to drop the old single-quotation checkbox-draw flow (superseded by per-line sourcing) and load the job's open quotation lines. Rework job-detail's milestone row for the committed/remaining ceiling display and quick actions, and its expense table/form for milestone tagging. Add a new workspace-level expense page reusing the existing modal.

**Tech Stack:** Angular 21 standalone components, signals, RxJS, Tailwind (existing `card`/`btn-primary`/`btn-secondary`/`input-field` utility classes, existing `icon-btn` + `<app-icon>` icon system).

## Global Constraints

- No new color palette - reuse existing `neutral`/`primary` Tailwind tokens throughout.
- No new icon needed - reuse the existing `<app-icon>` set (`banknote`, `rename`, `delete`, `chevronDown`, `chevronUp`).
- Manual verification via `ng build` + dev-server preview - this codebase has no component-test convention to extend.
- Commit after each task.

---

### Task 1: Sync `billing.service.ts`, `milestone.service.ts`, `expense.service.ts` types

**Files:**
- Modify: `ui/src/app/core/billing.service.ts`
- Modify: `ui/src/app/core/milestone.service.ts`
- Modify: `ui/src/app/core/expense.service.ts`

**Interfaces:**
- Produces: `LineItem { id?, description, quantity, unitPrice, milestoneId?, quotationLineId?, invoicedAmount?, remainingAmount? }`, `Milestone { ..., committedAmount, remainingAmount }`, `MilestonePaymentStatus { amount, committedAmount, remainingAmount, linkedInvoices: LinkedInvoiceSummary[], nextGate }`, `LinkedInvoiceSummary { invoiceId, number, status }`, `Expense { ..., jobId: string | null, milestoneId: string | null }`, `ExpenseRequest { ..., milestoneId?, jobId still absent - workspace-level methods take no jobId param }` - all consumed by every later task.

- [ ] **Step 1: Update `LineItem` and drop `Invoice.quotationId`/`InvoiceRequest.quotationId`**

In `ui/src/app/core/billing.service.ts`, replace the `LineItem` interface:

```typescript
export interface LineItem {
  id?: string;
  description: string;
  quantity: number;
  unitPrice: number;
  milestoneId?: string;
  quotationLineId?: string;
  invoicedAmount?: number;
  remainingAmount?: number;
}
```

Remove `quotationId: string | null;` from `Invoice` and `quotationId?: string;` from `InvoiceRequest`.

- [ ] **Step 2: Build to confirm the removed `quotationId` fields surface every stale reference**

Run: `cd ui && npx ng build 2>&1 | grep -i error`
Expected: errors in `billing-document-form-page.component.ts` (still assigns `quotationId: this.fromQuotation()?.quotationId` and reads `.lineItems` off `Quotation` assuming the old shape) - these are fixed in Task 3, not here. Confirm no *other* files reference `Invoice.quotationId`.

Run: `cd ui && grep -rn "\.quotationId" src/app --include=*.ts | grep -v billing-document-form-page`
Expected: no matches outside that one file (if something else matches, note it - it needs fixing in this task instead of Task 3).

- [ ] **Step 3: Update `Milestone` and `MilestonePaymentStatus`**

In `ui/src/app/core/milestone.service.ts`, replace:

```typescript
export interface Milestone {
  milestoneId: string;
  jobId: string;
  title: string;
  description: string | null;
  dueDate: string | null;
  amount: number | null;
  status: string;
  sortOrder: number;
  completedAt: string | null;
  completedBy: string | null;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  committedAmount: number;
  remainingAmount: number | null;
}
```

```typescript
export interface LinkedInvoiceSummary {
  invoiceId: string;
  number: string;
  status: string;
}

export interface MilestonePaymentStatus {
  amount: number | null;
  committedAmount: number;
  remainingAmount: number | null;
  linkedInvoices: LinkedInvoiceSummary[];
  nextGate: string | null;
}
```

- [ ] **Step 4: Update `Expense`/`ExpenseRequest` and add workspace-level methods**

In `ui/src/app/core/expense.service.ts`, replace:

```typescript
export interface Expense {
  expenseId: string;
  jobId: string | null;
  category: ExpenseCategory;
  amount: number;
  description: string | null;
  incurredDate: string;
  hasReceipt: boolean;
  payeeId: string | null;
  payeeName: string | null;
  payeeType: PayeeType | null;
  milestoneId: string | null;
  recordedByName: string;
  createdAt: string;
}

export interface ExpenseRequest {
  category: ExpenseCategory;
  amount: number;
  description?: string;
  incurredDate: string;
  payeeId?: string;
  payeeType?: PayeeType;
  milestoneId?: string;
}
```

Add workspace-level methods alongside the existing job-scoped ones, in the same class:

```typescript
  private workspaceBase(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/expense`;
  }

  getAllWorkspaceLevel(workspaceId: string): Observable<Expense[]> {
    return this.http.get<ApiResponse<Expense[]>>(this.workspaceBase(workspaceId)).pipe(map(res => res.data));
  }

  createWorkspaceLevel(workspaceId: string, request: ExpenseRequest): Observable<Expense> {
    return this.http.post<ApiResponse<Expense>>(this.workspaceBase(workspaceId), request).pipe(map(res => res.data));
  }

  updateWorkspaceLevel(workspaceId: string, expenseId: string, request: ExpenseRequest): Observable<Expense> {
    return this.http.put<ApiResponse<Expense>>(`${this.workspaceBase(workspaceId)}/${expenseId}`, request).pipe(map(res => res.data));
  }

  deleteWorkspaceLevel(workspaceId: string, expenseId: string): Observable<void> {
    return this.http.delete<void>(`${this.workspaceBase(workspaceId)}/${expenseId}`);
  }

  uploadWorkspaceLevelReceipt(workspaceId: string, expenseId: string, file: File): Observable<Expense> {
    const form = new FormData();
    form.append('file', file);
    return this.http
      .post<ApiResponse<Expense>>(`${this.workspaceBase(workspaceId)}/${expenseId}/receipt`, form)
      .pipe(map(res => res.data));
  }

  workspaceLevelReceiptUrl(workspaceId: string, expenseId: string): string {
    return `${this.workspaceBase(workspaceId)}/${expenseId}/receipt`;
  }
```

- [ ] **Step 5: Build**

Run: `cd ui && npx ng build 2>&1 | grep -i error`
Expected: errors only in files touched by later tasks (`billing-document-form-page.component.ts`, `job-detail.component.ts`, `expense-form-modal.component.ts` for the now-required-elsewhere `milestoneId` typing). List them, don't fix yet - confirms Task 1's blast radius matches the plan.

- [ ] **Step 6: Commit**

```bash
cd D:/Lithira/Projects/SurveyorLedger
git add ui/src/app/core/billing.service.ts ui/src/app/core/milestone.service.ts ui/src/app/core/expense.service.ts
git commit -m "feat: sync frontend types with quotation-invoice line, milestone ceiling, and expense backend changes

LineItem gains id/quotationLineId/invoicedAmount/remainingAmount.
Invoice/InvoiceRequest drop quotationId (removed backend-side).
Milestone gains committedAmount/remainingAmount. MilestonePaymentStatus
replaces its single linkedInvoice fields with a linkedInvoices list.
Expense gains milestoneId and nullable jobId; ExpenseService gains
workspace-level CRUD methods. Downstream components still reference the
old shapes - fixed in the following tasks, not compiling yet.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `LineItemEditorComponent` - quotation-source picker

**Files:**
- Modify: `ui/src/app/shared/line-item-editor/line-item-editor.component.ts`

**Interfaces:**
- Consumes: `LineItem` (Task 1).
- Produces: `@Input() quotationLines: QuotationLineSource[]` where `QuotationLineSource = { id: string; quotationNumber: string; description: string; milestoneId?: string; remainingAmount: number }`, consumed by Task 3.

- [ ] **Step 1: Add the `QuotationLineSource` type and the new input**

Replace the top of the file:

```typescript
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LineItem } from '../../core/billing.service';
import { Milestone } from '../../core/milestone.service';

export interface QuotationLineSource {
  id: string;
  quotationNumber: string;
  description: string;
  milestoneId?: string;
  remainingAmount: number;
}
```

- [ ] **Step 2: Add the Source dropdown to each row's template, and lock the milestone dropdown when a source is picked**

Replace the whole `@for` row block:

```html
        @for (item of items; track $index; let i = $index) {
          <div class="flex gap-sm items-start">
            <input
              class="input-field flex-1"
              placeholder="Description"
              [ngModel]="item.description"
              (ngModelChange)="updateItem(i, 'description', $event)"
              [name]="'desc-' + i"
            />
            <input
              class="input-field w-20"
              type="number"
              min="0"
              step="0.01"
              placeholder="Qty"
              [ngModel]="item.quantity"
              (ngModelChange)="updateItem(i, 'quantity', $event)"
              [name]="'qty-' + i"
            />
            <div>
              <input
                class="input-field w-28"
                type="number"
                min="0"
                step="0.01"
                placeholder="Unit price"
                [ngModel]="item.unitPrice"
                (ngModelChange)="updateItem(i, 'unitPrice', $event)"
                [name]="'price-' + i"
              />
              @if (item.quotationLineId) {
                <span class="block text-xs text-neutral-500 mt-2xs">max {{ sourceRemainingFor(item) | number: '1.2-2' }} remaining</span>
              }
            </div>
            @if (quotationLines.length > 0) {
              <select
                class="input-field w-48"
                [ngModel]="item.quotationLineId ?? ''"
                (ngModelChange)="onSourceChange(i, $event)"
                [name]="'source-' + i"
              >
                <option value="">No quotation (direct)</option>
                @for (source of quotationLines; track source.id) {
                  <option [value]="source.id">{{ source.quotationNumber }}: {{ source.description }} — {{ source.remainingAmount | number: '1.2-2' }} remaining</option>
                }
              </select>
            }
            @if (milestones.length > 0) {
              <select
                class="input-field w-40"
                [ngModel]="item.milestoneId ?? ''"
                (ngModelChange)="updateItem(i, 'milestoneId', $event || undefined)"
                [name]="'milestone-' + i"
                [disabled]="!!item.quotationLineId"
              >
                <option value="">No milestone (other fee)</option>
                @for (m of milestones; track m.milestoneId) {
                  <option [value]="m.milestoneId">{{ m.title }}</option>
                }
              </select>
            }
            <button type="button" class="text-primary-500 hover:text-primary-600 px-sm py-sm" (click)="removeItem(i)" title="Remove line">✕</button>
          </div>
        }
```

- [ ] **Step 3: Add `onSourceChange` and `sourceRemainingFor` methods**

Add to the class, alongside `updateItem`:

```typescript
  onSourceChange(index: number, quotationLineId: string): void {
    const updated = this.items.map((item, i) => {
      if (i !== index) return item;
      if (!quotationLineId) {
        const { quotationLineId: _drop, ...rest } = item;
        return rest;
      }
      const source = this.quotationLines.find(s => s.id === quotationLineId);
      if (!source) return item;
      return {
        ...item,
        quotationLineId: source.id,
        description: source.description,
        milestoneId: source.milestoneId
      };
    });
    this.itemsChange.emit(updated);
  }

  sourceRemainingFor(item: LineItem): number {
    return this.quotationLines.find(s => s.id === item.quotationLineId)?.remainingAmount ?? 0;
  }
```

Add the input near the existing ones:

```typescript
  @Input() quotationLines: QuotationLineSource[] = [];
```

- [ ] **Step 4: Build**

Run: `cd ui && npx ng build 2>&1 | grep -i error`
Expected: no new errors from this file (other files' errors from Task 1 still present, expected until Task 3).

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/shared/line-item-editor/line-item-editor.component.ts
git commit -m "feat: add quotation-line source picker to LineItemEditorComponent

Each line gets a Source dropdown - 'No quotation (direct)' or one of
the job's open quotation lines (with remaining balance shown). Picking
one auto-fills description and locks the milestone dropdown to the
source line's milestone, matching the backend's auto-copy/conflict-
reject behavior. Picking 'No quotation' unlocks the milestone dropdown
again for direct billing.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: `BillingDocumentFormPageComponent` - drop old draw-from-quotation flow, wire the new picker

**Files:**
- Modify: `ui/src/app/pages/billing/document-form/billing-document-form-page.component.ts`

**Interfaces:**
- Consumes: `LineItemEditorComponent`'s `quotationLines` input, `QuotationLineSource` (Task 2); `LineItem.remainingAmount`, `Invoice`/`InvoiceRequest` without `quotationId` (Task 1).

- [ ] **Step 1: Remove the old checkbox-draw UI block and its state**

Delete this template block:

```html
            @if (fromQuotation(); as quotation) {
              <div class="rounded bg-neutral-50 p-md">
                <p class="text-xs font-medium text-neutral-700 mb-sm">Draw from {{ quotation.number }}</p>
                <div class="space-y-xs">
                  @for (item of quotation.lineItems; track $index; let i = $index) {
                    <label class="flex items-center gap-sm text-sm text-neutral-700">
                      <input type="checkbox" [checked]="isDrawn(i)" (change)="toggleDraw(i, item)" />
                      {{ item.description }} - {{ item.quantity * item.unitPrice | number: '1.2-2' }}
                    </label>
                  }
                </div>
              </div>
            }
```

Replace `<app-line-item-editor [items]="lineItems" [milestones]="milestones()" (itemsChange)="lineItems = $event" />` with:

```html
              <app-line-item-editor
                [items]="lineItems"
                [milestones]="milestones()"
                [quotationLines]="documentType === 'invoice' ? quotationLines() : []"
                (itemsChange)="lineItems = $event"
              />
```

- [ ] **Step 2: Remove `fromQuotation`/`drawnIndexes`/`isDrawn`/`toggleDraw`, add `quotationLines` signal and loader**

Remove these class members:

```typescript
  fromQuotation = signal<Quotation | null>(null);
  drawnIndexes = new Set<number>();
```

and these methods:

```typescript
  isDrawn(index: number): boolean {
    return this.drawnIndexes.has(index);
  }

  toggleDraw(index: number, item: LineItem): void {
    if (this.drawnIndexes.has(index)) {
      this.drawnIndexes.delete(index);
      this.lineItems = this.lineItems.filter(li => li !== item);
    } else {
      this.drawnIndexes.add(index);
      this.lineItems = [...this.lineItems, { ...item }];
    }
  }
```

Add, near `milestones`:

```typescript
  quotationLines = signal<QuotationLineSource[]>([]);
```

Add the import at the top:

```typescript
import { LineItemEditorComponent, QuotationLineSource } from '../../../shared/line-item-editor/line-item-editor.component';
```

- [ ] **Step 3: Load quotation lines whenever the job is known, and simplify the `fromQuotationId` branch**

Add a new private method, next to `loadMilestones`:

```typescript
  private loadQuotationLines(): void {
    if (!this.jobId || this.documentType !== 'invoice') return;
    this.quotationService.search(this.workspaceId, undefined, this.jobId).subscribe({
      next: quotations => {
        const sources: QuotationLineSource[] = [];
        for (const q of quotations) {
          if (q.status === 'Rejected' || q.status === 'Expired') continue;
          for (const li of q.lineItems) {
            const remaining = li.remainingAmount ?? 0;
            if (!li.id || remaining <= 0) continue;
            sources.push({ id: li.id, quotationNumber: q.number, description: li.description, milestoneId: li.milestoneId, remainingAmount: remaining });
          }
        }
        this.quotationLines.set(sources);
      }
    });
  }
```

Call it everywhere `loadMilestones()` is currently called (the `!this.editingId && this.jobId` branch, the invoice-edit branch, the quotation-edit branch is skipped since `documentType !== 'invoice'` short-circuits inside the method itself, the `fromQuotationId` branch, and `onJobChange`). Concretely, replace each `this.loadMilestones();` call site with:

```typescript
          this.loadMilestones();
          this.loadQuotationLines();
```

Then simplify the `fromQuotationId` branch - replace:

```typescript
    } else if (fromQuotationId && this.documentType === 'invoice') {
      this.loading.set(true);
      this.quotationService.getById(this.workspaceId, fromQuotationId).subscribe({
        next: quotation => {
          this.fromQuotation.set(quotation);
          this.clientId = quotation.clientId;
          this.jobId = quotation.jobId;
          this.loadMilestones();
          this.lineItems = [];
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err.error?.message ?? 'Could not load quotation.');
          this.loading.set(false);
        }
      });
    } else if (milestoneId && this.jobId) {
      this.loading.set(true);
      this.milestoneService.getById(this.workspaceId, this.jobId, milestoneId).subscribe({
        next: milestone => {
          this.lineItems = [{ description: milestone.title, quantity: 1, unitPrice: milestone.amount ?? 0, milestoneId: milestone.milestoneId }];
          this.loading.set(false);
        },
        error: () => this.loading.set(false)
      });
    }
```

with:

```typescript
    } else if (fromQuotationId && this.documentType === 'invoice') {
      this.loading.set(true);
      this.quotationService.getById(this.workspaceId, fromQuotationId).subscribe({
        next: quotation => {
          this.clientId = quotation.clientId;
          this.jobId = quotation.jobId;
          this.loadMilestones();
          this.loadQuotationLines();
          this.lineItems = [{ description: '', quantity: 1, unitPrice: 0 }];
          this.loading.set(false);
        },
        error: err => {
          this.error.set(err.error?.message ?? 'Could not load quotation.');
          this.loading.set(false);
        }
      });
    } else if (milestoneId && this.jobId) {
      this.loading.set(true);
      this.milestoneService.getById(this.workspaceId, this.jobId, milestoneId).subscribe({
        next: milestone => {
          const amount = milestone.remainingAmount ?? milestone.amount ?? 0;
          this.lineItems = [{ description: milestone.title, quantity: 1, unitPrice: amount, milestoneId: milestone.milestoneId }];
          if (this.documentType === 'invoice') this.loadQuotationLines();
          this.loading.set(false);
        },
        error: () => this.loading.set(false)
      });
    }
```

- [ ] **Step 4: Remove `quotationId` from the invoice submit request**

Replace:

```typescript
      const request: InvoiceRequest = {
        clientId: this.clientId,
        jobId: this.jobId,
        quotationId: this.fromQuotation()?.quotationId,
        lineItems: this.lineItems,
```

with:

```typescript
      const request: InvoiceRequest = {
        clientId: this.clientId,
        jobId: this.jobId,
        lineItems: this.lineItems,
```

- [ ] **Step 5: Build**

Run: `cd ui && npx ng build 2>&1 | grep -i error`
Expected: 0 errors from this file. Check remaining errors are only in `job-detail.component.ts` and `expense-form-modal.component.ts` (Tasks 4-6).

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/pages/billing/document-form/billing-document-form-page.component.ts
git commit -m "refactor: replace single-quotation checkbox draw with per-line quotation sourcing

Invoice.QuotationId is gone backend-side - an invoice line now sources
from a specific quotation line via LineItemEditorComponent's new
picker, which can draw from any of the job's open quotations, not just
one passed via the fromQuotation query param. quotationLines() loads
whenever the job is known, filtered to lines with remainingAmount > 0.
The milestoneId prefill now uses the milestone's remainingAmount
instead of its full fee, matching the ceiling.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Milestone row - committed/remaining bar, quick actions, linked invoices, edit-form warning

**Files:**
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `Milestone.committedAmount`/`remainingAmount`, `MilestonePaymentStatus.linkedInvoices`/`committedAmount`/`remainingAmount` (Task 1).

- [ ] **Step 1: Replace the payment-status display block in the milestone row**

Replace this block (the `@if (milestonePaymentStatuses()[m.milestoneId]; as pay)` section):

```html
                      @if (milestonePaymentStatuses()[m.milestoneId]; as pay) {
                        @if (pay.linkedInvoiceId) {
                          <a
                            class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700"
                            [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', pay.linkedInvoiceId, 'edit']"
                          >{{ pay.amount | number: '1.2-2' }} · {{ pay.linkedInvoiceNumber }}</a>
                          @if (pay.nextGate) {
                            <span class="text-xs" [title]="pay.nextGate">🔒</span>
                          } @else {
                            <span class="text-xs" title="No payment blocking the next status">🔓</span>
                          }
                        } @else if (pay.amount) {
                          <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700">{{ pay.amount | number: '1.2-2' }}</span>
                          @if (!isClient()) {
                            <a
                              class="text-xs text-primary-500 hover:text-primary-600"
                              [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', 'new']"
                              [queryParams]="{ jobId: jobId, milestoneId: m.milestoneId }"
                            >Bill this milestone</a>
                          }
                        }
                      }
```

with:

```html
                      @if (milestonePaymentStatuses()[m.milestoneId]; as pay) {
                        @if (pay.amount) {
                          <button
                            type="button"
                            class="flex flex-col items-end gap-2xs text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700 hover:bg-neutral-200"
                            (click)="toggleMilestoneDetail(m.milestoneId)"
                            [title]="pay.nextGate ?? 'No payment blocking the next status'"
                          >
                            <span>{{ pay.committedAmount | number: '1.2-2' }} / {{ pay.amount | number: '1.2-2' }}</span>
                            <span class="w-24 h-1 rounded bg-neutral-200 overflow-hidden">
                              <span class="block h-full bg-primary-500" [style.width.%]="(pay.committedAmount / pay.amount) * 100"></span>
                            </span>
                          </button>
                          @if (!isClient()) {
                            @if ((pay.remainingAmount ?? 0) > 0) {
                              <a
                                class="text-xs text-primary-500 hover:text-primary-600"
                                [routerLink]="['/app/workspace', workspaceId, 'billing', 'quotations', 'new']"
                                [queryParams]="{ jobId: jobId, milestoneId: m.milestoneId }"
                              >Quote this</a>
                              <a
                                class="text-xs text-primary-500 hover:text-primary-600"
                                [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', 'new']"
                                [queryParams]="{ jobId: jobId, milestoneId: m.milestoneId }"
                              >Bill directly</a>
                            } @else {
                              <span class="text-xs text-neutral-500">Fully committed</span>
                            }
                          }
                        } @else if (!isClient()) {
                          <a
                            class="text-xs text-primary-500 hover:text-primary-600"
                            [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', 'new']"
                            [queryParams]="{ jobId: jobId, milestoneId: m.milestoneId }"
                          >Bill directly</a>
                        }
                      }
```

Note: when the milestone has no `amount` at all (`pay.amount` falsy), there's no ceiling to display, so only the direct-bill quick action shows - matches a fee-less milestone having nothing to quote a ceiling against, though it can still be billed ad hoc.

- [ ] **Step 2: Add the expanded linked-invoices panel, toggled by the new button**

Add this block right after the row's closing `</div>` (after the `@if (isClient())/@else` action-buttons block, at the same level as the existing `@if (editingRulesFor() === m.milestoneId)` block):

```html
                  @if (expandedMilestoneDetail() === m.milestoneId) {
                    <div class="px-md pb-md pt-sm border-t border-neutral-200 bg-neutral-50 rounded-b space-y-xs">
                      @if (milestonePaymentStatuses()[m.milestoneId]; as pay) {
                        <p class="text-xs text-neutral-600">Remaining: {{ (pay.remainingAmount ?? 0) | number: '1.2-2' }}</p>
                        @if (pay.linkedInvoices.length === 0) {
                          <p class="text-xs text-neutral-500">No invoices linked yet.</p>
                        } @else {
                          <div class="flex flex-wrap gap-sm">
                            @for (inv of pay.linkedInvoices; track inv.invoiceId) {
                              <a
                                class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700 hover:bg-neutral-200"
                                [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', inv.invoiceId, 'edit']"
                              >{{ inv.number }} · {{ inv.status }}</a>
                            }
                          </div>
                        }
                      }
                    </div>
                  }
```

- [ ] **Step 3: Add `expandedMilestoneDetail` signal and `toggleMilestoneDetail` method**

Add near `editingRulesFor`:

```typescript
  expandedMilestoneDetail = signal<string | null>(null);
```

Add near `toggleRulesEditor`:

```typescript
  toggleMilestoneDetail(milestoneId: string): void {
    this.expandedMilestoneDetail.update(current => (current === milestoneId ? null : milestoneId));
  }
```

- [ ] **Step 4: Add the committed-amount warning to the milestone edit form**

In the milestone edit form block (the one with `milestoneEditAmountDraft`), find the `Fee amount` input and add helper text after it. Locate:

```html
                    <input class="input-field text-sm" type="number" min="0" step="0.01" placeholder="Fee amount (optional)" [(ngModel)]="milestoneEditAmountDraft" />
```

(this is inside the editing form, distinct from the add-milestone form's identical-looking input at line ~338 which stays unchanged - the add form has nothing committed yet). Add immediately after it:

```html
                    <input class="input-field text-sm" type="number" min="0" step="0.01" placeholder="Fee amount (optional)" [(ngModel)]="milestoneEditAmountDraft" />
                    @if (m.committedAmount > 0) {
                      <p class="text-xs" [class.text-primary-500]="milestoneEditAmountDraft !== null && milestoneEditAmountDraft < m.committedAmount" [class.text-neutral-500]="milestoneEditAmountDraft === null || milestoneEditAmountDraft >= m.committedAmount">
                        Already committed: {{ m.committedAmount | number: '1.2-2' }}
                        @if (milestoneEditAmountDraft !== null && milestoneEditAmountDraft < m.committedAmount) {
                          — reducing below this may make the fee inconsistent with what's already billed.
                        }
                      </p>
                    }
```

- [ ] **Step 5: Build**

Run: `cd ui && npx ng build 2>&1 | grep -i error`
Expected: 0 errors from this file.

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: show milestone committed/remaining ceiling with quick actions

Money chip becomes a committed/fee progress bar. Expanding it shows the
remaining amount and every linked invoice (was single-invoice before
the fee-ceiling feature). Quote this / Bill directly quick actions
prefill the new quotation/invoice form via the existing milestoneId
query param, hidden once fully committed. Editing a milestone's fee
now shows what's already committed and warns if the new amount would
drop below it.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Job-scoped expense milestone tagging, category filter, running total

**Files:**
- Modify: `ui/src/app/pages/job/expense-form-modal/expense-form-modal.component.ts`
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `Milestone[]` (already loaded in job-detail), `Expense.milestoneId` (Task 1).
- Produces: `ExpenseFormModalComponent`'s `@Input() milestones: Milestone[]`, consumed by job-detail's template.

- [ ] **Step 1: Add the milestone dropdown to `ExpenseFormModalComponent`**

Add the import and input:

```typescript
import { Milestone } from '../../../core/milestone.service';
```

```typescript
  @Input() milestones: Milestone[] = [];
```

Add a `milestoneId` field and initialize it in `ngOnInit`:

```typescript
  milestoneId: string | null = null;
```

In `ngOnInit`, inside the `if (this.editing)` branch, add:

```typescript
      this.milestoneId = this.editing.milestoneId;
```

Add the dropdown to the template, right after the Category field's closing `</div>`:

```html
          @if (milestones.length > 0) {
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Milestone (optional)</label>
              <select class="input-field" name="milestoneId" [(ngModel)]="milestoneId">
                <option [ngValue]="null">No milestone</option>
                @for (m of milestones; track m.milestoneId) {
                  <option [ngValue]="m.milestoneId">{{ m.title }}</option>
                }
              </select>
            </div>
          }
```

Add `milestoneId: this.milestoneId ?? undefined` to the `request` object built in `submit()`:

```typescript
    const request: ExpenseRequest = {
      category: this.category,
      amount: this.amount,
      description: this.description || undefined,
      incurredDate: this.incurredDate,
      payeeId: this.category === 'StaffCost' ? this.payeeId! : undefined,
      payeeType: this.category === 'StaffCost' ? this.payeeType : undefined,
      milestoneId: this.milestoneId ?? undefined
    };
```

- [ ] **Step 2: Pass milestones into the modal and add a Milestone column + filter + running total to job-detail's expense table**

In `job-detail.component.ts`, find `<app-expense-form-modal` and add `[milestones]="milestones()"` alongside its existing inputs.

Replace the expense table section:

```html
        <div class="card">
          <div class="flex items-center justify-between mb-md">
            <h2 class="text-sm font-semibold text-neutral-900">Expenses</h2>
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="openExpenseModal()">+ Expense</button>
          </div>
          @if (jobExpenses().length === 0) {
            <p class="text-sm text-neutral-500">No expenses recorded on this job yet.</p>
          } @else {
            <div class="card p-0 overflow-x-auto">
              <table class="w-full text-sm">
                <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
                  <tr>
                    <th class="text-left px-lg py-sm font-medium">Date</th>
                    <th class="text-left px-lg py-sm font-medium">Category</th>
                    <th class="text-left px-lg py-sm font-medium">Payee</th>
                    <th class="text-left px-lg py-sm font-medium">Amount</th>
                    <th class="text-left px-lg py-sm font-medium"></th>
                  </tr>
                </thead>
                <tbody>
                  @for (expense of jobExpenses(); track expense.expenseId) {
                    <tr class="border-t border-neutral-200">
                      <td class="px-lg py-sm text-neutral-600">{{ expense.incurredDate | date: 'mediumDate' }}</td>
                      <td class="px-lg py-sm text-neutral-900">{{ expense.category }}</td>
                      <td class="px-lg py-sm text-neutral-600">{{ expense.payeeName ?? '—' }}</td>
                      <td class="px-lg py-sm text-neutral-600">{{ expense.amount | number: '1.2-2' }}</td>
                      <td class="px-lg py-sm text-right whitespace-nowrap">
                        <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700 mr-sm" (click)="openExpenseModal(expense)">Edit</button>
                        <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDeleteExpense.set(expense)">Delete</button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
```

with:

```html
        <div class="card">
          <div class="flex items-center justify-between mb-md">
            <h2 class="text-sm font-semibold text-neutral-900">Expenses</h2>
            <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="openExpenseModal()">+ Expense</button>
          </div>
          @if (jobExpenses().length === 0) {
            <p class="text-sm text-neutral-500">No expenses recorded on this job yet.</p>
          } @else {
            <div class="flex items-center gap-sm mb-sm">
              <select class="input-field w-40 py-xs text-xs" [(ngModel)]="expenseCategoryFilter">
                <option value="">All categories</option>
                @for (c of expenseCategories; track c) {
                  <option [value]="c">{{ c }}</option>
                }
              </select>
              @if (expenseMilestoneFilter()) {
                <button type="button" class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700 hover:bg-neutral-200" (click)="expenseMilestoneFilter.set(null)">
                  {{ milestoneTitle(expenseMilestoneFilter()!) }} ✕
                </button>
              }
            </div>
            <div class="card p-0 overflow-x-auto">
              <table class="w-full text-sm">
                <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
                  <tr>
                    <th class="text-left px-lg py-sm font-medium">Date</th>
                    <th class="text-left px-lg py-sm font-medium">Category</th>
                    <th class="text-left px-lg py-sm font-medium">Milestone</th>
                    <th class="text-left px-lg py-sm font-medium">Payee</th>
                    <th class="text-left px-lg py-sm font-medium">Amount</th>
                    <th class="text-left px-lg py-sm font-medium"></th>
                  </tr>
                </thead>
                <tbody>
                  @for (expense of filteredExpenses(); track expense.expenseId) {
                    <tr class="border-t border-neutral-200">
                      <td class="px-lg py-sm text-neutral-600">{{ expense.incurredDate | date: 'mediumDate' }}</td>
                      <td class="px-lg py-sm text-neutral-900">{{ expense.category }}</td>
                      <td class="px-lg py-sm">
                        @if (expense.milestoneId) {
                          <button type="button" class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700 hover:bg-neutral-200" (click)="expenseMilestoneFilter.set(expense.milestoneId)">
                            {{ milestoneTitle(expense.milestoneId) }}
                          </button>
                        } @else {
                          <span class="text-neutral-400">—</span>
                        }
                      </td>
                      <td class="px-lg py-sm text-neutral-600">{{ expense.payeeName ?? '—' }}</td>
                      <td class="px-lg py-sm text-neutral-600">{{ expense.amount | number: '1.2-2' }}</td>
                      <td class="px-lg py-sm text-right whitespace-nowrap">
                        <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700 mr-sm" (click)="openExpenseModal(expense)">Edit</button>
                        <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDeleteExpense.set(expense)">Delete</button>
                      </td>
                    </tr>
                  }
                </tbody>
                <tfoot>
                  <tr class="border-t border-neutral-300 bg-neutral-50 font-medium">
                    <td class="px-lg py-sm text-neutral-600" colspan="4">Total</td>
                    <td class="px-lg py-sm text-neutral-900">{{ filteredExpensesTotal() | number: '1.2-2' }}</td>
                    <td></td>
                  </tr>
                </tfoot>
              </table>
            </div>
          }
        </div>
```

- [ ] **Step 3: Add the filter state and computed values to the component class**

Add near `jobExpenses`:

```typescript
  expenseCategories = EXPENSE_CATEGORIES;
  expenseCategoryFilter = '';
  expenseMilestoneFilter = signal<string | null>(null);
```

Import `EXPENSE_CATEGORIES` from `expense.service.ts` alongside the existing `Expense, ExpenseService` import.

Add computed methods near `financialSummary`:

```typescript
  filteredExpenses(): Expense[] {
    return this.jobExpenses().filter(e =>
      (!this.expenseCategoryFilter || e.category === this.expenseCategoryFilter) &&
      (!this.expenseMilestoneFilter() || e.milestoneId === this.expenseMilestoneFilter())
    );
  }

  filteredExpensesTotal(): number {
    return this.filteredExpenses().reduce((sum, e) => sum + e.amount, 0);
  }

  milestoneTitle(milestoneId: string): string {
    return this.milestones().find(m => m.milestoneId === milestoneId)?.title ?? 'Unknown milestone';
  }
```

Note: `filteredExpenses()`/`filteredExpensesTotal()` are plain methods (not signals) called from the template each change-detection pass, matching this file's existing `financialSummary()` pattern - no new reactivity primitive introduced.

- [ ] **Step 4: Build**

Run: `cd ui && npx ng build 2>&1 | grep -i error`
Expected: 0 errors from these two files.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/pages/job/expense-form-modal/expense-form-modal.component.ts ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat: add milestone tagging, category filter, and running total to job expenses

ExpenseFormModalComponent gets an optional milestone dropdown when
milestones are passed in. Job-detail's expense table gains a Milestone
column (clickable chip that filters the table), a category filter, and
a running total footer row.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Workspace-level expenses - Billing tab, list page, route

**Files:**
- Modify: `ui/src/app/pages/billing/billing-tabs.component.ts`
- Modify: `ui/src/app/pages/job/expense-form-modal/expense-form-modal.component.ts`
- Create: `ui/src/app/pages/billing/expenses/workspace-expense-list.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `ExpenseService.getAllWorkspaceLevel`/`createWorkspaceLevel`/`updateWorkspaceLevel`/`deleteWorkspaceLevel` (Task 1).

- [ ] **Step 1: Add the Expenses tab**

In `billing-tabs.component.ts`, add a fourth `<a>` after the Clients one:

```html
      <a
        [routerLink]="['/app/workspace', workspaceId, 'billing', 'expenses']"
        routerLinkActive="border-primary-500 text-primary-600"
        class="px-md py-sm text-sm font-medium text-neutral-600 border-b-2 border-transparent hover:text-neutral-900"
      >
        Expenses
      </a>
```

- [ ] **Step 2: Make `ExpenseFormModalComponent` work without a `jobId`**

Change `@Input() jobId = '';` to `@Input() jobId: string | null = null;` and `@Input() participants: JobParticipant[] = [];` stays (already optional via default empty array - the StaffCost payee block already conditionally renders on `category === 'StaffCost'`, independent of `jobId`).

Replace the `submit()` method's save call:

```typescript
    const save$ = this.editing
      ? this.expenseService.update(this.workspaceId, this.jobId, this.editing.expenseId, request)
      : this.expenseService.create(this.workspaceId, this.jobId, request);
```

with:

```typescript
    const save$ = this.jobId
      ? this.editing
        ? this.expenseService.update(this.workspaceId, this.jobId, this.editing.expenseId, request)
        : this.expenseService.create(this.workspaceId, this.jobId, request)
      : this.editing
        ? this.expenseService.updateWorkspaceLevel(this.workspaceId, this.editing.expenseId, request)
        : this.expenseService.createWorkspaceLevel(this.workspaceId, request);
```

Do the same for the receipt-upload call inside `submit()`'s success handler:

```typescript
        if (this.receiptFile) {
          this.expenseService.uploadReceipt(this.workspaceId, this.jobId, expense.expenseId, this.receiptFile).subscribe({
```

becomes:

```typescript
        if (this.receiptFile) {
          const upload$ = this.jobId
            ? this.expenseService.uploadReceipt(this.workspaceId, this.jobId, expense.expenseId, this.receiptFile)
            : this.expenseService.uploadWorkspaceLevelReceipt(this.workspaceId, expense.expenseId, this.receiptFile);
          upload$.subscribe({
```

(the rest of that block - `next`/`error` handlers and closing braces - stays as-is, just re-indented under the new `upload$.subscribe`).

The milestone dropdown from Task 5 already only renders `@if (milestones.length > 0)`, and the workspace-level list page (Step 3 below) simply never passes `milestones`, so it stays empty and hidden there automatically - no extra guard needed.

- [ ] **Step 3: Create `WorkspaceExpenseListComponent`**

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { EXPENSE_CATEGORIES, Expense, ExpenseService } from '../../../core/expense.service';
import { CurrentWorkspaceService } from '../../../core/current-workspace.service';
import { BillingTabsComponent } from '../billing-tabs.component';
import { ExpenseFormModalComponent } from '../../job/expense-form-modal/expense-form-modal.component';

@Component({
  selector: 'app-workspace-expense-list',
  standalone: true,
  imports: [CommonModule, FormsModule, BillingTabsComponent, ExpenseFormModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <app-billing-tabs [workspaceId]="workspaceId" />

      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Expenses</h1>
        <button class="btn-primary" (click)="openModal()">+ Expense</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (expenses().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No workspace-level expenses yet.</div>
      } @else {
        <div class="flex items-center gap-sm mb-sm">
          <select class="input-field w-40 py-xs text-xs" [(ngModel)]="categoryFilter">
            <option value="">All categories</option>
            @for (c of categories; track c) {
              <option [value]="c">{{ c }}</option>
            }
          </select>
        </div>
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Date</th>
                <th class="text-left px-lg py-sm font-medium">Category</th>
                <th class="text-left px-lg py-sm font-medium">Payee</th>
                <th class="text-left px-lg py-sm font-medium">Amount</th>
                <th class="text-left px-lg py-sm font-medium"></th>
              </tr>
            </thead>
            <tbody>
              @for (expense of filteredExpenses(); track expense.expenseId) {
                <tr class="border-t border-neutral-200 hover:bg-neutral-50">
                  <td class="px-lg py-sm text-neutral-600">{{ expense.incurredDate | date: 'mediumDate' }}</td>
                  <td class="px-lg py-sm text-neutral-900">{{ expense.category }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ expense.payeeName ?? '—' }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ expense.amount | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm text-right whitespace-nowrap">
                    <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700 mr-sm" (click)="openModal(expense)">Edit</button>
                    <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="confirmingDelete.set(expense)">Delete</button>
                  </td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr class="border-t border-neutral-300 bg-neutral-50 font-medium">
                <td class="px-lg py-sm text-neutral-600" colspan="3">Total</td>
                <td class="px-lg py-sm text-neutral-900">{{ filteredTotal() | number: '1.2-2' }}</td>
                <td></td>
              </tr>
            </tfoot>
          </table>
        </div>
      }
    </div>

    @if (showModal()) {
      <app-expense-form-modal
        [workspaceId]="workspaceId"
        [editing]="editingExpense()"
        (cancel)="showModal.set(false); editingExpense.set(null)"
        (saved)="onSaved()"
      />
    }

    @if (confirmingDelete(); as expense) {
      <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="confirmingDelete.set(null)">
        <div class="card w-full max-w-sm" (click)="$event.stopPropagation()">
          <h2 class="text-base font-semibold text-neutral-900">Delete expense?</h2>
          <p class="text-sm text-neutral-600 mt-xs">{{ expense.category }} · {{ expense.amount | number: '1.2-2' }} will be deleted and cannot be undone.</p>
          <div class="flex justify-end gap-sm pt-md">
            <button type="button" class="btn-secondary flex-1 text-xs" (click)="confirmingDelete.set(null)">Cancel</button>
            <button type="button" class="btn-primary flex-1 text-xs" (click)="doDelete(expense)">Delete</button>
          </div>
        </div>
      </div>
    }
  `
})
export class WorkspaceExpenseListComponent implements OnInit {
  workspaceId = '';
  categories = EXPENSE_CATEGORIES;
  categoryFilter = '';
  expenses = signal<Expense[]>([]);
  loading = signal(true);
  error = signal('');
  showModal = signal(false);
  editingExpense = signal<Expense | null>(null);
  confirmingDelete = signal<Expense | null>(null);

  constructor(private expenseService: ExpenseService, private currentWorkspace: CurrentWorkspaceService) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.expenseService.getAllWorkspaceLevel(this.workspaceId).subscribe({
      next: expenses => {
        this.expenses.set(expenses);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load expenses.');
        this.loading.set(false);
      }
    });
  }

  filteredExpenses(): Expense[] {
    return this.expenses().filter(e => !this.categoryFilter || e.category === this.categoryFilter);
  }

  filteredTotal(): number {
    return this.filteredExpenses().reduce((sum, e) => sum + e.amount, 0);
  }

  openModal(expense: Expense | null = null): void {
    this.editingExpense.set(expense);
    this.showModal.set(true);
  }

  onSaved(): void {
    this.showModal.set(false);
    this.editingExpense.set(null);
    this.fetch();
  }

  doDelete(expense: Expense): void {
    this.expenseService.deleteWorkspaceLevel(this.workspaceId, expense.expenseId).subscribe({
      next: () => {
        this.confirmingDelete.set(null);
        this.fetch();
      },
      error: () => this.confirmingDelete.set(null)
    });
  }
}
```

- [ ] **Step 4: Register the route**

In `ui/src/app/app.routes.ts`, add alongside the other `billing/...` routes:

```typescript
          { path: 'billing/expenses', component: WorkspaceExpenseListComponent },
```

Add the import:

```typescript
import { WorkspaceExpenseListComponent } from './pages/billing/expenses/workspace-expense-list.component';
```

- [ ] **Step 5: Build**

Run: `cd ui && npx ng build 2>&1 | grep -i error`
Expected: 0 errors across the whole project - this is the last task, so the build should now be fully clean.

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/pages/billing/billing-tabs.component.ts ui/src/app/pages/job/expense-form-modal/expense-form-modal.component.ts ui/src/app/pages/billing/expenses/workspace-expense-list.component.ts ui/src/app/app.routes.ts
git commit -m "feat: add workspace-level expenses page under the Billing tabs

New Expenses tab alongside Invoices/Quotations/Clients, at
/app/workspace/:id/billing/expenses. Reuses ExpenseFormModalComponent
with jobId now optional - when absent it calls the workspace-level
ExpenseService methods instead of the job-scoped ones. Category filter
and running total, same pattern as the job-scoped expense table.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: End-to-end manual verification

**Files:** none (verification only)

- [ ] **Step 1: Start the dev server preview and confirm no console errors on load**

Use `preview_start` with the `ui` dev server config. Navigate to a workspace's Billing > Invoices > New page.

- [ ] **Step 2: Verify the quotation-source picker**

On a job with an existing quotation carrying a milestone-tagged line, open a new invoice for that job. Confirm the line editor's Source dropdown lists that quotation line with its remaining amount, picking it fills description and locks the milestone dropdown, and switching back to "No quotation" unlocks it.

- [ ] **Step 3: Verify the milestone row**

On the job detail page, confirm a fee-bearing milestone shows the committed/fee bar, clicking it expands to show remaining amount and linked invoices, and "Quote this"/"Bill directly" links navigate with the correct query params. Fully commit a milestone (bill its full remaining amount) and confirm the quick actions are replaced by "Fully committed".

- [ ] **Step 4: Verify workspace-level expenses**

Navigate to Billing > Expenses. Create a workspace-level expense, confirm it does not require a job/milestone field, appears in the list, category filter and running total work, and it does NOT appear in any job's expense tab.

- [ ] **Step 5: Verify job-scoped expense milestone tagging**

On a job detail page, create an expense tagged to a milestone. Confirm the Milestone column shows the tag, clicking the tag filters the table, and the running total updates with the filter.

- [ ] **Step 6: Take a screenshot of the milestone row and the workspace expenses list for the record, then report done**

No commit for this task - verification only.
