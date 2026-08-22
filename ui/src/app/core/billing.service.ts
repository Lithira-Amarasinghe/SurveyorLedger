import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

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

export type QuotationStatus = 'Draft' | 'Sent' | 'Accepted' | 'Rejected' | 'Expired';

export interface Quotation {
  quotationId: string;
  jobId: string | null;
  number: string;
  lineItems: LineItem[];
  taxRatePercent: number;
  subtotal: number;
  total: number;
  status: QuotationStatus;
  validUntil: string | null;
  revisionNumber: number;
  invoicedAmount: number;
  remainingAmount: number;
  createdAt: string;
  updatedAt: string;
}

export interface QuotationRequest {
  jobId: string | null;
  lineItems: LineItem[];
  taxRatePercent: number;
  validUntil?: string;
  status?: QuotationStatus;
}

export type InvoiceStatus = 'Draft' | 'Sent' | 'PartiallyPaid' | 'Paid' | 'Overdue' | 'Cancelled';
export type PaymentMethod = 'Cash' | 'BankTransfer' | 'Cheque';

export interface Invoice {
  invoiceId: string;
  jobId: string | null;
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
  installments: Installment[];
}

export interface InvoiceRequest {
  jobId: string | null;
  lineItems: LineItem[];
  taxRatePercent: number;
  discountAmount: number;
  dueDate?: string;
  status?: 'Draft' | 'Sent' | 'Cancelled';
  installments: Installment[];
}

export type InstallmentStatus = 'Paid' | 'Overdue' | 'Pending';

export interface Installment {
  amount: number;
  dueDate: string;
  status?: InstallmentStatus;
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
  recordedByName: string | null;
  isRefund: boolean;
  isVoided: boolean;
  voidedAt: string | null;
  voidReason: string | null;
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
export class QuotationService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/quotations`;
  }

  search(workspaceId: string, jobId?: string): Observable<Quotation[]> {
    let params = new HttpParams();
    if (jobId) params = params.set('jobId', jobId);
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

  send(workspaceId: string, quotationId: string, recipientPersonIds: string[]): Observable<void> {
    return this.http.post<void>(`${this.base(workspaceId)}/${quotationId}/send`, { recipientPersonIds });
  }
}

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/invoices`;
  }

  search(workspaceId: string, jobId?: string): Observable<Invoice[]> {
    let params = new HttpParams();
    if (jobId) params = params.set('jobId', jobId);
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

  voidPayment(workspaceId: string, invoiceId: string, paymentId: string, reason?: string): Observable<Payment> {
    return this.http
      .post<ApiResponse<Payment>>(`${this.base(workspaceId)}/${invoiceId}/payments/${paymentId}/void`, { reason })
      .pipe(map(res => res.data));
  }

  recordRefund(workspaceId: string, invoiceId: string, request: PaymentRequest, proofFile?: File): Observable<Payment> {
    const form = new FormData();
    form.append('amount', String(request.amount));
    form.append('method', request.method);
    form.append('receivedAt', request.receivedAt);
    if (request.referenceNumber) form.append('referenceNumber', request.referenceNumber);
    if (proofFile) form.append('proofFile', proofFile);
    return this.http.post<ApiResponse<Payment>>(`${this.base(workspaceId)}/${invoiceId}/refunds`, form).pipe(map(res => res.data));
  }

  paymentProofUrl(workspaceId: string, invoiceId: string, paymentId: string): string {
    return `${this.base(workspaceId)}/${invoiceId}/payments/${paymentId}/proof`;
  }

  getPaymentProofBlob(workspaceId: string, invoiceId: string, paymentId: string): Observable<Blob> {
    return this.http.get(this.paymentProofUrl(workspaceId, invoiceId, paymentId), { responseType: 'blob' });
  }

  send(workspaceId: string, invoiceId: string, recipientPersonIds: string[]): Observable<void> {
    return this.http.post<void>(`${this.base(workspaceId)}/${invoiceId}/send`, { recipientPersonIds });
  }
}
