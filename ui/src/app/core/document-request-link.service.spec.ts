import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DocumentRequestLinkService } from './document-request-link.service';
import { environment } from '../../environments/environment';

describe('DocumentRequestLinkService', () => {
  let service: DocumentRequestLinkService;
  let httpMock: HttpTestingController;
  const token = 'abc123';
  const base = `${environment.apiBaseUrl}/document-request-links/${token}`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DocumentRequestLinkService]
    });
    service = TestBed.inject(DocumentRequestLinkService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getPreview() unwraps ApiResponse', () => {
    const preview = { title: 'Legal Deed', description: null, category: 'LegalDocument', workspaceName: 'Acme', jobTitle: 'Job 1', expired: false, alreadyFulfilled: false };
    service.getPreview(token).subscribe(result => expect(result).toEqual(preview));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: preview });
  });

  it('upload() posts FormData with Files and optional DisplayFileName', () => {
    const file = new File(['bytes'], 'deed.pdf', { type: 'application/pdf' });
    service.upload(token, [file], 'Renamed.pdf').subscribe();
    const req = httpMock.expectOne(`${base}/upload`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    const body = req.request.body as FormData;
    expect(body.get('Files')).toBe(file);
    expect(body.get('DisplayFileName')).toBe('Renamed.pdf');
    req.flush(null);
  });
});
