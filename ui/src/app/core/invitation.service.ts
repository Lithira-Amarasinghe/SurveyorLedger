import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Invitation {
  invitationId: string;
  email: string;
  role: string;
  expiresAt: string;
  invitedByName: string;
  createdAt: string;
  emailFailed: boolean;
}

export interface InvitationPreview {
  email: string;
  workspaceName: string;
  role: string;
  expired: boolean;
}

export interface AcceptInvitationResult {
  workspaceId: string;
  role: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
  errors?: Record<string, string[]>;
}

@Injectable({ providedIn: 'root' })
export class InvitationService {
  private apiUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  create(workspaceId: string, email: string, role: string): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/workspace/${workspaceId}/invitations`, { email, role })
      .pipe(map(() => undefined));
  }

  list(workspaceId: string): Observable<Invitation[]> {
    return this.http.get<ApiResponse<Invitation[]>>(`${this.apiUrl}/workspace/${workspaceId}/invitations`)
      .pipe(map(res => res.data));
  }

  revoke(workspaceId: string, invitationId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/workspace/${workspaceId}/invitations/${invitationId}`);
  }

  resend(workspaceId: string, invitationId: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/workspace/${workspaceId}/invitations/${invitationId}/resend`, {});
  }

  getByToken(token: string): Observable<InvitationPreview> {
    return this.http.get<ApiResponse<InvitationPreview>>(`${this.apiUrl}/invitations/${token}`)
      .pipe(map(res => res.data));
  }

  accept(token: string): Observable<AcceptInvitationResult> {
    return this.http.post<ApiResponse<AcceptInvitationResult>>(`${this.apiUrl}/invitations/${token}/accept`, {})
      .pipe(map(res => res.data));
  }
}
