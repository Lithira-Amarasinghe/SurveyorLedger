import { Injectable, signal } from '@angular/core';

export interface CurrentWorkspace {
  workspaceId: string;
  name: string;
  description: string;
  createdAt: string;
  role: string;
  roles: string[];
  tier: string;
}

@Injectable({ providedIn: 'root' })
export class CurrentWorkspaceService {
  private state = signal<CurrentWorkspace | null>(null);
  current = this.state.asReadonly();

  set(workspace: CurrentWorkspace): void {
    this.state.set(workspace);
  }

  clear(): void {
    this.state.set(null);
  }
}
