import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { LandService, addressLine, Land } from './land.service';
import { environment } from '../../environments/environment';

describe('LandService', () => {
  let service: LandService;
  let httpMock: HttpTestingController;
  const workspaceId = 'ws-1';
  const base = `${environment.apiBaseUrl}/workspace/${workspaceId}/land`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [LandService]
    });
    service = TestBed.inject(LandService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('search() with a query appends ?query=', () => {
    service.search(workspaceId, 'main st').subscribe();
    const req = httpMock.expectOne(`${base}?query=main%20st`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [] });
  });

  it('search() with no query hits the base URL', () => {
    service.search(workspaceId).subscribe();
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: [] });
  });

  it('create() posts the land request', () => {
    const request = { address: { village: '123 Main St', district: 'Colombo' }, area: { acres: 10, roods: 0, perches: 0 } };
    const land = { landId: 'l1', address: { village: '123 Main St', gramaNiladhariDivision: null, divisionalSecretariat: null, pradeshiyaSabha: null, korale: null, hatpattu: null, district: 'Colombo', province: null }, area: { acres: 10, roods: 0, perches: 0, squareMeters: 40468.564224, hectares: 4.0468564224 }, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    service.create(workspaceId, request).subscribe(result => expect(result).toEqual(land));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: land });
  });

  it('getById() gets a single land', () => {
    const land = { landId: 'l1', address: { village: 'Main St', gramaNiladhariDivision: null, divisionalSecretariat: null, pradeshiyaSabha: null, korale: null, hatpattu: null, district: null, province: null }, area: { acres: null, roods: null, perches: null, squareMeters: null, hectares: null }, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    service.getById(workspaceId, 'l1').subscribe(result => expect(result).toEqual(land));
    const req = httpMock.expectOne(`${base}/l1`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: land });
  });

  it('update() puts the land request', () => {
    const request = { address: { village: 'New St', district: null } };
    service.update(workspaceId, 'l1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('getSurveys() gets the /surveys sub-route', () => {
    const surveys = [{ id: 's1', landId: 'l1', surveyPlanNumber: 'SP-1', surveyDate: '2020-01-01', surveyedByName: null, notes: null, createdAt: '2026-01-01' }];
    service.getSurveys(workspaceId, 'l1').subscribe(result => expect(result).toEqual(surveys));
    const req = httpMock.expectOne(`${base}/l1/surveys`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: surveys });
  });

  it('addSurvey() posts to /surveys', () => {
    const request = { surveyPlanNumber: 'SP-2', surveyDate: '2026-01-01' };
    service.addSurvey(workspaceId, 'l1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/surveys`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('getDeeds() gets the /deeds sub-route', () => {
    const deeds = [{ id: 'd1', landId: 'l1', deedNumber: 'DN-1', issuedDate: '2020-01-01', isCurrent: true, notes: null, createdAt: '2026-01-01' }];
    service.getDeeds(workspaceId, 'l1').subscribe(result => expect(result).toEqual(deeds));
    const req = httpMock.expectOne(`${base}/l1/deeds`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: deeds });
  });

  it('addDeed() posts to /deeds', () => {
    const request = { deedNumber: 'DN-2', issuedDate: '2026-01-01', isCurrent: true };
    service.addDeed(workspaceId, 'l1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/deeds`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('getBoundaries() gets the /boundaries sub-route', () => {
    const boundaries = [{ id: 'b1', landId: 'l1', label: 'North', description: null, createdAt: '2026-01-01' }];
    service.getBoundaries(workspaceId, 'l1').subscribe(result => expect(result).toEqual(boundaries));
    const req = httpMock.expectOne(`${base}/l1/boundaries`);
    expect(req.request.method).toBe('GET');
    req.flush({ success: true, data: boundaries });
  });

  it('addBoundary() posts to /boundaries', () => {
    const request = { label: 'River side', description: 'Runs along the river' };
    service.addBoundary(workspaceId, 'l1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/boundaries`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('updateSurvey() puts to the survey sub-route', () => {
    const request = { surveyPlanNumber: 'SP-1-fixed', surveyDate: '2020-01-01' };
    service.updateSurvey(workspaceId, 'l1', 's1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/surveys/s1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('deleteSurvey() deletes the survey sub-route', () => {
    service.deleteSurvey(workspaceId, 'l1', 's1').subscribe();
    const req = httpMock.expectOne(`${base}/l1/surveys/s1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('updateDeed() puts to the deed sub-route', () => {
    const request = { deedNumber: 'DN-1-fixed', issuedDate: '2020-01-01', isCurrent: true };
    service.updateDeed(workspaceId, 'l1', 'd1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/deeds/d1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('deleteDeed() deletes the deed sub-route', () => {
    service.deleteDeed(workspaceId, 'l1', 'd1').subscribe();
    const req = httpMock.expectOne(`${base}/l1/deeds/d1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('updateBoundary() puts to the boundary sub-route', () => {
    const request = { label: 'North (fixed)' };
    service.updateBoundary(workspaceId, 'l1', 'b1', request).subscribe();
    const req = httpMock.expectOne(`${base}/l1/boundaries/b1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: {} });
  });

  it('deleteBoundary() deletes the boundary sub-route', () => {
    service.deleteBoundary(workspaceId, 'l1', 'b1').subscribe();
    const req = httpMock.expectOne(`${base}/l1/boundaries/b1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('delete() deletes the land', () => {
    service.delete(workspaceId, 'l1').subscribe();
    const req = httpMock.expectOne(`${base}/l1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});

describe('addressLine', () => {
  const baseLand: Land = {
    landId: 'l1',
    address: { village: null, gramaNiladhariDivision: null, divisionalSecretariat: null, pradeshiyaSabha: null, korale: null, hatpattu: null, district: null, province: null },
    area: { acres: null, roods: null, perches: null, squareMeters: null, hectares: null },
    notes: null,
    createdAt: '2026-01-01',
    updatedAt: '2026-01-01',
    ownerId: null,
    ownerName: null,
    ownerPhone: null,
    ownerEmail: null,
    hasActiveLocationShareLink: false,
    hasActiveMapViewShareLink: false
  };

  it('joins village, DS division, and district with commas', () => {
    expect(addressLine({ ...baseLand, address: { ...baseLand.address, village: 'Kotte', divisionalSecretariat: 'Kotte DS', district: 'Colombo' } })).toBe('Kotte, Kotte DS, Colombo');
  });

  it('falls back to a placeholder when all are empty', () => {
    expect(addressLine(baseLand)).toBe('Unnamed land record');
  });

  it('uses just village when the rest are missing', () => {
    expect(addressLine({ ...baseLand, address: { ...baseLand.address, village: 'Kotte' } })).toBe('Kotte');
  });
});
