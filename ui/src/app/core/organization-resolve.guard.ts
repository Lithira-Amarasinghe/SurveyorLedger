import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { OrganizationService } from './organization.service';
import { CurrentOrganizationService } from './current-organization.service';

/**
 * Runs once on entering /app. Every user is guaranteed at least one organization (see the
 * invite-accept and startup-backfill changes elsewhere in this plan), so this never blocks
 * navigation - it only restores the persisted selection (or defaults to the first org) so the
 * user doesn't have to re-pick an organization every time they come back.
 */
export const organizationResolveGuard: CanActivateFn = () => {
  const orgService = inject(OrganizationService);
  const currentOrg = inject(CurrentOrganizationService);

  return orgService.list().pipe(
    map(orgs => {
      if (orgs.length === 0)
        return true;

      const persistedId = currentOrg.getPersistedId();
      const match = orgs.find(o => o.id === persistedId) ?? orgs[0];
      currentOrg.set(match);
      return true;
    }),
    catchError(() => of(true))
  );
};
