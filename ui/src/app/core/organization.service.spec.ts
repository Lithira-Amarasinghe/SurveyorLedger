import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { OrganizationService, Organization } from './organization.service';
import { environment } from '../../environments/environment';

describe('OrganizationService', () => {
  let service: OrganizationService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/organization`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [OrganizationService]
    });
    service = TestBed.inject(OrganizationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() gets every organization the caller belongs to', () => {
    const orgs: Organization[] = [{ id: 'o1', name: 'Acme', tier: 'Free', workspaceCount: 1, maxWorkspaces: 1, callerRoles: ['OrgOwner'] }];
    service.list().subscribe(result => expect(result).toEqual(orgs));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: orgs });
  });

  it('create() posts the org name', () => {
    const org: Organization = { id: 'o2', name: 'New Co', tier: 'Free', workspaceCount: 0, maxWorkspaces: 1, callerRoles: ['OrgOwner'] };
    service.create('New Co').subscribe(result => expect(result).toEqual(org));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'New Co' });
    req.flush({ success: true, data: org });
  });

  it('getById() gets a single organization', () => {
    const org: Organization = { id: 'o1', name: 'Acme', tier: 'Free', workspaceCount: 1, maxWorkspaces: 1, callerRoles: ['OrgOwner'] };
    service.getById('o1').subscribe(result => expect(result).toEqual(org));
    const req = httpMock.expectOne(`${base}/o1`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: org });
  });

  it('getMembers() gets the member roster', () => {
    service.getMembers('o1').subscribe();
    const req = httpMock.expectOne(`${base}/o1/members`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [] });
  });

  it('addMember() posts to the member endpoint', () => {
    service.addMember('o1', 'u1').subscribe();
    const req = httpMock.expectOne(`${base}/o1/members/u1`);
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, data: null });
  });

  it('removeMember() deletes the member', () => {
    service.removeMember('o1', 'u1').subscribe();
    const req = httpMock.expectOne(`${base}/o1/members/u1`);
    expect(req.request.method).toBe('DELETE');
    req.flush({ success: true, data: null });
  });

  it('updateSubscription() puts the tier', () => {
    const org: Organization = { id: 'o1', name: 'Acme', tier: 'Pro', workspaceCount: 1, maxWorkspaces: 5, callerRoles: ['OrgOwner'] };
    service.updateSubscription('o1', 'Pro').subscribe(result => expect(result).toEqual(org));
    const req = httpMock.expectOne(`${base}/o1/subscription`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ tier: 'Pro' });
    req.flush({ success: true, data: org });
  });
});
