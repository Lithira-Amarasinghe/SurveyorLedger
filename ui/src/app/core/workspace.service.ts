import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Workspace {
  workspaceId: string;
  name: string;
  description: string;
  createdAt: string;
  isActive: boolean;
  tier: string;
  role: string;
}

export interface Member {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  role: string;
  assignedAt: string;
  isOwner: boolean;
}

export interface Permission {
  name: string;
  resource: string;
  action: string;
  description: string;
}

export interface Role {
  id: string;
  name: string;
  description: string | null;
  permissions: Permission[];
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  private apiUrl = `${environment.apiBaseUrl}/workspace`;

  constructor(private http: HttpClient) {}

  list(): Observable<Workspace[]> {
    return this.http.get<ApiResponse<Workspace[]>>(this.apiUrl).pipe(map(res => res.data));
  }

  create(name: string, description: string, tier: string): Observable<Workspace> {
    return this.http.post<ApiResponse<Workspace>>(this.apiUrl, { name, description, tier }).pipe(map(res => res.data));
  }

  getById(id: string): Observable<Workspace> {
    return this.http.get<ApiResponse<Workspace>>(`${this.apiUrl}/${id}`).pipe(map(res => res.data));
  }

  getMembers(workspaceId: string): Observable<Member[]> {
    return this.http.get<ApiResponse<Member[]>>(`${this.apiUrl}/${workspaceId}/members`).pipe(map(res => res.data));
  }

  updateMemberRole(workspaceId: string, userId: string, role: string): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${workspaceId}/members/${userId}`, { role });
  }

  removeMember(workspaceId: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${workspaceId}/members/${userId}`);
  }

  getRoles(workspaceId: string): Observable<Role[]> {
    return this.http.get<ApiResponse<Role[]>>(`${this.apiUrl}/${workspaceId}/roles`).pipe(map(res => res.data));
  }
}
