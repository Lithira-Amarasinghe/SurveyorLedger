import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DocumentRequestService } from './document-request.service';
import { environment } from '../../environments/environment';

describe('DocumentRequestService', () => {
  let service: DocumentRequestService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';
  const jobId = 'j1';
  const base = `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/document-request`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DocumentRequestService]
    });
    service = TestBed.inject(DocumentRequestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const sample = {
    requestId: 'r1', jobId, title: 'Legal Deed', description: null, category: 'LegalDocument',
    targetRole: null, targetUserId: null, targetUserName: null, hasActiveShareLink: false,
    status: 'Pending', fulfilledDocumentId: null, fulfilledAt: null, fulfilledBy: null,
    requestedBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01'
  };

  it('list() unwraps ApiResponse', () => {
    service.list(workspaceId, jobId).subscribe(result => expect(result).toEqual([sample]));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [sample] });
  });

  it('create() posts title/description/category/targetRole/targetUserId', () => {
    service.create(workspaceId, jobId, 'Legal Deed', null, 'LegalDocument', null, null).subscribe(result => expect(result).toEqual(sample));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ title: 'Legal Deed', description: null, category: 'LegalDocument', targetRole: null, targetUserId: null });
    req.flush({ success: true, data: sample });
  });

  it('fulfill() posts FormData to /{id}/fulfill', () => {
    const file = new File(['bytes'], 'deed.pdf', { type: 'application/pdf' });
    const fulfilled = { ...sample, status: 'Fulfilled', fulfilledDocumentId: 'd1' };
    service.fulfill(workspaceId, jobId, 'r1', file, 'ClientVisible').subscribe(result => expect(result).toEqual(fulfilled));
    const req = httpMock.expectOne(`${base}/r1/fulfill`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush({ success: true, data: fulfilled });
  });

  it('reopen() posts to /{id}/reopen with an optional note', () => {
    service.reopen(workspaceId, jobId, 'r1', 'Scan as PDF.').subscribe(result => expect(result).toEqual(sample));
    const req = httpMock.expectOne(`${base}/r1/reopen`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ note: 'Scan as PDF.' });
    req.flush({ success: true, data: sample });
  });

  it('cancel() deletes with no body', () => {
    service.cancel(workspaceId, jobId, 'r1').subscribe();
    const req = httpMock.expectOne(`${base}/r1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('updateTarget() patches the target sub-route', () => {
    const updated = { ...sample, targetRole: 'Client' };
    service.updateTarget(workspaceId, jobId, 'r1', 'Client', null).subscribe(result => expect(result).toEqual(updated));
    const req = httpMock.expectOne(`${base}/r1/target`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ targetRole: 'Client', targetUserId: null });
    req.flush({ success: true, data: updated });
  });

  it('generateShareLink() posts to /{id}/share-link', () => {
    const link = { token: 'abc123', expiresAt: '2026-01-08' };
    service.generateShareLink(workspaceId, jobId, 'r1').subscribe(result => expect(result).toEqual(link));
    const req = httpMock.expectOne(`${base}/r1/share-link`);
    expect(req.request.method).toBe('POST');
    req.flush({ success: true, data: link });
  });

  it('revokeShareLink() deletes /{id}/share-link', () => {
    service.revokeShareLink(workspaceId, jobId, 'r1').subscribe();
    const req = httpMock.expectOne(`${base}/r1/share-link`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
