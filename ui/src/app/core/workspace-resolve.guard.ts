import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { WorkspaceService } from './workspace.service';
import { CurrentWorkspaceService } from './current-workspace.service';

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
        role: workspace.role,
        tier: workspace.tier,
      });
      return true;
    }),
    catchError(() =>
      of(router.createUrlTree(['/app/dashboard'], { queryParams: { error: 'workspace-not-found' } }))
    )
  );
};
