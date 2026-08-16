import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { AddressInput } from './person.service';

export interface Invitation {
  invitationId: string;
  email: string;
  role: string;
  status: 'Pending' | 'Declined' | 'Expired' | 'Revoked';
  expiresAt: string;
  invitedByName: string;
  createdAt: string;
  emailFailed: boolean;
}

export interface MyInvitation {
  invitationId: string;
  workspaceName: string;
  role: string;
  status: 'Pending' | 'Accepted' | 'Declined' | 'Expired' | 'Revoked';
  expiresAt: string;
  createdAt: string;
  hasLogin: boolean;
  /** Set only for a job-scoped invite - joining one job, not the whole workspace. */
  jobLabel?: string;
}

export interface InvitationPreview {
  invitationId: string;
  email: string;
  workspaceName: string;
  role: string;
  expired: boolean;
  hasLogin: boolean;
  /** Set only for a job-scoped invite - joining one job, not the whole workspace. */
  jobLabel?: string;
}

export interface AcceptInvitationResult {
  workspaceId: string;
  role: string;
  /** Set only for a job-scoped invite - route to the job page, not the workspace overview. */
  jobId?: string;
}

export interface AddMemberRequest {
  email: string;
  role: string;
  firstName?: string;
  lastName?: string;
  phone?: string;
  address?: AddressInput;
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

  /** The single "add a person to this workspace" call - new or existing account, nothing granted until they accept. */
  create(workspaceId: string, request: AddMemberRequest): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/workspace/${workspaceId}/invitations`, request)
      .pipe(map(() => undefined));
  }

  list(workspaceId: string): Observable<Invitation[]> {
    return this.http.get<ApiResponse<Invitation[]>>(`${this.apiUrl}/workspace/${workspaceId}/invitations`)
      .pipe(map(res => res.data));
  }

  /** Every invitation for the logged-in user, across every workspace. */
  mine(): Observable<MyInvitation[]> {
    return this.http.get<ApiResponse<MyInvitation[]>>(`${this.apiUrl}/invitations/mine`)
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

  /** Accept as an already-logged-in account. */
  accept(invitationId: string): Observable<AcceptInvitationResult> {
    return this.http.post<ApiResponse<AcceptInvitationResult>>(`${this.apiUrl}/invitations/${invitationId}/accept`, {})
      .pipe(map(res => res.data));
  }

  decline(invitationId: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/invitations/${invitationId}/decline`, {});
  }

  /** Decline before ever logging in - the only option when the account has no password yet. */
  declineByToken(token: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/invitations/${token}/decline-by-token`, {});
  }

  /** Sets a password for an account that doesn't have one yet, then accepts. No auth token exists yet, so this is reached by token. */
  completeInvitation(token: string, password: string, confirmPassword: string, firstName: string, lastName: string): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/invitations/${token}/complete`, {
      password,
      confirmPassword,
      firstName,
      lastName,
    }).pipe(map(() => undefined));
  }
}
