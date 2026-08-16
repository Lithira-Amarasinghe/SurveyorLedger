import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { JobService } from './job.service';
import { CurrentWorkspaceService } from './current-workspace.service';

/**
 * Guards /app/job/:workspaceId/:jobId - the minimal-shell route for a job-only grant (no
 * workspace membership). Deliberately does NOT call CurrentWorkspaceService.set() the way
 * workspaceResolveGuard does: SidebarComponent renders the full workspace tab list
 * (Overview/Land/Billing/Members) off any truthy currentWorkspace.current(), with no
 * per-tab permission check - setting it here would leak that nav to someone who can't use
 * most of it. Leaving it cleared keeps the sidebar in its no-workspace state instead.
 */
export const jobAccessGuard: CanActivateFn = (route) => {
  const jobService = inject(JobService);
  const currentWorkspace = inject(CurrentWorkspaceService);
  const router = inject(Router);
  const jobId = route.paramMap.get('jobId')!;

  currentWorkspace.clear();

  return jobService.getStandalone(jobId).pipe(
    map(() => true),
    catchError(() =>
      of(router.createUrlTree(['/app/dashboard'], { queryParams: { error: 'job-not-found' } }))
    )
  );
};
