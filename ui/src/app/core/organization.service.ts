import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Organization {
  id: string;
  name: string;
  tier: string;
  workspaceCount: number;
  maxWorkspaces: number;
  callerRoles: string[];
}

export interface OrganizationMember {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  isOwner: boolean;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class OrganizationService {
  private apiUrl = `${environment.apiBaseUrl}/organization`;

  constructor(private http: HttpClient) {}

  list(): Observable<Organization[]> {
    return this.http.get<ApiResponse<Organization[]>>(this.apiUrl).pipe(map(res => res.data));
  }

  create(name: string): Observable<Organization> {
    return this.http.post<ApiResponse<Organization>>(this.apiUrl, { name }).pipe(map(res => res.data));
  }

  getById(id: string): Observable<Organization> {
    return this.http.get<ApiResponse<Organization>>(`${this.apiUrl}/${id}`).pipe(map(res => res.data));
  }

  getMembers(id: string): Observable<OrganizationMember[]> {
    return this.http.get<ApiResponse<OrganizationMember[]>>(`${this.apiUrl}/${id}/members`).pipe(map(res => res.data));
  }

  addMember(id: string, targetUserId: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${id}/members/${targetUserId}`, {});
  }

  removeMember(id: string, targetUserId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}/members/${targetUserId}`);
  }

  updateSubscription(id: string, tier: string): Observable<Organization> {
    return this.http.put<ApiResponse<Organization>>(`${this.apiUrl}/${id}/subscription`, { tier }).pipe(map(res => res.data));
  }
}
