import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { JobService } from './job.service';
import { environment } from '../../environments/environment';

describe('JobService', () => {
  let service: JobService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';
  const base = `${environment.apiBaseUrl}/workspace/${workspaceId}/job`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [JobService]
    });
    service = TestBed.inject(JobService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() unwraps ApiResponse and hits the correct URL', () => {
    const jobs = [{ jobId: 'j1', jobNumber: 'JOB-0001', title: 'Test', description: null, status: 'Draft', createdBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01' }];
    service.list(workspaceId).subscribe(result => expect(result).toEqual(jobs));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: jobs });
  });

  it('create() posts title only', () => {
    const job = { jobId: 'j1', jobNumber: 'JOB-0001', title: 'New job', description: null, status: 'Draft', createdBy: 'u1', createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    service.create(workspaceId, 'New job').subscribe(result => expect(result).toEqual(job));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ title: 'New job' });
    req.flush({ success: true, data: job });
  });

  it('updateStatus() puts to the /status sub-route', () => {
    service.updateStatus(workspaceId, 'j1', 'Scheduled').subscribe();
    const req = httpMock.expectOne(`${base}/j1/status`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ status: 'Scheduled' });
    req.flush({ success: true, data: {} });
  });

  it('addParticipant() posts the chosen job-scoped role', () => {
    service.addParticipant(workspaceId, 'j1', 'u2', 'Surveyor').subscribe();
    const req = httpMock.expectOne(`${base}/j1/participants/u2`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ role: 'Surveyor' });
    req.flush({ success: true, data: {} });
  });

  it('removeParticipant() deletes with no body', () => {
    service.removeParticipant(workspaceId, 'j1', 'u2').subscribe();
    const req = httpMock.expectOne(`${base}/j1/participants/u2`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getLands() gets the /lands sub-route', () => {
    const lands = [{ landId: 'l1', address: { street: 'Main St', city: null, district: null, postalCode: null, country: null }, size: null, sizeUnit: null, gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' }];
    service.getLands(workspaceId, 'j1').subscribe(result => expect(result).toEqual(lands));
    const req = httpMock.expectOne(`${base}/j1/lands`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: lands });
  });

  it('addLand() posts with no body', () => {
    service.addLand(workspaceId, 'j1', 'land1').subscribe();
    const req = httpMock.expectOne(`${base}/j1/lands/land1`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('removeLand() deletes', () => {
    service.removeLand(workspaceId, 'j1', 'land1').subscribe();
    const req = httpMock.expectOne(`${base}/j1/lands/land1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
