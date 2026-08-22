import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { firstValueFrom } from 'rxjs';
import { organizationResolveGuard } from './organization-resolve.guard';
import { CurrentOrganizationService } from './current-organization.service';
import { environment } from '../../environments/environment';
import { Organization } from './organization.service';

describe('organizationResolveGuard', () => {
  let httpMock: HttpTestingController;
  let currentOrg: CurrentOrganizationService;
  const base = `${environment.apiBaseUrl}/organization`;
  const orgs: Organization[] = [
    { id: 'o1', name: 'First', tier: 'Free', workspaceCount: 1, maxWorkspaces: 1, callerRoles: ['OrgOwner'] },
    { id: 'o2', name: 'Second', tier: 'Free', workspaceCount: 1, maxWorkspaces: 1, callerRoles: ['OrgMember'] }
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule]
    });
    httpMock = TestBed.inject(HttpTestingController);
    currentOrg = TestBed.inject(CurrentOrganizationService);
    localStorage.clear();
  });

  afterEach(() => httpMock.verify());

  it('restores a persisted org id that is still in the list', async () => {
    localStorage.setItem('selectedOrganizationId', 'o2');

    const resultPromise = TestBed.runInInjectionContext(() =>
      firstValueFrom(organizationResolveGuard({} as any, {} as any) as any)
    );

    httpMock.expectOne(base).flush({ success: true, data: orgs });
    const allowed = await resultPromise;

    expect(allowed).toBe(true);
    expect(currentOrg.current()?.id).toBe('o2');
  });

  it('falls back to the first org when the persisted id is stale', async () => {
    localStorage.setItem('selectedOrganizationId', 'does-not-exist');

    const resultPromise = TestBed.runInInjectionContext(() =>
      firstValueFrom(organizationResolveGuard({} as any, {} as any) as any)
    );

    httpMock.expectOne(base).flush({ success: true, data: orgs });
    const allowed = await resultPromise;

    expect(allowed).toBe(true);
    expect(currentOrg.current()?.id).toBe('o1');
  });
});
