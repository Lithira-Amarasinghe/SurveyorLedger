import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface FinancialSummary {
  totalInvoiced: number;
  totalPaid: number;
  totalOutstanding: number;
  totalExpenses: number;
  grossProfit: number;
  profitMarginPercent: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface PaymentHistoryRow {
  paymentId: string;
  receivedAt: string;
  jobId: string;
  jobNumber: string;
  jobTitle: string;
  invoiceNumber: string;
  clientName: string;
  amount: number;
  method: string;
}

export interface ExpenseHistoryRow {
  expenseId: string;
  incurredDate: string;
  jobId: string;
  jobNumber: string;
  jobTitle: string;
  category: string;
  payeeName: string | null;
  amount: number;
}

export interface OutstandingInvoiceRow {
  invoiceId: string;
  jobId: string;
  jobNumber: string;
  jobTitle: string;
  invoiceNumber: string;
  clientName: string;
  total: number;
  amountPaid: number;
  balance: number;
  dueDate: string | null;
  isOverdue: boolean;
  daysOverdue: number;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class ReportService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/reports`;
  }

  private dateParams(from?: string, to?: string): HttpParams {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return params;
  }

  getSummary(workspaceId: string, from?: string, to?: string): Observable<FinancialSummary> {
    return this.http
      .get<ApiResponse<FinancialSummary>>(`${this.base(workspaceId)}/summary`, { params: this.dateParams(from, to) })
      .pipe(map(res => res.data));
  }

  getPayments(workspaceId: string, from?: string, to?: string, page = 1, pageSize = 50): Observable<PagedResult<PaymentHistoryRow>> {
    const params = this.dateParams(from, to).set('page', page).set('pageSize', pageSize);
    return this.http
      .get<ApiResponse<PagedResult<PaymentHistoryRow>>>(`${this.base(workspaceId)}/payments`, { params })
      .pipe(map(res => res.data));
  }

  getExpenses(workspaceId: string, from?: string, to?: string, page = 1, pageSize = 50): Observable<PagedResult<ExpenseHistoryRow>> {
    const params = this.dateParams(from, to).set('page', page).set('pageSize', pageSize);
    return this.http
      .get<ApiResponse<PagedResult<ExpenseHistoryRow>>>(`${this.base(workspaceId)}/expenses`, { params })
      .pipe(map(res => res.data));
  }

  getOutstandingInvoices(workspaceId: string): Observable<OutstandingInvoiceRow[]> {
    return this.http
      .get<ApiResponse<OutstandingInvoiceRow[]>>(`${this.base(workspaceId)}/outstanding-invoices`)
      .pipe(map(res => res.data));
  }
}
