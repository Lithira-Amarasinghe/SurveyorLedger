import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export const EXPENSE_CATEGORIES = ['StaffCost', 'Subcontractor', 'Equipment', 'Material', 'Transport', 'Other'] as const;
export type ExpenseCategory = (typeof EXPENSE_CATEGORIES)[number];

export const PAYEE_TYPES = ['Salary', 'Commission', 'Bonus', 'ProfitShare'] as const;
export type PayeeType = (typeof PAYEE_TYPES)[number];

export interface Expense {
  expenseId: string;
  jobId: string;
  category: ExpenseCategory;
  amount: number;
  description: string | null;
  incurredDate: string;
  hasReceipt: boolean;
  payeeId: string | null;
  payeeName: string | null;
  payeeType: PayeeType | null;
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
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class ExpenseService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string, jobId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/expense`;
  }

  getAll(workspaceId: string, jobId: string): Observable<Expense[]> {
    return this.http.get<ApiResponse<Expense[]>>(this.base(workspaceId, jobId)).pipe(map(res => res.data));
  }

  create(workspaceId: string, jobId: string, request: ExpenseRequest): Observable<Expense> {
    return this.http.post<ApiResponse<Expense>>(this.base(workspaceId, jobId), request).pipe(map(res => res.data));
  }

  update(workspaceId: string, jobId: string, expenseId: string, request: ExpenseRequest): Observable<Expense> {
    return this.http.put<ApiResponse<Expense>>(`${this.base(workspaceId, jobId)}/${expenseId}`, request).pipe(map(res => res.data));
  }

  delete(workspaceId: string, jobId: string, expenseId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${expenseId}`);
  }

  uploadReceipt(workspaceId: string, jobId: string, expenseId: string, file: File): Observable<Expense> {
    const form = new FormData();
    form.append('file', file);
    return this.http
      .post<ApiResponse<Expense>>(`${this.base(workspaceId, jobId)}/${expenseId}/receipt`, form)
      .pipe(map(res => res.data));
  }

  receiptUrl(workspaceId: string, jobId: string, expenseId: string): string {
    return `${this.base(workspaceId, jobId)}/${expenseId}/receipt`;
  }
}
