import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Document {
  documentId: string;
  jobId: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  category: 'SurveyPlan' | 'LegalDocument' | 'Photo' | 'Other';
  visibility: 'Internal' | 'ClientVisible';
  uploadedBy: string;
  uploadedByName: string;
  createdAt: string;
  updatedAt: string;
  uploadBatchId: string | null;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class DocumentService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string, jobId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/document`;
  }

  list(workspaceId: string, jobId: string): Observable<Document[]> {
    return this.http.get<ApiResponse<Document[]>>(this.base(workspaceId, jobId)).pipe(map(res => res.data));
  }

  upload(workspaceId: string, jobId: string, file: File, category: string, visibility: string, displayFileName?: string, batchId?: string): Observable<Document> {
    const form = new FormData();
    form.append('File', file);
    form.append('Category', category);
    form.append('Visibility', visibility);
    if (displayFileName) form.append('DisplayFileName', displayFileName);
    if (batchId) form.append('BatchId', batchId);
    return this.http.post<ApiResponse<Document>>(this.base(workspaceId, jobId), form).pipe(map(res => res.data));
  }

  /**
   * Fetches the file as a Blob - both preview and download go through this one method.
   * A plain <a href> to this endpoint would 401: the JWT rides an Authorization header
   * the jwtInterceptor attaches only to HttpClient requests, not bare navigation.
   */
  getFileBlob(workspaceId: string, jobId: string, documentId: string): Observable<Blob> {
    return this.http.get(`${this.base(workspaceId, jobId)}/${documentId}`, { responseType: 'blob' });
  }

  delete(workspaceId: string, jobId: string, documentId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId, jobId)}/${documentId}`);
  }

  updateVisibility(workspaceId: string, jobId: string, documentId: string, visibility: string): Observable<Document> {
    return this.http
      .patch<ApiResponse<Document>>(`${this.base(workspaceId, jobId)}/${documentId}/visibility`, { visibility })
      .pipe(map(res => res.data));
  }

  rename(workspaceId: string, jobId: string, documentId: string, fileName: string): Observable<Document> {
    return this.http
      .patch<ApiResponse<Document>>(`${this.base(workspaceId, jobId)}/${documentId}`, { fileName })
      .pipe(map(res => res.data));
  }
}
