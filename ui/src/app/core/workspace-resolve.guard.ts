import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { WorkspaceService } from './workspace.service';
import { CurrentWorkspaceService } from './current-workspace.service';

const ROLE_PRIORITY = ['Admin', 'Surveyor', 'Member'];

function pickPrimaryRole(roles: string[]): string {
  return ROLE_PRIORITY.find(r => roles.includes(r)) ?? roles[0] ?? '';
}

export const workspaceResolveGuard: CanActivateFn = (route) => {
  const workspaceService = inject(WorkspaceService);
  const currentWorkspace = inject(CurrentWorkspaceService);
  const router = inject(Router);
  const id = route.paramMap.get('id')!;

  return workspaceService.getById(id).pipe(
    map(workspace => {
      currentWorkspace.set({
        workspaceId: workspace.workspaceId,
        name: workspace.name,
        description: workspace.description,
        createdAt: workspace.createdAt,
        // A user can hold more than one role at a workspace now - pick the highest-priority
        // one as "the" role for pages that only need a single label (nav, guards).
        role: pickPrimaryRole(workspace.roles),
        roles: workspace.roles,
        tier: workspace.tier,
      });
      return true;
    }),
    catchError(() =>
      of(router.createUrlTree(['/app/dashboard'], { queryParams: { error: 'workspace-not-found' } }))
    )
  );
};
