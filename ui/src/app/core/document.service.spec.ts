import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DocumentService } from './document.service';
import { environment } from '../../environments/environment';

describe('DocumentService', () => {
  let service: DocumentService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';
  const jobId = 'j1';
  const base = `${environment.apiBaseUrl}/workspace/${workspaceId}/job/${jobId}/document`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DocumentService]
    });
    service = TestBed.inject(DocumentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() unwraps ApiResponse and hits the correct URL', () => {
    const docs = [{ documentId: 'd1', jobId, fileName: 'plan.pdf', contentType: 'application/pdf', fileSizeBytes: 100, category: 'SurveyPlan', visibility: 'ClientVisible', uploadedBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01' }];
    service.list(workspaceId, jobId).subscribe(result => expect(result).toEqual(docs));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: docs });
  });

  it('upload() posts a FormData body with File, Category, Visibility', () => {
    const doc = { documentId: 'd1', jobId, fileName: 'plan.pdf', contentType: 'application/pdf', fileSizeBytes: 100, category: 'SurveyPlan', visibility: 'ClientVisible', uploadedBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    const file = new File(['content'], 'plan.pdf', { type: 'application/pdf' });

    service.upload(workspaceId, jobId, file, 'SurveyPlan', 'ClientVisible').subscribe(result => expect(result).toEqual(doc));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    const body = req.request.body as FormData;
    expect(body.get('File')).toBe(file);
    expect(body.get('Category')).toBe('SurveyPlan');
    expect(body.get('Visibility')).toBe('ClientVisible');
    req.flush({ success: true, data: doc });
  });

  it('getFileBlob() gets the document by id with blob response type', () => {
    const blob = new Blob(['bytes'], { type: 'application/pdf' });
    service.getFileBlob(workspaceId, jobId, 'd1').subscribe(result => expect(result).toEqual(blob));
    const req = httpMock.expectOne(`${base}/d1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(blob);
  });

  it('delete() deletes with no body', () => {
    service.delete(workspaceId, jobId, 'd1').subscribe();
    const req = httpMock.expectOne(`${base}/d1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
