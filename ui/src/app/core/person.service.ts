import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { WorkspaceService, Member } from './workspace.service';

export interface Person {
  userId: string;
  name: string;
  roleLabel: string;
}

interface ClientResponse {
  userId: string;
  firstName: string;
  lastName: string;
  phone: string | null;
  email: string | null;
  hasLogin: boolean;
  createdAt: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class PersonService {
  constructor(private http: HttpClient, private workspaceService: WorkspaceService) {}

  private clientBase(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/client`;
  }

  /**
   * Merges workspace members (real role: Admin/Manager/Surveyor/Client) with bare
   * clients (roleLabel "Client") into one search result, de-duplicated by userId -
   * a person who is both a workspace member AND has been used as a client keeps
   * their member entry (real role wins over the generic "Client" label).
   */
  searchPeople(workspaceId: string, query: string): Observable<Person[]> {
    const term = query.trim().toLowerCase();

    const members$ = this.workspaceService.getMembers(workspaceId).pipe(
      map(members =>
        members
          .filter(m => !term || `${m.firstName} ${m.lastName}`.toLowerCase().includes(term) || m.email.toLowerCase().includes(term))
          .map(m => this.toPersonFromMember(m))
      )
    );

    const params = new HttpParams().set('query', query);
    const clients$ = this.http
      .get<ApiResponse<ClientResponse[]>>(this.clientBase(workspaceId), { params })
      .pipe(map(res => res.data.map(c => this.toPersonFromClient(c))));

    return forkJoin({ members: members$, clients: clients$ }).pipe(
      map(({ members, clients }) => {
        const seen = new Set(members.map(m => m.userId));
        const uniqueClients = clients.filter(c => !seen.has(c.userId));
        return [...members, ...uniqueClients];
      })
    );
  }

  createClient(workspaceId: string, request: { firstName: string; lastName: string; phone?: string }): Observable<Person> {
    return this.http
      .post<ApiResponse<ClientResponse>>(this.clientBase(workspaceId), request)
      .pipe(map(res => this.toPersonFromClient(res.data)));
  }

  private toPersonFromMember(m: Member): Person {
    return { userId: m.userId, name: `${m.firstName} ${m.lastName}`, roleLabel: m.role };
  }

  private toPersonFromClient(c: ClientResponse): Person {
    return { userId: c.userId, name: `${c.firstName} ${c.lastName}`, roleLabel: 'Client' };
  }
}
