import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

/** Land counterpart to DocumentRequest - role-only targeting, no per-person targeting (Land has no per-record participant list like Job's assignments). */
export interface LandDocumentRequest {
  requestId: string;
  landId: string;
  ownerType: 'Land' | 'LandSurvey' | 'LandDeed' | 'LandPhoto';
  ownerId: string;
  title: string;
  description: string | null;
  category: 'SurveyPlan' | 'LegalDocument' | 'Photo' | 'Other';
  targetRole: 'Admin' | 'Surveyor' | 'Client' | null;
  hasActiveShareLink: boolean;
  status: 'Pending' | 'Fulfilled' | 'Reopened';
  fulfilledBatchId: string | null;
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
export class LandDocumentRequestService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string, landId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/land/${landId}/document-request`;
  }

  list(workspaceId: string, landId: string): Observable<LandDocumentRequest[]> {
    return this.http.get<ApiResponse<LandDocumentRequest[]>>(this.base(workspaceId, landId)).pipe(map(res => res.data));
  }

  create(
    workspaceId: string, landId: string, title: string, description: string | null, category: string, targetRole: string | null,
    ownerType: 'Land' | 'LandSurvey' | 'LandDeed' | 'LandPhoto' = 'Land', ownerId?: string
  ): Observable<LandDocumentRequest> {
    return this.http
      .post<ApiResponse<LandDocumentRequest>>(this.base(workspaceId, landId), { title, description, category, targetRole, ownerType, ownerId })
      .pipe(map(res => res.data));
  }

  fulfill(workspaceId: string, landId: string, requestId: string, files: File[], batchId: string, displayFileName?: string): Observable<LandDocumentRequest> {
    const form = new FormData();
    files.forEach(file => form.append('Files', file));
    form.append('BatchId', batchId);
    if (displayFileName) form.append('DisplayFileName', displayFileName);
    return this.http
      .post<ApiResponse<LandDocumentRequest>>(`${this.base(workspaceId, landId)}/${requestId}/fulfill`, form)
      .pipe(map(res => res.data));
  }

  reopen(workspaceId: string, landId: string, requestId: string, note?: string | null): Observable<LandDocumentRequest> {
    return this.http
      .post<ApiResponse<LandDocumentRequest>>(`${this.base(workspaceId, landId)}/${requestId}/reopen`, { note: note ?? null })
      .pipe(map(res => res.data));
  }

  cancel(workspaceId: string, landId: string, requestId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, landId)}/${requestId}`);
  }

  updateTarget(workspaceId: string, landId: string, requestId: string, targetRole: string | null): Observable<LandDocumentRequest> {
    return this.http
      .patch<ApiResponse<LandDocumentRequest>>(`${this.base(workspaceId, landId)}/${requestId}/target`, { targetRole })
      .pipe(map(res => res.data));
  }

  generateShareLink(workspaceId: string, landId: string, requestId: string): Observable<{ token: string; expiresAt: string }> {
    return this.http
      .post<ApiResponse<{ token: string; expiresAt: string }>>(`${this.base(workspaceId, landId)}/${requestId}/share-link`, {})
      .pipe(map(res => res.data));
  }

  revokeShareLink(workspaceId: string, landId: string, requestId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, landId)}/${requestId}/share-link`);
  }
}
