import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Land } from './land.service';

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
  id: string;
  userId: string;
  firstName: string;
  lastName: string;
  email: string | null;
  participantType: string;
  addedAt: string;
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

  addParticipant(workspaceId: string, jobId: string, userId: string, participantType: string): Observable<JobParticipant> {
    return this.http
      .post<ApiResponse<JobParticipant>>(`${this.base(workspaceId)}/${jobId}/participants/${userId}`, { participantType })
      .pipe(map(res => res.data));
  }

  removeParticipant(workspaceId: string, jobId: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${jobId}/participants/${userId}`);
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
}
