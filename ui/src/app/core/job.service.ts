import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Land } from './land.service';
import { AddressInput } from './person.service';

export interface Job {
  jobId: string;
  jobNumber: string;
  title: string;
  description: string | null;
  status: string;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface JobParticipant {
  userId: string;
  firstName: string;
  lastName: string;
  email: string | null;
  role: string;
  assignedAt: string;
}

export interface JobInvitation {
  invitationId: string;
  email: string;
  role: string;
  expiresAt: string;
  status: string;
}

/** Exactly one of participant/invitation is set - "invited" means nothing granted yet, pending acceptance. */
export interface AddParticipantResult {
  status: 'added' | 'invited';
  participant?: JobParticipant;
  invitation?: JobInvitation;
}

export interface AccessibleJob {
  jobId: string;
  jobNumber: string;
  title: string;
  status: string;
  workspaceId: string;
  workspaceName: string;
  accessScopeType: string;
}

export interface JobWithWorkspace extends Job {
  workspaceId: string;
  workspaceName: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class JobService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/job`;
  }

  list(workspaceId: string): Observable<Job[]> {
    return this.http.get<ApiResponse<Job[]>>(this.base(workspaceId)).pipe(map(res => res.data));
  }

  create(workspaceId: string, title: string): Observable<Job> {
    return this.http.post<ApiResponse<Job>>(this.base(workspaceId), { title }).pipe(map(res => res.data));
  }

  getById(workspaceId: string, jobId: string): Observable<Job> {
    return this.http.get<ApiResponse<Job>>(`${this.base(workspaceId)}/${jobId}`).pipe(map(res => res.data));
  }

  update(workspaceId: string, jobId: string, request: { title: string; description: string | null }): Observable<Job> {
    return this.http.put<ApiResponse<Job>>(`${this.base(workspaceId)}/${jobId}`, request).pipe(map(res => res.data));
  }

  updateStatus(workspaceId: string, jobId: string, status: string): Observable<Job> {
    return this.http.put<ApiResponse<Job>>(`${this.base(workspaceId)}/${jobId}/status`, { status }).pipe(map(res => res.data));
  }

  getParticipants(workspaceId: string, jobId: string): Observable<JobParticipant[]> {
    return this.http.get<ApiResponse<JobParticipant[]>>(`${this.base(workspaceId)}/${jobId}/participants`).pipe(map(res => res.data));
  }

  /**
   * role is the job-scoped grant to create - "Surveyor" or "Client", independent of the
   * person's workspace role. Instant if the target already has consent coverage for this
   * job; otherwise the API creates an invite instead - check the returned status.
   */
  addParticipant(workspaceId: string, jobId: string, userId: string, role: string): Observable<AddParticipantResult> {
    return this.http
      .post<ApiResponse<AddParticipantResult>>(`${this.base(workspaceId)}/${jobId}/participants/${userId}`, { role })
      .pipe(map(res => res.data));
  }

  /** For someone typed by email in the "not found" fallback - always creates an invite. */
  inviteParticipant(
    workspaceId: string, jobId: string, role: string, email: string,
    firstName?: string, lastName?: string, phone?: string, address?: AddressInput
  ): Observable<AddParticipantResult> {
    return this.http
      .post<ApiResponse<AddParticipantResult>>(`${this.base(workspaceId)}/${jobId}/participants/invite`, {
        role, email, firstName, lastName, phone, address
      })
      .pipe(map(res => res.data));
  }

  removeParticipant(workspaceId: string, jobId: string, userId: string, role: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${jobId}/participants/${userId}/roles/${role}`);
  }

  getLands(workspaceId: string, jobId: string): Observable<Land[]> {
    return this.http.get<ApiResponse<Land[]>>(`${this.base(workspaceId)}/${jobId}/lands`).pipe(map(res => res.data));
  }

  addLand(workspaceId: string, jobId: string, landId: string): Observable<void> {
    return this.http.post<void>(`${this.base(workspaceId)}/${jobId}/lands/${landId}`, {});
  }

  removeLand(workspaceId: string, jobId: string, landId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${jobId}/lands/${landId}`);
  }

  /** Every job this user can open, across every workspace - backs the dashboard's Jobs section. */
  getMine(): Observable<AccessibleJob[]> {
    return this.http
      .get<ApiResponse<AccessibleJob[]>>(`${environment.apiBaseUrl}/jobs/mine`)
      .pipe(map(res => res.data));
  }

  /** Fetch a single job with no workspace prefix - for a caller who may not be a workspace member. */
  getStandalone(jobId: string): Observable<JobWithWorkspace> {
    return this.http
      .get<ApiResponse<JobWithWorkspace>>(`${environment.apiBaseUrl}/jobs/${jobId}`)
      .pipe(map(res => res.data));
  }
}
