import { Injectable, signal } from '@angular/core';
import { Organization } from './organization.service';

const STORAGE_KEY = 'selectedOrganizationId';

@Injectable({ providedIn: 'root' })
export class CurrentOrganizationService {
  private state = signal<Organization | null>(null);
  current = this.state.asReadonly();

  set(organization: Organization): void {
    this.state.set(organization);
    localStorage.setItem(STORAGE_KEY, organization.id);
  }

  clear(): void {
    this.state.set(null);
    localStorage.removeItem(STORAGE_KEY);
  }

  getPersistedId(): string | null {
    return localStorage.getItem(STORAGE_KEY);
  }
}
