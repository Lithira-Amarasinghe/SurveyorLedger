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

  it('searchPeople() filters the workspace member list by name/email, labels each by real role', () => {
    let result: unknown;
    service.searchPeople(workspaceId, 'sam').subscribe(r => (result = r));

    const membersReq = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/members`);
    membersReq.flush({
      success: true,
      data: [
        { userId: 'u1', email: 'sam@x.com', firstName: 'Sam', lastName: 'Surveyor', role: 'Surveyor', assignedAt: '2026-01-01', isOwner: false },
        { userId: 'u2', email: 'ann@x.com', firstName: 'Ann', lastName: 'Admin', role: 'Admin', assignedAt: '2026-01-01', isOwner: true },
        { userId: 'u3', email: 'samantha@x.com', firstName: 'Samantha', lastName: 'Client', role: 'Client', assignedAt: '2026-01-01', isOwner: false }
      ]
    });

    expect(result).toEqual([
      { userId: 'u1', name: 'Sam Surveyor', roleLabel: 'Surveyor' },
      { userId: 'u3', name: 'Samantha Client', roleLabel: 'Client' }
    ]);
  });
});
