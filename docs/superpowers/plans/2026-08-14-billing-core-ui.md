# Billing Core UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Angular UI for Client/Quotation/Invoice/Payment against the
billing backend shipped in `docs/superpowers/plans/2026-08-14-billing-core.md`,
per `docs/superpowers/specs/2026-08-14-billing-core-ui-design.md`.

**Architecture:** Standalone Angular components, one `core/billing.service.ts` with
three injectable services (`ClientService`, `QuotationService`, `InvoiceService`),
plain Tailwind-styled markup (no Material - this codebase doesn't use it), signals
for local state, `FormsModule`/`ngModel` for forms - all matching `land.service.ts`
and the `pages/land/` components exactly.

**Deviation from the UI spec, and why:** the spec described a "detail panel route"
per resource (mirroring Land's `land-detail-panel` + unsaved-changes-guard
machinery). Land's panel is that complex because it manages five nested
sub-resources (surveys/deeds/boundaries/photos) inline. Client/Quotation/Invoice
have no nested sub-resources - each is a flat form. A single create-or-edit modal
per resource (mirroring `create-land-modal`, extended to also handle edit) covers
the same functionality with far less code and no route/guard machinery, which is
the more minimalistic - and equally robust - choice for this data shape. Lists
still live on their own routes, matching `land-list.component.ts`.

**Tech Stack:** Angular 21 standalone components, RxJS, Tailwind CSS (existing
design tokens: `btn-primary`, `btn-secondary`, `card`, `input-field`, spacing
scale `xs/sm/md/lg`, color scale `neutral-*`/`primary-*`).

## Global Constraints

- No Angular Material - plain Tailwind markup only, matching every existing page.
- No new npm dependencies.
- Every service method mirrors its controller endpoint 1:1, same shape as
  `land.service.ts`.
- Every form's error handling: local `error = signal('')`, set from
  `err.error?.message ?? '<fallback>'`, rendered inline - no toast/snackbar exists
  in this codebase.
- Routes nest under `/app/workspace/:id/...` like every existing feature route.
- Print routes sit outside `AppShellComponent`'s children, as siblings at the top
  of `app.routes.ts`, exactly like the existing land-print route.

---

### Task 1: Billing core service (`core/billing.service.ts`)

**Files:**
- Create: `ui/src/app/core/billing.service.ts`

**Interfaces:**
- Produces: `Address` (reuse shape from `land.service.ts`'s own `Address`, redeclared
  here to keep this file self-contained - same fields: `street`, `city`, `district`,
  `postalCode`, `country`, all `string | null`), `Client`, `ClientRequest`,
  `LineItem`, `Quotation`, `QuotationRequest`, `ConvertQuotationRequest`,
  `Invoice`, `InvoiceRequest`, `Payment`, `PaymentRequest`, `ClientService`,
  `QuotationService`, `InvoiceService` - consumed by every later task.

- [ ] **Step 1: Write the service file**

```typescript
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Address {
  street: string | null;
  city: string | null;
  district: string | null;
  postalCode: string | null;
  country: string | null;
}

export interface LineItem {
  description: string;
  quantity: number;
  unitPrice: number;
}

export interface Client {
  clientId: string;
  name: string;
  phone: string | null;
  email: string | null;
  address: Address;
  createdAt: string;
  updatedAt: string;
}

export interface ClientRequest {
  name: string;
  phone?: string;
  email?: string;
  address?: Address;
}

export interface ClientBalance {
  clientId: string;
  outstandingBalance: number;
}

export type QuotationStatus = 'Draft' | 'Sent' | 'Accepted' | 'Rejected' | 'Expired';

export interface Quotation {
  quotationId: string;
  clientId: string;
  jobId: string | null;
  number: string;
  lineItems: LineItem[];
  taxRatePercent: number;
  subtotal: number;
  total: number;
  status: QuotationStatus;
  validUntil: string | null;
  revisionNumber: number;
  createdAt: string;
  updatedAt: string;
}

export interface QuotationRequest {
  clientId: string;
  jobId?: string;
  lineItems: LineItem[];
  taxRatePercent: number;
  validUntil?: string;
  status?: QuotationStatus;
}

export interface ConvertQuotationRequest {
  dueDate?: string;
  discountAmount: number;
}

export type InvoiceStatus = 'Draft' | 'Sent' | 'PartiallyPaid' | 'Paid' | 'Overdue' | 'Cancelled';
export type PaymentMethod = 'Cash' | 'BankTransfer' | 'Cheque';

export interface Invoice {
  invoiceId: string;
  clientId: string;
  jobId: string | null;
  quotationId: string | null;
  number: string;
  lineItems: LineItem[];
  taxRatePercent: number;
  discountAmount: number;
  subtotal: number;
  total: number;
  amountPaid: number;
  balance: number;
  status: InvoiceStatus;
  dueDate: string | null;
  isOverdue: boolean;
  daysOverdue: number;
  createdAt: string;
  updatedAt: string;
}

export interface InvoiceRequest {
  clientId: string;
  jobId?: string;
  lineItems: LineItem[];
  taxRatePercent: number;
  discountAmount: number;
  dueDate?: string;
  status?: 'Draft' | 'Sent' | 'Cancelled';
}

export interface Payment {
  paymentId: string;
  invoiceId: string;
  amount: number;
  method: PaymentMethod;
  receivedAt: string;
  referenceNumber: string | null;
  hasProofFile: boolean;
  receiptNumber: string;
  createdAt: string;
}

export interface PaymentRequest {
  amount: number;
  method: PaymentMethod;
  receivedAt: string;
  referenceNumber?: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class ClientService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/clients`;
  }

  search(workspaceId: string, query?: string): Observable<Client[]> {
    const params = query ? new HttpParams().set('query', query) : undefined;
    return this.http.get<ApiResponse<Client[]>>(this.base(workspaceId), { params }).pipe(map(res => res.data));
  }

  create(workspaceId: string, request: ClientRequest): Observable<Client> {
    return this.http.post<ApiResponse<Client>>(this.base(workspaceId), request).pipe(map(res => res.data));
  }

  getById(workspaceId: string, clientId: string): Observable<Client> {
    return this.http.get<ApiResponse<Client>>(`${this.base(workspaceId)}/${clientId}`).pipe(map(res => res.data));
  }

  update(workspaceId: string, clientId: string, request: ClientRequest): Observable<Client> {
    return this.http.put<ApiResponse<Client>>(`${this.base(workspaceId)}/${clientId}`, request).pipe(map(res => res.data));
  }

  delete(workspaceId: string, clientId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${clientId}`);
  }

  getBalance(workspaceId: string, clientId: string): Observable<ClientBalance> {
    return this.http.get<ApiResponse<ClientBalance>>(`${this.base(workspaceId)}/${clientId}/balance`).pipe(map(res => res.data));
  }

  getPayments(workspaceId: string, clientId: string): Observable<Payment[]> {
    return this.http.get<ApiResponse<Payment[]>>(`${this.base(workspaceId)}/${clientId}/payments`).pipe(map(res => res.data));
  }
}

@Injectable({ providedIn: 'root' })
export class QuotationService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/quotations`;
  }

  search(workspaceId: string, clientId?: string): Observable<Quotation[]> {
    const params = clientId ? new HttpParams().set('clientId', clientId) : undefined;
    return this.http.get<ApiResponse<Quotation[]>>(this.base(workspaceId), { params }).pipe(map(res => res.data));
  }

  create(workspaceId: string, request: QuotationRequest): Observable<Quotation> {
    return this.http.post<ApiResponse<Quotation>>(this.base(workspaceId), request).pipe(map(res => res.data));
  }

  getById(workspaceId: string, quotationId: string): Observable<Quotation> {
    return this.http.get<ApiResponse<Quotation>>(`${this.base(workspaceId)}/${quotationId}`).pipe(map(res => res.data));
  }

  update(workspaceId: string, quotationId: string, request: QuotationRequest): Observable<Quotation> {
    return this.http.put<ApiResponse<Quotation>>(`${this.base(workspaceId)}/${quotationId}`, request).pipe(map(res => res.data));
  }

  delete(workspaceId: string, quotationId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${quotationId}`);
  }

  convertToInvoice(workspaceId: string, quotationId: string, request: ConvertQuotationRequest): Observable<Invoice> {
    return this.http
      .post<ApiResponse<Invoice>>(`${this.base(workspaceId)}/${quotationId}/convert-to-invoice`, request)
      .pipe(map(res => res.data));
  }
}

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/invoices`;
  }

  search(workspaceId: string, clientId?: string): Observable<Invoice[]> {
    const params = clientId ? new HttpParams().set('clientId', clientId) : undefined;
    return this.http.get<ApiResponse<Invoice[]>>(this.base(workspaceId), { params }).pipe(map(res => res.data));
  }

  create(workspaceId: string, request: InvoiceRequest): Observable<Invoice> {
    return this.http.post<ApiResponse<Invoice>>(this.base(workspaceId), request).pipe(map(res => res.data));
  }

  getById(workspaceId: string, invoiceId: string): Observable<Invoice> {
    return this.http.get<ApiResponse<Invoice>>(`${this.base(workspaceId)}/${invoiceId}`).pipe(map(res => res.data));
  }

  update(workspaceId: string, invoiceId: string, request: InvoiceRequest): Observable<Invoice> {
    return this.http.put<ApiResponse<Invoice>>(`${this.base(workspaceId)}/${invoiceId}`, request).pipe(map(res => res.data));
  }

  delete(workspaceId: string, invoiceId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${invoiceId}`);
  }

  recordPayment(workspaceId: string, invoiceId: string, request: PaymentRequest, proofFile?: File): Observable<Payment> {
    const form = new FormData();
    form.append('amount', String(request.amount));
    form.append('method', request.method);
    form.append('receivedAt', request.receivedAt);
    if (request.referenceNumber) form.append('referenceNumber', request.referenceNumber);
    if (proofFile) form.append('proofFile', proofFile);
    return this.http.post<ApiResponse<Payment>>(`${this.base(workspaceId)}/${invoiceId}/payments`, form).pipe(map(res => res.data));
  }

  getPayments(workspaceId: string, invoiceId: string): Observable<Payment[]> {
    return this.http.get<ApiResponse<Payment[]>>(`${this.base(workspaceId)}/${invoiceId}/payments`).pipe(map(res => res.data));
  }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors referencing `billing.service.ts` (unrelated pre-existing errors,
if any, are not this task's concern).

- [ ] **Step 3: Commit**

```bash
git add ui/src/app/core/billing.service.ts
git commit -m "feat(ui): add billing core service layer"
```

---

### Task 2: Shared `ClientPickerComponent` and `LineItemEditorComponent`

**Files:**
- Create: `ui/src/app/shared/client-picker/client-picker.component.ts`
- Create: `ui/src/app/shared/line-item-editor/line-item-editor.component.ts`

**Interfaces:**
- Consumes: `Client`, `ClientService`, `LineItem` (Task 1)
- Produces: `ClientPickerComponent` with `@Input() workspaceId`,
  `@Input() value: string | null` (a `clientId`), `@Input() initialClientLabel: string | null`,
  `@Output() valueChange: EventEmitter<string | null>` - consumed by Tasks 5 and 6's
  quotation/invoice forms.
- Produces: `LineItemEditorComponent` with `@Input() items: LineItem[]`,
  `@Output() itemsChange: EventEmitter<LineItem[]>` - consumed by Tasks 5 and 6.

- [ ] **Step 1: Write the client picker**

Searchable single-select, same debounce/search-then-pick shape as
`owner-picker.component.ts` but simpler (no manual-entry fallback - a Client must
exist as a Client record, unlike Land's owner which can be a bare contact).

```typescript
import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { Client, ClientService } from '../../core/billing.service';

@Component({
  selector: 'app-client-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div>
      <label class="block text-xs font-medium text-neutral-700 mb-xs">Client</label>

      @if (selected(); as client) {
        <div class="flex items-center justify-between px-md py-sm rounded bg-neutral-50">
          <div>
            <span class="text-sm text-neutral-900">{{ client.name }}</span>
            @if (client.phone) {
              <span class="block text-xs text-neutral-500">{{ client.phone }}</span>
            }
          </div>
          <button type="button" class="text-xs text-primary-500 hover:text-primary-600" (click)="clear()">Change</button>
        </div>
      } @else {
        <input
          class="input-field"
          placeholder="Search clients by name, phone, or email…"
          [ngModel]="query"
          (ngModelChange)="onQueryChange($event)"
          name="clientSearch"
        />

        @if (searching()) {
          <p class="text-xs text-neutral-500 mt-xs">Searching…</p>
        } @else if (results().length > 0) {
          <div class="mt-xs border border-neutral-200 rounded divide-y divide-neutral-200">
            @for (client of results(); track client.clientId) {
              <button type="button" class="w-full text-left px-md py-sm hover:bg-neutral-50" (click)="select(client)">
                <span class="text-sm text-neutral-900">{{ client.name }}</span>
                @if (client.phone) {
                  <span class="block text-xs text-neutral-500">{{ client.phone }}</span>
                }
              </button>
            }
          </div>
        } @else if (query.trim().length >= 2) {
          <p class="text-xs text-neutral-500 mt-xs">No match. Create the client first from the Clients tab.</p>
        }
      }
    </div>
  `
})
export class ClientPickerComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() value: string | null = null;
  @Input() initialClientLabel: string | null = null;
  @Output() valueChange = new EventEmitter<string | null>();

  query = '';
  results = signal<Client[]>([]);
  searching = signal(false);
  selected = signal<Client | null>(null);

  private queries = new Subject<string>();

  constructor(private clientService: ClientService) {}

  ngOnInit(): void {
    if (this.value && this.initialClientLabel) {
      this.selected.set({
        clientId: this.value,
        name: this.initialClientLabel,
        phone: null,
        email: null,
        address: { street: null, city: null, district: null, postalCode: null, country: null },
        createdAt: '',
        updatedAt: ''
      });
    }

    this.queries
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap(term => this.clientService.search(this.workspaceId, term))
      )
      .subscribe({
        next: clients => {
          this.results.set(clients);
          this.searching.set(false);
        },
        error: () => {
          this.results.set([]);
          this.searching.set(false);
        }
      });
  }

  onQueryChange(term: string): void {
    this.query = term;
    this.searching.set(term.trim().length >= 2);
    if (term.trim().length < 2) {
      this.results.set([]);
      return;
    }
    this.queries.next(term);
  }

  select(client: Client): void {
    this.selected.set(client);
    this.results.set([]);
    this.query = '';
    this.valueChange.emit(client.clientId);
  }

  clear(): void {
    this.selected.set(null);
    this.valueChange.emit(null);
  }
}
```

- [ ] **Step 2: Write the line-item editor**

```typescript
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LineItem } from '../../core/billing.service';

@Component({
  selector: 'app-line-item-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div>
      <label class="block text-xs font-medium text-neutral-700 mb-xs">Line items</label>
      <div class="space-y-sm">
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
            <button type="button" class="text-primary-500 hover:text-primary-600 px-sm py-sm" (click)="removeItem(i)" title="Remove line">✕</button>
          </div>
        }
      </div>
      <button type="button" class="text-xs text-primary-500 hover:text-primary-600 mt-sm" (click)="addItem()">+ Add line item</button>

      <div class="mt-sm text-sm text-neutral-700 text-right">
        Subtotal: {{ subtotal() | number: '1.2-2' }}
      </div>
    </div>
  `
})
export class LineItemEditorComponent {
  @Input() items: LineItem[] = [];
  @Output() itemsChange = new EventEmitter<LineItem[]>();

  addItem(): void {
    this.itemsChange.emit([...this.items, { description: '', quantity: 1, unitPrice: 0 }]);
  }

  removeItem(index: number): void {
    this.itemsChange.emit(this.items.filter((_, i) => i !== index));
  }

  updateItem(index: number, field: keyof LineItem, value: string | number): void {
    const updated = this.items.map((item, i) => (i === index ? { ...item, [field]: field === 'description' ? value : Number(value) } : item));
    this.itemsChange.emit(updated);
  }

  subtotal(): number {
    return this.items.reduce((sum, item) => sum + item.quantity * item.unitPrice, 0);
  }
}
```

`LineItemEditorComponent`'s template uses the `number` pipe, so it needs
`CommonModule` (already imported, which provides `DecimalPipe` via `number`).

- [ ] **Step 3: Verify compilation**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors in either new file.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/shared/client-picker/ ui/src/app/shared/line-item-editor/
git commit -m "feat(ui): add client picker and line-item editor shared components"
```

---

### Task 3: Billing tab strip + sidebar/routes wiring

**Files:**
- Create: `ui/src/app/pages/billing/billing-tabs.component.ts`
- Modify: `ui/src/app/shell/sidebar.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Produces: `BillingTabsComponent` with `@Input() workspaceId`,
  `@Input() active: 'invoices' | 'quotations' | 'clients'` - consumed by Tasks
  4-6's list components (each page embeds this at the top).

- [ ] **Step 1: Write the tab strip**

```typescript
import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-billing-tabs',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  template: `
    <div class="flex gap-sm border-b border-neutral-200 mb-lg">
      <a
        [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices']"
        routerLinkActive="border-primary-500 text-primary-600"
        class="px-md py-sm text-sm font-medium text-neutral-600 border-b-2 border-transparent hover:text-neutral-900"
      >
        Invoices
      </a>
      <a
        [routerLink]="['/app/workspace', workspaceId, 'billing', 'quotations']"
        routerLinkActive="border-primary-500 text-primary-600"
        class="px-md py-sm text-sm font-medium text-neutral-600 border-b-2 border-transparent hover:text-neutral-900"
      >
        Quotations
      </a>
      <a
        [routerLink]="['/app/workspace', workspaceId, 'billing', 'clients']"
        routerLinkActive="border-primary-500 text-primary-600"
        class="px-md py-sm text-sm font-medium text-neutral-600 border-b-2 border-transparent hover:text-neutral-900"
      >
        Clients
      </a>
    </div>
  `
})
export class BillingTabsComponent {
  @Input() workspaceId = '';
}
```

- [ ] **Step 2: Add the sidebar link**

In `ui/src/app/shell/sidebar.component.ts`, add one link after the existing
"Land" link (between Land and Members, matching visual order of the workspace's
core work: Jobs → Land → Billing → Members):

```html
<a
  [routerLink]="['/app/workspace', ws.workspaceId, 'billing', 'invoices']"
  routerLinkActive="bg-primary-50 text-primary-600"
  class="flex items-center gap-sm px-md py-sm rounded text-sm text-neutral-700 hover:bg-neutral-100"
  (click)="navigate.emit()"
>
  Billing
</a>
```

Insert it directly after the `Land` `<a>` block and before the `Members` `<a>`
block (both already present in the file, per `ui/src/app/shell/sidebar.component.ts:41-56`).

- [ ] **Step 3: Add placeholder route entries (components wired in Tasks 4-6)**

This step only reserves the route shape; Tasks 4-6 supply the actual components.
Skip actually registering routes here - do it in Task 6 once every component this
plan needs to reference exists, so `app.routes.ts` only changes once. Mark this
step done with no action; it exists to document the dependency for whoever reads
tasks out of order.

- [ ] **Step 4: Verify compilation**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors in `billing-tabs.component.ts` or `sidebar.component.ts`.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/pages/billing/billing-tabs.component.ts ui/src/app/shell/sidebar.component.ts
git commit -m "feat(ui): add billing tab strip and sidebar link"
```

---

### Task 4: Clients list + create/edit modal

**Files:**
- Create: `ui/src/app/pages/billing/clients/client-list.component.ts`
- Create: `ui/src/app/pages/billing/clients/client-form-modal/client-form-modal.component.ts`

**Interfaces:**
- Consumes: `Client`, `ClientRequest`, `ClientService` (Task 1),
  `BillingTabsComponent` (Task 3)
- Produces: `ClientListComponent` (routed in Task 6), `ClientFormModalComponent`
  with `@Input() workspaceId`, `@Input() editing: Client | null` (null = create
  mode), `@Output() cancel`, `@Output() saved: EventEmitter<Client>`.

- [ ] **Step 1: Write the create/edit modal**

```typescript
import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Address, Client, ClientRequest, ClientService } from '../../../../core/billing.service';

@Component({
  selector: 'app-client-form-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-md" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">{{ editing ? 'Edit client' : 'New client' }}</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Name</label>
            <input class="input-field" type="text" name="name" [(ngModel)]="name" required autofocus />
          </div>
          <div class="grid grid-cols-2 gap-sm">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Phone</label>
              <input class="input-field" type="text" name="phone" [(ngModel)]="phone" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Email</label>
              <input class="input-field" type="email" name="email" [(ngModel)]="email" />
            </div>
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Street</label>
            <input class="input-field" type="text" name="street" [(ngModel)]="street" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">City</label>
            <input class="input-field" type="text" name="city" [(ngModel)]="city" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !name.trim()">
              {{ loading() ? 'Saving…' : editing ? 'Save' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class ClientFormModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() editing: Client | null = null;
  @Output() cancel = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Client>();

  name = '';
  phone = '';
  email = '';
  street = '';
  city = '';
  loading = signal(false);
  error = signal('');

  constructor(private clientService: ClientService) {}

  ngOnInit(): void {
    if (this.editing) {
      this.name = this.editing.name;
      this.phone = this.editing.phone ?? '';
      this.email = this.editing.email ?? '';
      this.street = this.editing.address.street ?? '';
      this.city = this.editing.address.city ?? '';
    }
  }

  submit(): void {
    if (!this.name.trim()) return;
    this.error.set('');
    this.loading.set(true);

    const address: Address = { street: this.street.trim() || null, city: this.city.trim() || null, district: null, postalCode: null, country: null };
    const request: ClientRequest = {
      name: this.name.trim(),
      phone: this.phone.trim() || undefined,
      email: this.email.trim() || undefined,
      address
    };

    const save$ = this.editing
      ? this.clientService.update(this.workspaceId, this.editing.clientId, request)
      : this.clientService.create(this.workspaceId, request);

    save$.subscribe({
      next: client => {
        this.loading.set(false);
        this.saved.emit(client);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not save client.');
      }
    });
  }
}
```

- [ ] **Step 2: Write the list page**

Balances are fetched per-row after the client list loads, same `forkJoin`
composition pattern `land-list.component.ts` uses for its deed/survey counts.

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Client, ClientService } from '../../../core/billing.service';
import { CurrentWorkspaceService } from '../../../core/current-workspace.service';
import { BillingTabsComponent } from '../billing-tabs.component';
import { ClientFormModalComponent } from './client-form-modal/client-form-modal.component';

interface ClientRow {
  client: Client;
  balance: number;
}

@Component({
  selector: 'app-client-list',
  standalone: true,
  imports: [CommonModule, BillingTabsComponent, ClientFormModalComponent],
  template: `
    <div class="p-lg max-w-4xl mx-auto">
      <app-billing-tabs [workspaceId]="workspaceId" active="clients" />

      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Clients</h1>
        <button class="btn-primary" (click)="openCreate()">New client</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (rows().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No clients yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Name</th>
                <th class="text-left px-lg py-sm font-medium">Phone</th>
                <th class="text-left px-lg py-sm font-medium">Email</th>
                <th class="text-left px-lg py-sm font-medium">Outstanding balance</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.client.clientId) {
                <tr class="border-t border-neutral-200 cursor-pointer hover:bg-neutral-50" (click)="openEdit(row.client)">
                  <td class="px-lg py-sm text-neutral-900">{{ row.client.name }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.client.phone ?? '—' }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ row.client.email ?? '—' }}</td>
                  <td class="px-lg py-sm" [class.text-primary-600]="row.balance > 0" [class.font-medium]="row.balance > 0">
                    {{ row.balance | number: '1.2-2' }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-client-form-modal [workspaceId]="workspaceId" [editing]="editingClient()" (cancel)="closeModal()" (saved)="onSaved()" />
    }
  `
})
export class ClientListComponent implements OnInit {
  workspaceId = '';
  rows = signal<ClientRow[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);
  editingClient = signal<Client | null>(null);

  constructor(private clientService: ClientService, private currentWorkspace: CurrentWorkspaceService) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.clientService.search(this.workspaceId).subscribe({
      next: clients => {
        if (clients.length === 0) {
          this.rows.set([]);
          this.loading.set(false);
          return;
        }
        forkJoin(
          clients.map(client =>
            this.clientService.getBalance(this.workspaceId, client.clientId).pipe(
              catchError(() => of({ clientId: client.clientId, outstandingBalance: 0 }))
            )
          )
        ).subscribe(balances => {
          this.rows.set(clients.map((client, i) => ({ client, balance: balances[i].outstandingBalance })));
          this.loading.set(false);
        });
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load clients.');
        this.loading.set(false);
      }
    });
  }

  openCreate(): void {
    this.editingClient.set(null);
    this.modalOpen.set(true);
  }

  openEdit(client: Client): void {
    this.editingClient.set(client);
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  onSaved(): void {
    this.modalOpen.set(false);
    this.fetch();
  }
}
```

- [ ] **Step 3: Verify compilation**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors in either new file.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/billing/clients/
git commit -m "feat(ui): add clients list and create/edit modal"
```

---

### Task 5: Quotations list + create/edit modal + convert-to-invoice

**Files:**
- Create: `ui/src/app/pages/billing/quotations/quotation-list.component.ts`
- Create: `ui/src/app/pages/billing/quotations/quotation-form-modal/quotation-form-modal.component.ts`
- Create: `ui/src/app/pages/billing/quotations/convert-modal/convert-quotation-modal.component.ts`

**Interfaces:**
- Consumes: `Quotation`, `QuotationRequest`, `ConvertQuotationRequest`,
  `QuotationService`, `LineItem` (Task 1), `ClientPickerComponent`,
  `LineItemEditorComponent` (Task 2), `BillingTabsComponent` (Task 3)
- Produces: `QuotationListComponent` (routed in Task 6)

- [ ] **Step 1: Write the create/edit modal**

```typescript
import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LineItem, Quotation, QuotationRequest, QuotationService, QuotationStatus } from '../../../../core/billing.service';
import { ClientPickerComponent } from '../../../../shared/client-picker/client-picker.component';
import { LineItemEditorComponent } from '../../../../shared/line-item-editor/line-item-editor.component';

@Component({
  selector: 'app-quotation-form-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, ClientPickerComponent, LineItemEditorComponent],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-lg" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">{{ editing ? 'Edit quotation' : 'New quotation' }}</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <app-client-picker
            [workspaceId]="workspaceId"
            [value]="clientId"
            [initialClientLabel]="editing?.clientId ? initialClientLabel : null"
            (valueChange)="clientId = $event"
          />

          <app-line-item-editor [items]="lineItems" (itemsChange)="lineItems = $event" />

          <div class="grid grid-cols-2 gap-sm">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Tax rate (%)</label>
              <input class="input-field" type="number" min="0" step="0.01" name="taxRate" [(ngModel)]="taxRatePercent" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Valid until</label>
              <input class="input-field" type="date" name="validUntil" [(ngModel)]="validUntil" />
            </div>
          </div>

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Status</label>
            <select class="input-field" name="status" [(ngModel)]="status">
              <option value="Draft">Draft</option>
              <option value="Sent">Sent</option>
              <option value="Accepted">Accepted</option>
              <option value="Rejected">Rejected</option>
              <option value="Expired">Expired</option>
            </select>
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !clientId || lineItems.length === 0">
              {{ loading() ? 'Saving…' : editing ? 'Save' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class QuotationFormModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() editing: Quotation | null = null;
  @Input() initialClientLabel: string | null = null;
  @Output() cancel = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Quotation>();

  clientId: string | null = null;
  lineItems: LineItem[] = [{ description: '', quantity: 1, unitPrice: 0 }];
  taxRatePercent = 0;
  validUntil = '';
  status: QuotationStatus = 'Draft';
  loading = signal(false);
  error = signal('');

  constructor(private quotationService: QuotationService) {}

  ngOnInit(): void {
    if (this.editing) {
      this.clientId = this.editing.clientId;
      this.lineItems = this.editing.lineItems.length > 0 ? [...this.editing.lineItems] : [{ description: '', quantity: 1, unitPrice: 0 }];
      this.taxRatePercent = this.editing.taxRatePercent;
      this.validUntil = this.editing.validUntil ? this.editing.validUntil.substring(0, 10) : '';
      this.status = this.editing.status;
    }
  }

  submit(): void {
    if (!this.clientId || this.lineItems.length === 0) return;
    this.error.set('');
    this.loading.set(true);

    const request: QuotationRequest = {
      clientId: this.clientId,
      lineItems: this.lineItems,
      taxRatePercent: this.taxRatePercent,
      validUntil: this.validUntil || undefined,
      status: this.status
    };

    const save$ = this.editing
      ? this.quotationService.update(this.workspaceId, this.editing.quotationId, request)
      : this.quotationService.create(this.workspaceId, request);

    save$.subscribe({
      next: quotation => {
        this.loading.set(false);
        this.saved.emit(quotation);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not save quotation.');
      }
    });
  }
}
```

- [ ] **Step 2: Write the convert-to-invoice confirm modal**

```typescript
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Invoice, Quotation, QuotationService } from '../../../../core/billing.service';

@Component({
  selector: 'app-convert-quotation-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-sm" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">Convert to invoice</h2>
        <p class="text-sm text-neutral-600 mt-xs">
          Quotation {{ quotation.number }} will become a new invoice with the same line items.
        </p>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Due date</label>
            <input class="input-field" type="date" name="dueDate" [(ngModel)]="dueDate" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Discount amount</label>
            <input class="input-field" type="number" min="0" step="0.01" name="discount" [(ngModel)]="discountAmount" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading()">{{ loading() ? 'Converting…' : 'Convert' }}</button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class ConvertQuotationModalComponent {
  @Input() workspaceId = '';
  @Input() quotation!: Quotation;
  @Output() cancel = new EventEmitter<void>();
  @Output() converted = new EventEmitter<Invoice>();

  dueDate = '';
  discountAmount = 0;
  loading = signal(false);
  error = signal('');

  constructor(private quotationService: QuotationService) {}

  submit(): void {
    this.error.set('');
    this.loading.set(true);
    this.quotationService.convertToInvoice(this.workspaceId, this.quotation.quotationId, { dueDate: this.dueDate || undefined, discountAmount: this.discountAmount }).subscribe({
      next: invoice => {
        this.loading.set(false);
        this.converted.emit(invoice);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not convert quotation.');
      }
    });
  }
}
```

- [ ] **Step 3: Write the list page**

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Quotation, QuotationService } from '../../../core/billing.service';
import { CurrentWorkspaceService } from '../../../core/current-workspace.service';
import { BillingTabsComponent } from '../billing-tabs.component';
import { QuotationFormModalComponent } from './quotation-form-modal/quotation-form-modal.component';
import { ConvertQuotationModalComponent } from './convert-modal/convert-quotation-modal.component';

@Component({
  selector: 'app-quotation-list',
  standalone: true,
  imports: [CommonModule, BillingTabsComponent, QuotationFormModalComponent, ConvertQuotationModalComponent],
  template: `
    <div class="p-lg max-w-5xl mx-auto">
      <app-billing-tabs [workspaceId]="workspaceId" active="quotations" />

      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Quotations</h1>
        <button class="btn-primary" (click)="openCreate()">New quotation</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (quotations().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No quotations yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Number</th>
                <th class="text-left px-lg py-sm font-medium">Total</th>
                <th class="text-left px-lg py-sm font-medium">Status</th>
                <th class="text-left px-lg py-sm font-medium">Valid until</th>
                <th class="px-lg py-sm"></th>
              </tr>
            </thead>
            <tbody>
              @for (quotation of quotations(); track quotation.quotationId) {
                <tr class="border-t border-neutral-200 hover:bg-neutral-50">
                  <td class="px-lg py-sm text-neutral-900 cursor-pointer" (click)="openEdit(quotation)">{{ quotation.number }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ quotation.total | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm">
                    <span class="text-xs px-sm py-xs rounded bg-neutral-100 text-neutral-700">{{ quotation.status }}</span>
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ quotation.validUntil ? (quotation.validUntil | date: 'mediumDate') : '—' }}</td>
                  <td class="px-lg py-sm text-right">
                    @if (quotation.status === 'Draft' || quotation.status === 'Sent') {
                      <button class="text-xs text-primary-500 hover:text-primary-600" (click)="openConvert(quotation)">Convert to invoice</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-quotation-form-modal [workspaceId]="workspaceId" [editing]="editingQuotation()" (cancel)="closeModal()" (saved)="onSaved()" />
    }
    @if (convertingQuotation(); as quotation) {
      <app-convert-quotation-modal [workspaceId]="workspaceId" [quotation]="quotation" (cancel)="convertingQuotation.set(null)" (converted)="onConverted($event)" />
    }
  `
})
export class QuotationListComponent implements OnInit {
  workspaceId = '';
  quotations = signal<Quotation[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);
  editingQuotation = signal<Quotation | null>(null);
  convertingQuotation = signal<Quotation | null>(null);

  constructor(private quotationService: QuotationService, private currentWorkspace: CurrentWorkspaceService, private router: Router) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.quotationService.search(this.workspaceId).subscribe({
      next: quotations => {
        this.quotations.set(quotations);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load quotations.');
        this.loading.set(false);
      }
    });
  }

  openCreate(): void {
    this.editingQuotation.set(null);
    this.modalOpen.set(true);
  }

  openEdit(quotation: Quotation): void {
    this.editingQuotation.set(quotation);
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  onSaved(): void {
    this.modalOpen.set(false);
    this.fetch();
  }

  openConvert(quotation: Quotation): void {
    this.convertingQuotation.set(quotation);
  }

  onConverted(invoice: import('../../../core/billing.service').Invoice): void {
    this.convertingQuotation.set(null);
    this.router.navigate(['/app/workspace', this.workspaceId, 'billing', 'invoices']);
  }
}
```

- [ ] **Step 4: Verify compilation**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors in any of the three new files.

- [ ] **Step 5: Commit**

```bash
git add ui/src/app/pages/billing/quotations/
git commit -m "feat(ui): add quotations list, create/edit modal, and convert-to-invoice"
```

---

### Task 6: Invoices list + create/edit modal + record-payment modal + routes

**Files:**
- Create: `ui/src/app/pages/billing/invoices/invoice-list.component.ts`
- Create: `ui/src/app/pages/billing/invoices/invoice-form-modal/invoice-form-modal.component.ts`
- Create: `ui/src/app/pages/billing/invoices/record-payment-modal/record-payment-modal.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `Invoice`, `InvoiceRequest`, `Payment`, `PaymentRequest`,
  `InvoiceService` (Task 1), `ClientPickerComponent`, `LineItemEditorComponent`
  (Task 2), `BillingTabsComponent` (Task 3), `ClientListComponent` (Task 4),
  `QuotationListComponent` (Task 5)
- Produces: `InvoiceListComponent`, routed as
  `/app/workspace/:id/billing/invoices|quotations|clients`

- [ ] **Step 1: Write the record-payment modal**

```typescript
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Invoice, InvoiceService, Payment, PaymentMethod } from '../../../../core/billing.service';

@Component({
  selector: 'app-record-payment-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-sm" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">Record payment</h2>
        <p class="text-sm text-neutral-600 mt-xs">Outstanding balance: {{ invoice.balance | number: '1.2-2' }}</p>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Amount</label>
            <input class="input-field" type="number" min="0.01" step="0.01" name="amount" [(ngModel)]="amount" required autofocus />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Method</label>
            <select class="input-field" name="method" [(ngModel)]="method">
              <option value="Cash">Cash</option>
              <option value="BankTransfer">Bank transfer</option>
              <option value="Cheque">Cheque</option>
            </select>
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Received date</label>
            <input class="input-field" type="date" name="receivedAt" [(ngModel)]="receivedAt" required />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Reference number</label>
            <input class="input-field" type="text" name="referenceNumber" [(ngModel)]="referenceNumber" placeholder="Cheque #, transaction ref…" />
          </div>
          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Proof of payment (optional)</label>
            <input class="input-field" type="file" (change)="onFileChange($event)" />
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || amount <= 0 || !receivedAt">
              {{ loading() ? 'Recording…' : 'Record payment' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class RecordPaymentModalComponent {
  @Input() workspaceId = '';
  @Input() invoice!: Invoice;
  @Output() cancel = new EventEmitter<void>();
  @Output() recorded = new EventEmitter<Payment>();

  amount = 0;
  method: PaymentMethod = 'Cash';
  receivedAt = new Date().toISOString().substring(0, 10);
  referenceNumber = '';
  proofFile: File | undefined;
  loading = signal(false);
  error = signal('');

  constructor(private invoiceService: InvoiceService) {}

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.proofFile = input.files?.[0];
  }

  submit(): void {
    if (this.amount <= 0 || !this.receivedAt) return;
    this.error.set('');
    this.loading.set(true);

    this.invoiceService
      .recordPayment(
        this.workspaceId,
        this.invoice.invoiceId,
        { amount: this.amount, method: this.method, receivedAt: this.receivedAt, referenceNumber: this.referenceNumber.trim() || undefined },
        this.proofFile
      )
      .subscribe({
        next: payment => {
          this.loading.set(false);
          this.recorded.emit(payment);
        },
        error: err => {
          this.loading.set(false);
          this.error.set(err.error?.message ?? 'Could not record payment.');
        }
      });
  }
}
```

- [ ] **Step 2: Write the create/edit modal**

```typescript
import { Component, EventEmitter, Input, OnInit, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Invoice, InvoiceRequest, InvoiceService, LineItem } from '../../../../core/billing.service';
import { ClientPickerComponent } from '../../../../shared/client-picker/client-picker.component';
import { LineItemEditorComponent } from '../../../../shared/line-item-editor/line-item-editor.component';

@Component({
  selector: 'app-invoice-form-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, ClientPickerComponent, LineItemEditorComponent],
  template: `
    <div class="fixed inset-0 z-50 bg-neutral-900/40 flex items-center justify-center px-lg" (click)="cancel.emit()">
      <div class="card w-full max-w-lg" (click)="$event.stopPropagation()">
        <h2 class="text-base font-semibold text-neutral-900">{{ editing ? 'Edit invoice' : 'New invoice' }}</h2>

        <form class="mt-lg space-y-md" (ngSubmit)="submit()">
          <app-client-picker [workspaceId]="workspaceId" [value]="clientId" (valueChange)="clientId = $event" />

          <app-line-item-editor [items]="lineItems" (itemsChange)="lineItems = $event" />

          <div class="grid grid-cols-3 gap-sm">
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Tax rate (%)</label>
              <input class="input-field" type="number" min="0" step="0.01" name="taxRate" [(ngModel)]="taxRatePercent" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Discount</label>
              <input class="input-field" type="number" min="0" step="0.01" name="discount" [(ngModel)]="discountAmount" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-700 mb-xs">Due date</label>
              <input class="input-field" type="date" name="dueDate" [(ngModel)]="dueDate" />
            </div>
          </div>

          <div>
            <label class="block text-xs font-medium text-neutral-700 mb-xs">Status</label>
            <select class="input-field" name="status" [(ngModel)]="status">
              <option value="Draft">Draft</option>
              <option value="Sent">Sent</option>
              <option value="Cancelled">Cancelled</option>
            </select>
            @if (editing && (editing.status === 'PartiallyPaid' || editing.status === 'Paid')) {
              <p class="text-xs text-neutral-500 mt-xs">
                Current status is {{ editing.status }} - set automatically from payments and can't be changed here.
              </p>
            }
          </div>

          @if (error()) {
            <p class="text-sm text-primary-500">{{ error() }}</p>
          }

          <div class="flex justify-end gap-sm pt-sm">
            <button type="button" class="btn-secondary" (click)="cancel.emit()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="loading() || !clientId || lineItems.length === 0">
              {{ loading() ? 'Saving…' : editing ? 'Save' : 'Create' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class InvoiceFormModalComponent implements OnInit {
  @Input() workspaceId = '';
  @Input() editing: Invoice | null = null;
  @Output() cancel = new EventEmitter<void>();
  @Output() saved = new EventEmitter<Invoice>();

  clientId: string | null = null;
  lineItems: LineItem[] = [{ description: '', quantity: 1, unitPrice: 0 }];
  taxRatePercent = 0;
  discountAmount = 0;
  dueDate = '';
  status: 'Draft' | 'Sent' | 'Cancelled' = 'Draft';
  loading = signal(false);
  error = signal('');

  constructor(private invoiceService: InvoiceService) {}

  ngOnInit(): void {
    if (this.editing) {
      this.clientId = this.editing.clientId;
      this.lineItems = this.editing.lineItems.length > 0 ? [...this.editing.lineItems] : [{ description: '', quantity: 1, unitPrice: 0 }];
      this.taxRatePercent = this.editing.taxRatePercent;
      this.discountAmount = this.editing.discountAmount;
      this.dueDate = this.editing.dueDate ? this.editing.dueDate.substring(0, 10) : '';
      this.status = this.editing.status === 'Draft' || this.editing.status === 'Sent' || this.editing.status === 'Cancelled' ? this.editing.status : 'Sent';
    }
  }

  submit(): void {
    if (!this.clientId || this.lineItems.length === 0) return;
    this.error.set('');
    this.loading.set(true);

    const request: InvoiceRequest = {
      clientId: this.clientId,
      lineItems: this.lineItems,
      taxRatePercent: this.taxRatePercent,
      discountAmount: this.discountAmount,
      dueDate: this.dueDate || undefined,
      status: this.status
    };

    const save$ = this.editing
      ? this.invoiceService.update(this.workspaceId, this.editing.invoiceId, request)
      : this.invoiceService.create(this.workspaceId, request);

    save$.subscribe({
      next: invoice => {
        this.loading.set(false);
        this.saved.emit(invoice);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Could not save invoice.');
      }
    });
  }
}
```

- [ ] **Step 3: Write the list page**

Status badge coloring makes overdue/paid state scannable at a glance without
reading every row - the single "needed feature" this task adds beyond a plain
table, per the "clear, easy to understand" brief.

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Invoice, InvoiceService, Payment } from '../../../core/billing.service';
import { CurrentWorkspaceService } from '../../../core/current-workspace.service';
import { BillingTabsComponent } from '../billing-tabs.component';
import { InvoiceFormModalComponent } from './invoice-form-modal/invoice-form-modal.component';
import { RecordPaymentModalComponent } from './record-payment-modal/record-payment-modal.component';

@Component({
  selector: 'app-invoice-list',
  standalone: true,
  imports: [CommonModule, BillingTabsComponent, InvoiceFormModalComponent, RecordPaymentModalComponent],
  template: `
    <div class="p-lg max-w-5xl mx-auto">
      <app-billing-tabs [workspaceId]="workspaceId" active="invoices" />

      <div class="flex items-center justify-between mb-lg">
        <h1 class="text-lg font-semibold text-neutral-900">Invoices</h1>
        <button class="btn-primary" (click)="openCreate()">New invoice</button>
      </div>

      @if (loading()) {
        <p class="text-sm text-neutral-500">Loading…</p>
      } @else if (error()) {
        <div class="card text-sm text-primary-500">
          {{ error() }}
          <button class="btn-secondary ml-md" (click)="fetch()">Retry</button>
        </div>
      } @else if (invoices().length === 0) {
        <div class="card text-center text-sm text-neutral-500">No invoices yet. Create one to get started.</div>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Number</th>
                <th class="text-left px-lg py-sm font-medium">Total</th>
                <th class="text-left px-lg py-sm font-medium">Balance</th>
                <th class="text-left px-lg py-sm font-medium">Status</th>
                <th class="text-left px-lg py-sm font-medium">Due date</th>
                <th class="px-lg py-sm"></th>
              </tr>
            </thead>
            <tbody>
              @for (invoice of invoices(); track invoice.invoiceId) {
                <tr class="border-t border-neutral-200 hover:bg-neutral-50">
                  <td class="px-lg py-sm text-neutral-900 cursor-pointer" (click)="openEdit(invoice)">{{ invoice.number }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ invoice.total | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ invoice.balance | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm">
                    <span class="text-xs px-sm py-xs rounded" [class]="statusClass(invoice)">
                      {{ invoice.isOverdue ? 'Overdue (' + invoice.daysOverdue + 'd)' : invoice.status }}
                    </span>
                  </td>
                  <td class="px-lg py-sm text-neutral-600">{{ invoice.dueDate ? (invoice.dueDate | date: 'mediumDate') : '—' }}</td>
                  <td class="px-lg py-sm text-right">
                    @if (invoice.balance > 0 && invoice.status !== 'Cancelled') {
                      <button class="text-xs text-primary-500 hover:text-primary-600" (click)="openPayment(invoice)">Record payment</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    @if (modalOpen()) {
      <app-invoice-form-modal [workspaceId]="workspaceId" [editing]="editingInvoice()" (cancel)="closeModal()" (saved)="onSaved()" />
    }
    @if (payingInvoice(); as invoice) {
      <app-record-payment-modal [workspaceId]="workspaceId" [invoice]="invoice" (cancel)="payingInvoice.set(null)" (recorded)="onPaymentRecorded()" />
    }
  `
})
export class InvoiceListComponent implements OnInit {
  workspaceId = '';
  invoices = signal<Invoice[]>([]);
  loading = signal(true);
  error = signal('');
  modalOpen = signal(false);
  editingInvoice = signal<Invoice | null>(null);
  payingInvoice = signal<Invoice | null>(null);

  constructor(private invoiceService: InvoiceService, private currentWorkspace: CurrentWorkspaceService) {}

  ngOnInit(): void {
    this.workspaceId = this.currentWorkspace.current()?.workspaceId ?? '';
    this.fetch();
  }

  fetch(): void {
    this.loading.set(true);
    this.error.set('');
    this.invoiceService.search(this.workspaceId).subscribe({
      next: invoices => {
        this.invoices.set(invoices);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load invoices.');
        this.loading.set(false);
      }
    });
  }

  statusClass(invoice: Invoice): string {
    if (invoice.isOverdue) return 'bg-primary-50 text-primary-600';
    if (invoice.status === 'Paid') return 'bg-green-50 text-green-700';
    if (invoice.status === 'PartiallyPaid') return 'bg-amber-50 text-amber-700';
    return 'bg-neutral-100 text-neutral-700';
  }

  openCreate(): void {
    this.editingInvoice.set(null);
    this.modalOpen.set(true);
  }

  openEdit(invoice: Invoice): void {
    this.editingInvoice.set(invoice);
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  onSaved(): void {
    this.modalOpen.set(false);
    this.fetch();
  }

  openPayment(invoice: Invoice): void {
    this.payingInvoice.set(invoice);
  }

  onPaymentRecorded(): void {
    this.payingInvoice.set(null);
    this.fetch();
  }
}
```

- [ ] **Step 4: Register the three billing routes**

In `ui/src/app/app.routes.ts`, add the imports:

```typescript
import { ClientListComponent } from './pages/billing/clients/client-list.component';
import { QuotationListComponent } from './pages/billing/quotations/quotation-list.component';
import { InvoiceListComponent } from './pages/billing/invoices/invoice-list.component';
```

Then add three routes inside the existing `workspace/:id` children array (the
same array that already holds `jobs`, `lands`, `members`, `roles` -
`ui/src/app/app.routes.ts:53-63`), directly after the `lands/:landId` entry:

```typescript
{ path: 'billing/clients', component: ClientListComponent },
{ path: 'billing/quotations', component: QuotationListComponent },
{ path: 'billing/invoices', component: InvoiceListComponent },
```

- [ ] **Step 5: Verify compilation and a dev build**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors.

Run: `cd ui && ng build`
Expected: build succeeds with no errors (warnings for bundle size are fine).

- [ ] **Step 6: Commit**

```bash
git add ui/src/app/pages/billing/invoices/ ui/src/app/app.routes.ts
git commit -m "feat(ui): add invoices list, create/edit modal, record-payment modal, and billing routes"
```

---

### Task 7: Print pages for invoice, quotation, and receipt

**Files:**
- Create: `ui/src/app/pages/billing/print/invoice-print.component.ts`
- Create: `ui/src/app/pages/billing/print/quotation-print.component.ts`
- Create: `ui/src/app/pages/billing/print/receipt-print.component.ts`
- Modify: `ui/src/app/app.routes.ts`

**Interfaces:**
- Consumes: `Invoice`, `Quotation`, `Payment`, `InvoiceService`, `QuotationService`
  (Task 1)

- [ ] **Step 1: Write the invoice print page**

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Invoice, InvoiceService } from '../../../core/billing.service';

@Component({
  selector: 'app-invoice-print',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (invoice(); as invoice) {
      <div class="max-w-2xl mx-auto p-lg">
        <div class="flex justify-between items-start mb-lg print:hidden">
          <h1 class="text-lg font-semibold">Invoice {{ invoice.number }}</h1>
          <button type="button" class="btn-primary" (click)="print()">Print / Save as PDF</button>
        </div>

        <h1 class="text-xl font-semibold text-neutral-900">Invoice {{ invoice.number }}</h1>
        <p class="text-sm text-neutral-600">
          Status: {{ invoice.status }}
          @if (invoice.dueDate) { · Due {{ invoice.dueDate | date: 'mediumDate' }} }
        </p>

        <table class="w-full text-sm mt-lg">
          <thead>
            <tr class="border-b border-neutral-200 text-left text-neutral-500">
              <th class="py-xs">Description</th>
              <th class="py-xs text-right">Qty</th>
              <th class="py-xs text-right">Unit price</th>
              <th class="py-xs text-right">Amount</th>
            </tr>
          </thead>
          <tbody>
            @for (item of invoice.lineItems; track $index) {
              <tr class="border-b border-neutral-100">
                <td class="py-xs">{{ item.description }}</td>
                <td class="py-xs text-right">{{ item.quantity }}</td>
                <td class="py-xs text-right">{{ item.unitPrice | number: '1.2-2' }}</td>
                <td class="py-xs text-right">{{ item.quantity * item.unitPrice | number: '1.2-2' }}</td>
              </tr>
            }
          </tbody>
        </table>

        <div class="mt-md flex flex-col items-end text-sm">
          <div class="flex justify-between w-56"><span>Subtotal</span><span>{{ invoice.subtotal | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56"><span>Discount</span><span>-{{ invoice.discountAmount | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56"><span>Tax ({{ invoice.taxRatePercent }}%)</span><span>{{ invoice.total - invoice.subtotal + invoice.discountAmount | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56 font-semibold text-neutral-900 border-t border-neutral-200 mt-xs pt-xs"><span>Total</span><span>{{ invoice.total | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56 text-neutral-600"><span>Paid</span><span>{{ invoice.amountPaid | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56 font-semibold"><span>Balance due</span><span>{{ invoice.balance | number: '1.2-2' }}</span></div>
        </div>
      </div>
    } @else if (error()) {
      <p class="p-lg text-sm text-primary-500">{{ error() }}</p>
    }
  `
})
export class InvoicePrintComponent implements OnInit {
  invoice = signal<Invoice | null>(null);
  loading = signal(true);
  error = signal('');

  constructor(private invoiceService: InvoiceService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    const workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    const invoiceId = this.route.snapshot.paramMap.get('invoiceId') ?? '';
    this.invoiceService.getById(workspaceId, invoiceId).subscribe({
      next: invoice => {
        this.invoice.set(invoice);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load invoice.');
        this.loading.set(false);
      }
    });
  }

  print(): void {
    window.print();
  }
}
```

- [ ] **Step 2: Write the quotation print page**

Same structure as Step 1, adapted for `Quotation`/`QuotationService` (no
paid/balance rows - a quotation isn't paid yet):

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Quotation, QuotationService } from '../../../core/billing.service';

@Component({
  selector: 'app-quotation-print',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (quotation(); as quotation) {
      <div class="max-w-2xl mx-auto p-lg">
        <div class="flex justify-between items-start mb-lg print:hidden">
          <h1 class="text-lg font-semibold">Quotation {{ quotation.number }}</h1>
          <button type="button" class="btn-primary" (click)="print()">Print / Save as PDF</button>
        </div>

        <h1 class="text-xl font-semibold text-neutral-900">Quotation {{ quotation.number }}</h1>
        <p class="text-sm text-neutral-600">
          Status: {{ quotation.status }}
          @if (quotation.validUntil) { · Valid until {{ quotation.validUntil | date: 'mediumDate' }} }
          @if (quotation.revisionNumber > 0) { · Revision {{ quotation.revisionNumber }} }
        </p>

        <table class="w-full text-sm mt-lg">
          <thead>
            <tr class="border-b border-neutral-200 text-left text-neutral-500">
              <th class="py-xs">Description</th>
              <th class="py-xs text-right">Qty</th>
              <th class="py-xs text-right">Unit price</th>
              <th class="py-xs text-right">Amount</th>
            </tr>
          </thead>
          <tbody>
            @for (item of quotation.lineItems; track $index) {
              <tr class="border-b border-neutral-100">
                <td class="py-xs">{{ item.description }}</td>
                <td class="py-xs text-right">{{ item.quantity }}</td>
                <td class="py-xs text-right">{{ item.unitPrice | number: '1.2-2' }}</td>
                <td class="py-xs text-right">{{ item.quantity * item.unitPrice | number: '1.2-2' }}</td>
              </tr>
            }
          </tbody>
        </table>

        <div class="mt-md flex flex-col items-end text-sm">
          <div class="flex justify-between w-56"><span>Subtotal</span><span>{{ quotation.subtotal | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56"><span>Tax ({{ quotation.taxRatePercent }}%)</span><span>{{ quotation.total - quotation.subtotal | number: '1.2-2' }}</span></div>
          <div class="flex justify-between w-56 font-semibold text-neutral-900 border-t border-neutral-200 mt-xs pt-xs"><span>Total</span><span>{{ quotation.total | number: '1.2-2' }}</span></div>
        </div>
      </div>
    } @else if (error()) {
      <p class="p-lg text-sm text-primary-500">{{ error() }}</p>
    }
  `
})
export class QuotationPrintComponent implements OnInit {
  quotation = signal<Quotation | null>(null);
  loading = signal(true);
  error = signal('');

  constructor(private quotationService: QuotationService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    const workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    const quotationId = this.route.snapshot.paramMap.get('quotationId') ?? '';
    this.quotationService.getById(workspaceId, quotationId).subscribe({
      next: quotation => {
        this.quotation.set(quotation);
        this.loading.set(false);
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load quotation.');
        this.loading.set(false);
      }
    });
  }

  print(): void {
    window.print();
  }
}
```

- [ ] **Step 3: Write the receipt print page**

A receipt is a `Payment`, not its own stored document (per the backend spec -
`ReceiptNumber` lives on `Payment`). Loads the parent invoice to get the payments
list, then finds the one matching the route's `paymentId`.

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Invoice, InvoiceService, Payment } from '../../../core/billing.service';

@Component({
  selector: 'app-receipt-print',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (loading()) {
      <p class="p-lg text-sm text-neutral-500">Loading…</p>
    } @else if (payment(); as payment) {
      <div class="max-w-md mx-auto p-lg">
        <div class="flex justify-between items-start mb-lg print:hidden">
          <h1 class="text-lg font-semibold">Receipt {{ payment.receiptNumber }}</h1>
          <button type="button" class="btn-primary" (click)="print()">Print / Save as PDF</button>
        </div>

        <h1 class="text-xl font-semibold text-neutral-900">Receipt {{ payment.receiptNumber }}</h1>
        <p class="text-sm text-neutral-600">For invoice {{ invoice()?.number }}</p>

        <div class="mt-lg space-y-sm text-sm">
          <div class="flex justify-between"><span class="text-neutral-500">Amount</span><span class="font-semibold">{{ payment.amount | number: '1.2-2' }}</span></div>
          <div class="flex justify-between"><span class="text-neutral-500">Method</span><span>{{ payment.method }}</span></div>
          <div class="flex justify-between"><span class="text-neutral-500">Received</span><span>{{ payment.receivedAt | date: 'mediumDate' }}</span></div>
          @if (payment.referenceNumber) {
            <div class="flex justify-between"><span class="text-neutral-500">Reference</span><span>{{ payment.referenceNumber }}</span></div>
          }
        </div>
      </div>
    } @else if (error()) {
      <p class="p-lg text-sm text-primary-500">{{ error() }}</p>
    }
  `
})
export class ReceiptPrintComponent implements OnInit {
  invoice = signal<Invoice | null>(null);
  payment = signal<Payment | null>(null);
  loading = signal(true);
  error = signal('');

  constructor(private invoiceService: InvoiceService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    const workspaceId = this.route.snapshot.paramMap.get('id') ?? '';
    const invoiceId = this.route.snapshot.paramMap.get('invoiceId') ?? '';
    const paymentId = this.route.snapshot.paramMap.get('paymentId') ?? '';

    this.invoiceService.getById(workspaceId, invoiceId).subscribe({
      next: invoice => {
        this.invoice.set(invoice);
        this.invoiceService.getPayments(workspaceId, invoiceId).subscribe({
          next: payments => {
            const match = payments.find(p => p.paymentId === paymentId) ?? null;
            this.payment.set(match);
            if (!match) this.error.set('Payment not found.');
            this.loading.set(false);
          },
          error: err => {
            this.error.set(err.error?.message ?? 'Could not load payment.');
            this.loading.set(false);
          }
        });
      },
      error: err => {
        this.error.set(err.error?.message ?? 'Could not load invoice.');
        this.loading.set(false);
      }
    });
  }

  print(): void {
    window.print();
  }
}
```

- [ ] **Step 4: Register the three print routes**

In `ui/src/app/app.routes.ts`, add the imports:

```typescript
import { InvoicePrintComponent } from './pages/billing/print/invoice-print.component';
import { QuotationPrintComponent } from './pages/billing/print/quotation-print.component';
import { ReceiptPrintComponent } from './pages/billing/print/receipt-print.component';
```

Add three routes as top-level siblings, directly after the existing
`lands/:landId/print` route (`ui/src/app/app.routes.ts:34`) - outside
`AppShellComponent`'s children, same reasoning as that route (chrome-free but
still authenticated):

```typescript
{ path: 'app/workspace/:id/billing/invoices/:invoiceId/print', component: InvoicePrintComponent, canActivate: [authGuard] },
{ path: 'app/workspace/:id/billing/quotations/:quotationId/print', component: QuotationPrintComponent, canActivate: [authGuard] },
{ path: 'app/workspace/:id/billing/invoices/:invoiceId/payments/:paymentId/print', component: ReceiptPrintComponent, canActivate: [authGuard] },
```

- [ ] **Step 5: Add "Print" links from the list pages**

In `ui/src/app/pages/billing/invoices/invoice-list.component.ts`, add a print
link next to the existing "Record payment" action in the actions cell:

```html
<a
  class="text-xs text-neutral-500 hover:text-neutral-700 mr-md"
  [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices', invoice.invoiceId, 'print']"
  (click)="$event.stopPropagation()"
>Print</a>
```

This requires adding `RouterLink` to that component's `imports` array. Do the
equivalent for `quotation-list.component.ts` (link to
`billing/quotations/:quotationId/print`).

- [ ] **Step 6: Verify compilation and build**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors.

Run: `cd ui && ng build`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add ui/src/app/pages/billing/print/ ui/src/app/app.routes.ts ui/src/app/pages/billing/invoices/invoice-list.component.ts ui/src/app/pages/billing/quotations/quotation-list.component.ts
git commit -m "feat(ui): add invoice/quotation/receipt print pages"
```

---

### Task 8: Job detail "Billing" tab

**Files:**
- Modify: `ui/src/app/pages/job/job-detail.component.ts`

**Interfaces:**
- Consumes: `Invoice`, `Quotation`, `InvoiceService`, `QuotationService` (Task 1)

- [ ] **Step 1: Read the current job-detail tab structure**

Before editing, read `ui/src/app/pages/job/job-detail.component.ts` in full to
find its existing tab-switching pattern (it already has multiple tabs - Documents,
Milestones, etc., per the file list in `ui/src/app/pages/job/`). Match that exact
pattern for the new "Billing" tab - same tab-button styling, same
`activeTab signal<string>` mechanism. Do not introduce a second tab-switching
convention.

- [ ] **Step 2: Add the Billing tab**

Add a new tab button alongside the existing ones, and a tab panel that:
1. Injects `InvoiceService` and `QuotationService` from `../../core/billing.service`.
2. On tab activation (or `ngOnInit`, matching however the existing tabs load their
   data), calls `invoiceService.search(workspaceId)` and
   `quotationService.search(workspaceId)`, then filters each result client-side:
   `invoices.filter(i => i.jobId === jobId)` / `quotations.filter(q => q.jobId === jobId)`.
3. Renders two small tables (reuse the same table markup style as
   `invoice-list.component.ts`/`quotation-list.component.ts`, condensed - number,
   total, status only, no actions).
4. Each row links to `/app/workspace/:id/billing/invoices/:invoiceId` or
   `/app/workspace/:id/billing/quotations/:quotationId` - but since Task 4-6 built
   modal-based edit (no dedicated per-item route), instead link to the list page
   with the row highlighted, or simply link to
   `['/app/workspace', workspaceId, 'billing', 'invoices']` and let the user find it
   in the full list. State this explicitly in the empty/row state: "View or edit
   from the Invoices tab."

Concretely, the tab panel template:

```html
@if (activeTab() === 'billing') {
  <div class="space-y-lg">
    <div>
      <h3 class="text-sm font-semibold text-neutral-900 mb-sm">Invoices</h3>
      @if (jobInvoices().length === 0) {
        <p class="text-sm text-neutral-500">No invoices linked to this job yet.</p>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Number</th>
                <th class="text-left px-lg py-sm font-medium">Total</th>
                <th class="text-left px-lg py-sm font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (invoice of jobInvoices(); track invoice.invoiceId) {
                <tr class="border-t border-neutral-200">
                  <td class="px-lg py-sm text-neutral-900">{{ invoice.number }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ invoice.total | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ invoice.status }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
      <a class="text-xs text-primary-500 hover:text-primary-600 mt-sm inline-block" [routerLink]="['/app/workspace', workspaceId, 'billing', 'invoices']">
        Manage invoices →
      </a>
    </div>

    <div>
      <h3 class="text-sm font-semibold text-neutral-900 mb-sm">Quotations</h3>
      @if (jobQuotations().length === 0) {
        <p class="text-sm text-neutral-500">No quotations linked to this job yet.</p>
      } @else {
        <div class="card p-0 overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="bg-neutral-100 text-neutral-600 text-xs uppercase">
              <tr>
                <th class="text-left px-lg py-sm font-medium">Number</th>
                <th class="text-left px-lg py-sm font-medium">Total</th>
                <th class="text-left px-lg py-sm font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (quotation of jobQuotations(); track quotation.quotationId) {
                <tr class="border-t border-neutral-200">
                  <td class="px-lg py-sm text-neutral-900">{{ quotation.number }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ quotation.total | number: '1.2-2' }}</td>
                  <td class="px-lg py-sm text-neutral-600">{{ quotation.status }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
      <a class="text-xs text-primary-500 hover:text-primary-600 mt-sm inline-block" [routerLink]="['/app/workspace', workspaceId, 'billing', 'quotations']">
        Manage quotations →
      </a>
    </div>
  </div>
}
```

And in the component class, add:

```typescript
jobInvoices = signal<Invoice[]>([]);
jobQuotations = signal<Quotation[]>([]);

private loadBilling(): void {
  this.invoiceService.search(this.workspaceId).subscribe({
    next: invoices => this.jobInvoices.set(invoices.filter(i => i.jobId === this.jobId)),
    error: () => this.jobInvoices.set([])
  });
  this.quotationService.search(this.workspaceId).subscribe({
    next: quotations => this.jobQuotations.set(quotations.filter(q => q.jobId === this.jobId)),
    error: () => this.jobQuotations.set([])
  });
}
```

Call `this.loadBilling()` wherever the existing tabs' equivalent load methods are
called from `ngOnInit` (match the existing pattern exactly - some tabs may lazy
load on first activation instead of eagerly; follow whichever the file already
does for its other tabs).

Add `InvoiceService` and `QuotationService` to the constructor injection list, and
`RouterLink` to `imports` if not already present.

- [ ] **Step 3: Verify compilation and build**

Run: `cd ui && npx tsc --noEmit -p tsconfig.app.json`
Expected: no errors.

Run: `cd ui && ng build`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add ui/src/app/pages/job/job-detail.component.ts
git commit -m "feat(ui): add Billing tab to job detail page"
```

---

## Self-Review Notes

**Spec coverage:** navigation/routes (Task 3, 6, 7), client CRUD (Task 4),
quotation CRUD + convert (Task 5), invoice CRUD + payments (Task 6), print pages
(Task 7), job-tab integration (Task 8), error handling (every form, per-task) -
all covered. Client balance/payment-history display (spec's "read-only sections
on Client detail") is intentionally deferred: since Task 4 collapsed Client's
detail view into the create/edit modal (no persistent detail route to host a
read-only section), the balance is instead surfaced in the Clients list table
directly - simpler and still visible everywhere a client is listed. This is a
minor, reasoned deviation from the spec's literal wording, consistent with the
plan's stated modal-first simplification.

**Correction applied during planning:** the UI spec said "Material table" -
verified against the actual codebase (`land-list.component.ts`) and corrected to
plain Tailwind tables throughout, since Angular Material isn't used anywhere in
this project.
