import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PersonService } from './person.service';
import { environment } from '../../environments/environment';

describe('PersonService', () => {
  let service: PersonService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [PersonService]
    });
    service = TestBed.inject(PersonService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('searchPeople() merges members and clients, labels each by real role', () => {
    let result: unknown;
    service.searchPeople(workspaceId, 'sam').subscribe(r => (result = r));

    const membersReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/members`);
    membersReq.flush({
      success: true,
      data: [
        { userId: 'u1', email: 'sam@x.com', firstName: 'Sam', lastName: 'Surveyor', role: 'Surveyor', assignedAt: '2026-01-01', isOwner: false },
        { userId: 'u2', email: 'ann@x.com', firstName: 'Ann', lastName: 'Admin', role: 'Admin', assignedAt: '2026-01-01', isOwner: true }
      ]
    });

    const clientsReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/client?query=sam`);
    clientsReq.flush({
      success: true,
      data: [{ userId: 'u3', firstName: 'Samantha', lastName: 'Client', phone: '077', email: null, hasLogin: false, createdAt: '2026-01-01' }]
    });

    expect(result).toEqual([
      { userId: 'u1', name: 'Sam Surveyor', roleLabel: 'Surveyor' },
      { userId: 'u3', name: 'Samantha Client', roleLabel: 'Client' }
    ]);
  });

  it('searchPeople() de-duplicates by userId, preferring the member entry (real role over generic Client label)', () => {
    let result: unknown;
    service.searchPeople(workspaceId, 'sam').subscribe(r => (result = r));

    const membersReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/members`);
    membersReq.flush({
      success: true,
      data: [{ userId: 'u1', email: 'sam@x.com', firstName: 'Sam', lastName: 'Both', role: 'Surveyor', assignedAt: '2026-01-01', isOwner: false }]
    });

    const clientsReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/client?query=sam`);
    clientsReq.flush({
      success: true,
      data: [{ userId: 'u1', firstName: 'Sam', lastName: 'Both', phone: null, email: 'sam@x.com', hasLogin: true, createdAt: '2026-01-01' }]
    });

    expect(result).toEqual([{ userId: 'u1', name: 'Sam Both', roleLabel: 'Surveyor' }]);
  });

  it('createClient() posts to /client and returns a Person labeled Client', () => {
    let result: unknown;
    service.createClient(workspaceId, { firstName: 'New', lastName: 'Client', phone: '0771234567' }).subscribe(r => (result = r));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/client`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ firstName: 'New', lastName: 'Client', phone: '0771234567' });
    req.flush({ success: true, data: { userId: 'u9', firstName: 'New', lastName: 'Client', phone: '0771234567', email: null, hasLogin: false, createdAt: '2026-01-01' } });

    expect(result).toEqual({ userId: 'u9', name: 'New Client', roleLabel: 'Client' });
  });
});
