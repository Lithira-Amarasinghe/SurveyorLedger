import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Milestone {
  milestoneId: string;
  jobId: string;
  title: string;
  description: string | null;
  dueDate: string | null;
  status: string;
  sortOrder: number;
  completedAt: string | null;
  completedBy: string | null;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class MilestoneService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string, jobId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/milestone`;
  }

  list(workspaceId: string, jobId: string): Observable<Milestone[]> {
    return this.http.get<ApiResponse<Milestone[]>>(this.base(workspaceId, jobId)).pipe(map(res => res.data));
  }

  create(workspaceId: string, jobId: string, request: { title: string; description?: string | null; dueDate?: string | null }): Observable<Milestone> {
    return this.http.post<ApiResponse<Milestone>>(this.base(workspaceId, jobId), request).pipe(map(res => res.data));
  }

  updateStatus(workspaceId: string, jobId: string, milestoneId: string, status: string): Observable<Milestone> {
    return this.http
      .put<ApiResponse<Milestone>>(`${this.base(workspaceId, jobId)}/${milestoneId}/status`, { status })
      .pipe(map(res => res.data));
  }

  delete(workspaceId: string, jobId: string, milestoneId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${milestoneId}`);
  }

  reorder(workspaceId: string, jobId: string, milestoneIds: string[]): Observable<Milestone[]> {
    return this.http
      .put<ApiResponse<Milestone[]>>(`${this.base(workspaceId, jobId)}/reorder`, { milestoneIds })
      .pipe(map(res => res.data));
  }
}
