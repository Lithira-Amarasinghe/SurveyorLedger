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
