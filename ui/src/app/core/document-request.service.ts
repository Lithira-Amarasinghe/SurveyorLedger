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
  status: 'Pending' | 'Fulfilled';
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

  create(workspaceId: string, jobId: string, title: string, description: string | null, category: string): Observable<DocumentRequest> {
    return this.http
      .post<ApiResponse<DocumentRequest>>(this.base(workspaceId, jobId), { title, description, category })
      .pipe(map(res => res.data));
  }

  fulfill(workspaceId: string, jobId: string, requestId: string, file: File, visibility: string): Observable<DocumentRequest> {
    const form = new FormData();
    form.append('File', file);
    form.append('Visibility', visibility);
    return this.http
      .post<ApiResponse<DocumentRequest>>(`${this.base(workspaceId, jobId)}/${requestId}/fulfill`, form)
      .pipe(map(res => res.data));
  }

  reopen(workspaceId: string, jobId: string, requestId: string): Observable<DocumentRequest> {
    return this.http
      .post<ApiResponse<DocumentRequest>>(`${this.base(workspaceId, jobId)}/${requestId}/reopen`, {})
      .pipe(map(res => res.data));
  }

  cancel(workspaceId: string, jobId: string, requestId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${requestId}`);
  }
}
