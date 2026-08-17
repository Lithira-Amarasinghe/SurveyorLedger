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
  roles: string[];
}

/** An extra scope a member holds access to beyond the workspace itself - today, a specific job. */
export interface MemberScopeGrant {
  scopeType: string;
  scopeId: string;
  label: string;
  role: string;
}

/** Blanket access a member's role grants over an entire scope type, and what it lets them do. */
export interface MemberFullAccessGrant {
  scopeType: string;
  roleName: string;
  actions: string[];
}

export interface Member {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  assignedAt: string;
  isOwner: boolean;
  /** Blanket (view_all) access this member's role(s) grant, with the actions each allows. */
  fullAccessGrants: MemberFullAccessGrant[];
  /** Explicit per-scope grants, e.g. individual job assignments. */
  additionalScopes: MemberScopeGrant[];
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

  /** Role names valid to pick for this scope - "Workspace" for invite/role-change, "Job" for job assignment. Backend is the single source of truth. */
  getEligibleRoles(workspaceId: string, scope: 'Workspace' | 'Job'): Observable<string[]> {
    return this.http
      .get<ApiResponse<string[]>>(`${this.apiUrl}/${workspaceId}/roles/eligible`, { params: { scope } })
      .pipe(map(res => res.data));
  }

  addMemberRole(workspaceId: string, userId: string, role: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${workspaceId}/members/${userId}/roles`, { role });
  }

  removeMemberRole(workspaceId: string, userId: string, role: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${workspaceId}/members/${userId}/roles/${role}`);
  }

  removeMember(workspaceId: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${workspaceId}/members/${userId}`);
  }

  getRoles(workspaceId: string): Observable<Role[]> {
    return this.http.get<ApiResponse<Role[]>>(`${this.apiUrl}/${workspaceId}/roles`).pipe(map(res => res.data));
  }
}
