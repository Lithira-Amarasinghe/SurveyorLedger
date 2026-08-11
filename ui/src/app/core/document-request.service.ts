import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface DocumentRequest {
  requestId: string;
  jobId: string;
  title: string;
  description: string | null;
  category: 'SurveyPlan' | 'LegalDocument' | 'Photo' | 'Other';
  targetRole: 'Admin' | 'Surveyor' | 'Client' | null;
  targetUserId: string | null;
  targetUserName: string | null;
  status: 'Pending' | 'Fulfilled' | 'Reopened';
  fulfilledDocumentId: string | null;
  fulfilledAt: string | null;
  fulfilledBy: string | null;
  requestedBy: string;
  createdAt: string;
  updatedAt: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class DocumentRequestService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string, jobId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/document-request`;
  }

  list(workspaceId: string, jobId: string): Observable<DocumentRequest[]> {
    return this.http.get<ApiResponse<DocumentRequest[]>>(this.base(workspaceId, jobId)).pipe(map(res => res.data));
  }

  create(workspaceId: string, jobId: string, title: string, description: string | null, category: string, targetRole: string | null, targetUserId: string | null): Observable<DocumentRequest> {
    return this.http
      .post<ApiResponse<DocumentRequest>>(this.base(workspaceId, jobId), { title, description, category, targetRole, targetUserId })
      .pipe(map(res => res.data));
  }

  fulfill(workspaceId: string, jobId: string, requestId: string, file: File, visibility: string, displayFileName?: string): Observable<DocumentRequest> {
    const form = new FormData();
    form.append('File', file);
    form.append('Visibility', visibility);
    if (displayFileName) form.append('DisplayFileName', displayFileName);
    return this.http
      .post<ApiResponse<DocumentRequest>>(`${this.base(workspaceId, jobId)}/${requestId}/fulfill`, form)
      .pipe(map(res => res.data));
  }

  reopen(workspaceId: string, jobId: string, requestId: string, note?: string | null): Observable<DocumentRequest> {
    return this.http
      .post<ApiResponse<DocumentRequest>>(`${this.base(workspaceId, jobId)}/${requestId}/reopen`, { note: note ?? null })
      .pipe(map(res => res.data));
  }

  cancel(workspaceId: string, jobId: string, requestId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${requestId}`);
  }

  updateTarget(workspaceId: string, jobId: string, requestId: string, targetRole: string | null, targetUserId: string | null): Observable<DocumentRequest> {
    return this.http
      .patch<ApiResponse<DocumentRequest>>(`${this.base(workspaceId, jobId)}/${requestId}/target`, { targetRole, targetUserId })
      .pipe(map(res => res.data));
  }
}
