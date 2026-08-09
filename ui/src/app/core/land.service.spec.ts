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
    const request = { address: { street: '123 Main St', city: 'Colombo', district: null, postalCode: null, country: null }, size: 10, sizeUnit: 'acres' };
    const land = { landId: 'l1', address: request.address, size: 10, sizeUnit: 'acres', gpsCoordinates: null, notes: null, createdAt: '2026-01-01', updatedAt: '2026-01-01' };
    service.create(workspaceId, request).subscribe(result => expect(result).toEqual(land));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ success: true, data: land });
  });
});

describe('addressLine', () => {
  const baseLand: Land = {
    landId: 'l1',
    address: { street: null, city: null, district: null, postalCode: null, country: null },
    size: null,
    sizeUnit: null,
    gpsCoordinates: null,
    notes: null,
    createdAt: '2026-01-01',
    updatedAt: '2026-01-01'
  };

  it('joins street and city with a comma', () => {
    expect(addressLine({ ...baseLand, address: { ...baseLand.address, street: '123 Main St', city: 'Colombo' } })).toBe('123 Main St, Colombo');
  });

  it('falls back to a placeholder when both are empty', () => {
    expect(addressLine(baseLand)).toBe('Unnamed land record');
  });

  it('uses just street when city is missing', () => {
    expect(addressLine({ ...baseLand, address: { ...baseLand.address, street: '123 Main St' } })).toBe('123 Main St');
  });
});
