import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { InvitationService } from './invitation.service';
import { environment } from '../../environments/environment';

describe('InvitationService', () => {
  let service: InvitationService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [InvitationService]
    });
    service = TestBed.inject(InvitationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('create() posts the add-member request to /invitations - email is required, nothing granted until accept', () => {
    let result: unknown;
    service.create(workspaceId, { email: 'new@x.com', role: 'Client', firstName: 'New', lastName: 'Person' }).subscribe(r => (result = r));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/workspace/${workspaceId}/invitations`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'new@x.com', role: 'Client', firstName: 'New', lastName: 'Person' });
    req.flush({ success: true, data: {} });

    expect(result).toBeUndefined();
  });

  it('accept() posts to /invitations/{id}/accept', () => {
    let result: unknown;
    service.accept('inv-1').subscribe(r => (result = r));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/invitations/inv-1/accept`);
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, data: { workspaceId: 'ws-1', role: 'Surveyor' } });

    expect(result).toEqual({ workspaceId: 'ws-1', role: 'Surveyor' });
  });

  it('decline() posts to /invitations/{id}/decline', () => {
    service.decline('inv-1').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/invitations/inv-1/decline`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('mine() gets /invitations/mine', () => {
    let result: unknown;
    service.mine().subscribe(r => (result = r));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/invitations/mine`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [] });

    expect(result).toEqual([]);
  });
});
