import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { WorkspaceService } from './workspace.service';
import { JobService } from './job.service';
import { CurrentWorkspaceService } from './current-workspace.service';

const ROLE_PRIORITY = ['Admin', 'Surveyor', 'Member'];

function pickPrimaryRole(roles: string[]): string {
  return ROLE_PRIORITY.find(r => roles.includes(r)) ?? roles[0] ?? '';
}

/**
 * GetWorkspaceByIdAsync requires a Workspace-scope UserAccess row, so it 404s for a
 * job-only user (they only hold a Job-scope grant) even when they're bookmarking or
 * following an old link into /app/workspace/:id/jobs/:jobId for a job they DO have
 * access to. Before bouncing them to the dashboard, check whether this is exactly that
 * case and redirect to the job-only leaf route (/app/job/:workspaceId/:jobId) instead -
 * same route jobAccessGuard already serves for direct job-only navigation.
 */
export const workspaceResolveGuard: CanActivateFn = (route) => {
  const workspaceService = inject(WorkspaceService);
  const jobService = inject(JobService);
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
    catchError(() => {
      const jobId = route.firstChild?.paramMap.get('jobId');
      if (!jobId) {
        return of(router.createUrlTree(['/app/dashboard'], { queryParams: { error: 'workspace-not-found' } }));
      }
      return jobService.getStandalone(jobId).pipe(
        map(() => router.createUrlTree(['/app/job', id, jobId])),
        catchError(() => of(router.createUrlTree(['/app/dashboard'], { queryParams: { error: 'workspace-not-found' } })))
      );
    })
  );
};
