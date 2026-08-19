import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface LandDocumentRequestLinkPreview {
  title: string | null;
  description: string | null;
  category: 'SurveyPlan' | 'LegalDocument' | 'Photo' | 'Other' | null;
  workspaceName: string | null;
  landAddressLine: string | null;
  expired: boolean;
  alreadyFulfilled: boolean;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

/**
 * Deliberately separate from LandDocumentRequestService: this service never has a
 * workspace or land id to send, and never attaches an auth header - it structurally
 * can't, since the token is the only thing identifying what's being uploaded to.
 */
@Injectable({ providedIn: 'root' })
export class LandDocumentRequestLinkService {
  constructor(private http: HttpClient) {}

  private base(token: string): string {
    return `${environment.apiBaseUrl}/land-document-request-links/${token}`;
  }

  getPreview(token: string): Observable<LandDocumentRequestLinkPreview> {
    return this.http.get<ApiResponse<LandDocumentRequestLinkPreview>>(this.base(token)).pipe(map(res => res.data));
  }

  upload(token: string, file: File, displayFileName?: string): Observable<void> {
    const form = new FormData();
    form.append('File', file);
    if (displayFileName) form.append('DisplayFileName', displayFileName);
    return this.http.post<void>(`${this.base(token)}/upload`, form);
  }
}
