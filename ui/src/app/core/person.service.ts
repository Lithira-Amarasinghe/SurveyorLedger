import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { map } from 'rxjs/operators';
import { Observable, of } from 'rxjs';
import { environment } from '../../environments/environment';
import { WorkspaceService, Member } from './workspace.service';

export interface Person {
  userId: string;
  name: string;
  roleLabel: string;
}

/** A person anywhere in the system - not necessarily a member of the current workspace. */
export interface Account {
  userId: string;
  firstName: string;
  lastName: string;
  email: string | null;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

export interface AddressInput {
  street?: string;
  city?: string;
  district?: string;
  postalCode?: string;
  country?: string;
}

@Injectable({ providedIn: 'root' })
export class PersonService {
  constructor(private workspaceService: WorkspaceService, private http: HttpClient) {}

  /**
   * Searches every account in the system, not just this workspace's members - land
   * ownership is a record-keeping reference, deliberately decoupled from workspace access
   * (an owner may never have been invited anywhere). Short queries return nothing rather
   * than dumping the whole user table.
   */
  searchAccounts(query: string): Observable<Account[]> {
    const term = query.trim();
    if (term.length < 2) return of([]);

    return this.http
      .get<ApiResponse<Account[]>>(`${environment.apiBaseUrl}/user/search`, { params: new HttpParams().set('q', term) })
      .pipe(map(res => res.data));
  }

  /**
   * Every person associated with a workspace is a real member (UserAccess row), so the
   * workspace member list is the single source for "who exists here" - adding someone new
   * goes through inviting them (see InvitationService), not a separate create-person call.
   */
  searchPeople(workspaceId: string, query: string): Observable<Person[]> {
    const term = query.trim().toLowerCase();

    return this.workspaceService.getMembers(workspaceId).pipe(
      map(members =>
        members
          .filter(m => !term || `${m.firstName} ${m.lastName}`.toLowerCase().includes(term) || m.email.toLowerCase().includes(term))
          .map(m => this.toPerson(m))
      )
    );
  }

  private toPerson(m: Member): Person {
    return { userId: m.userId, name: `${m.firstName} ${m.lastName}`, roleLabel: m.roles.join(', ') };
  }
}
