import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface JobBudget {
  jobId: string;
  estimatedFee: number;
  estimatedCost: number;
  expectedProfit: number;
  updatedByName: string;
  updatedAt: string;
}

export interface JobBudgetRequest {
  estimatedFee: number;
  estimatedCost: number;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class JobBudgetService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string, jobId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/budget`;
  }

  get(workspaceId: string, jobId: string): Observable<JobBudget | null> {
    return this.http.get<ApiResponse<JobBudget | null>>(this.base(workspaceId, jobId)).pipe(map(res => res.data));
  }

  upsert(workspaceId: string, jobId: string, request: JobBudgetRequest): Observable<JobBudget> {
    return this.http.put<ApiResponse<JobBudget>>(this.base(workspaceId, jobId), request).pipe(map(res => res.data));
  }

  delete(workspaceId: string, jobId: string): Observable<void> {
    return this.http.delete<void>(this.base(workspaceId, jobId));
  }
}
