import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface Address {
  street: string | null;
  city: string | null;
  district: string | null;
  postalCode: string | null;
  country: string | null;
}

export interface LandAreaValue {
  acres: number | null;
  roods: number | null;
  perches: number | null;
  squareMeters: number | null;
  hectares: number | null;
}

export interface Land {
  landId: string;
  address: Address;
  area: LandAreaValue;
  gpsCoordinates: string | null;
  latitude: number | null;
  longitude: number | null;
  hasActiveLocationShareLink: boolean;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
  /** Set when the owner is a real account. Null when the owner is only plain contact info. */
  ownerId: string | null;
  ownerName: string | null;
  ownerPhone: string | null;
  ownerEmail: string | null;
}

export interface LandRequest {
  address?: Address;
  area?: Partial<LandAreaValue>;
  gpsCoordinates?: string;
  notes?: string;
  /** Either ownerId (an existing account) or ownerName/-Phone/-Email - never both. */
  ownerId?: string;
  ownerName?: string;
  ownerPhone?: string;
  ownerEmail?: string;
}

export interface LandLocation {
  lat: number;
  lng: number;
}

export interface LandSurvey {
  id: string;
  landId: string;
  surveyPlanNumber: string;
  surveyDate: string;
  surveyedByName: string | null;
  notes: string | null;
  createdAt: string;
}

export interface LandSurveyRequest {
  surveyPlanNumber: string;
  surveyDate: string;
  surveyedByName?: string;
  notes?: string;
}

export interface LandDeed {
  id: string;
  landId: string;
  deedNumber: string;
  issuedDate: string;
  isCurrent: boolean;
  notes: string | null;
  createdAt: string;
}

export interface LandDeedRequest {
  deedNumber: string;
  issuedDate: string;
  isCurrent: boolean;
  notes?: string;
}

export interface LandBoundary {
  id: string;
  landId: string;
  label: string;
  description: string | null;
  createdAt: string;
}

export interface LandBoundaryRequest {
  label: string;
  description?: string;
}

export interface LandPhoto {
  photoId: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  uploadedByName: string;
  createdAt: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

/** Single source of truth for formatting a Land's address into a display line. */
export function addressLine(land: Land): string {
  return [land.address.street, land.address.city].filter(Boolean).join(', ') || 'Unnamed land record';
}

/** tel:/wa.me hrefs from free-text OwnerPhone - strips formatting for the link only, display text is untouched. Malformed numbers simply won't resolve on tap; no validation is added (matches OwnerPhone staying unvalidated free text). */
export function telHref(phone: string): string {
  return `tel:${phone.replace(/[^\d+]/g, '')}`;
}

export function whatsAppHref(phone: string): string {
  return `https://wa.me/${phone.replace(/[^\d+]/g, '')}`;
}

const SQUARE_METERS_PER_PERCH = 25.29285264;
const SQUARE_METERS_PER_ROOD = SQUARE_METERS_PER_PERCH * 40;
const SQUARE_METERS_PER_ACRE = SQUARE_METERS_PER_ROOD * 4;
const SQUARE_METERS_PER_HECTARE = 10000;

/** Mirrors AreaConversion.FromAcresRoodsPerches server-side - used for the live client-side preview only, the server value on save is authoritative. */
export function acresRoodsPerchesToSquareMeters(acres: number, roods: number, perches: number): number {
  return acres * SQUARE_METERS_PER_ACRE + roods * SQUARE_METERS_PER_ROOD + perches * SQUARE_METERS_PER_PERCH;
}

/** Mirrors AreaConversion.ToAcresRoodsPerches server-side. */
export function squareMetersToAcresRoodsPerches(squareMeters: number): { acres: number; roods: number; perches: number } {
  const totalPerches = squareMeters / SQUARE_METERS_PER_PERCH;
  const acres = Math.floor(totalPerches / 160);
  const remainder = totalPerches - acres * 160;
  const roods = Math.floor(remainder / 40);
  const perches = Math.round((remainder - roods * 40) * 100) / 100;
  return { acres, roods, perches };
}

export function squareMetersToHectares(squareMeters: number): number {
  return squareMeters / SQUARE_METERS_PER_HECTARE;
}

export function hectaresToSquareMeters(hectares: number): number {
  return hectares * SQUARE_METERS_PER_HECTARE;
}

/** Single source of truth for displaying a LandAreaValue - always formats from the A-R-P fields, which a Land response always has populated regardless of which unit it was entered in. */
export function formatArea(area: LandAreaValue): string {
  const { acres, roods, perches } = area;
  if (acres === null && roods === null && perches === null) return '—';

  const parts: string[] = [];
  if (acres) parts.push(`${acres}A`);
  if (roods) parts.push(`${roods}R`);
  if (perches || parts.length === 0) parts.push(`${perches ?? 0}P`);
  return parts.join(' ');
}

@Injectable({ providedIn: 'root' })
export class LandService {
  constructor(private http: HttpClient) {}

  private base(workspaceId: string): string {
    return `${environment.apiBaseUrl}/workspace/${workspaceId}/land`;
  }

  search(workspaceId: string, query?: string): Observable<Land[]> {
    const params = query ? new HttpParams().set('query', query) : undefined;
    return this.http.get<ApiResponse<Land[]>>(this.base(workspaceId), { params }).pipe(map(res => res.data));
  }

  create(workspaceId: string, request: LandRequest): Observable<Land> {
    return this.http.post<ApiResponse<Land>>(this.base(workspaceId), request).pipe(map(res => res.data));
  }

  getById(workspaceId: string, landId: string): Observable<Land> {
    return this.http.get<ApiResponse<Land>>(`${this.base(workspaceId)}/${landId}`).pipe(map(res => res.data));
  }

  update(workspaceId: string, landId: string, request: LandRequest): Observable<Land> {
    return this.http.put<ApiResponse<Land>>(`${this.base(workspaceId)}/${landId}`, request).pipe(map(res => res.data));
  }

  setLocation(workspaceId: string, landId: string, location: LandLocation): Observable<Land> {
    return this.http
      .put<ApiResponse<Land>>(`${this.base(workspaceId)}/${landId}/location`, { latitude: location.lat, longitude: location.lng })
      .pipe(map(res => res.data));
  }

  generateLocationShareLink(workspaceId: string, landId: string): Observable<string> {
    return this.http
      .post<ApiResponse<{ token: string }>>(`${this.base(workspaceId)}/${landId}/location-share-link`, {})
      .pipe(map(res => res.data.token));
  }

  regenerateLocationShareLink(workspaceId: string, landId: string): Observable<string> {
    return this.http
      .post<ApiResponse<{ token: string }>>(`${this.base(workspaceId)}/${landId}/location-share-link/regenerate`, {})
      .pipe(map(res => res.data.token));
  }

  revokeLocationShareLink(workspaceId: string, landId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${landId}/location-share-link`);
  }

  getSurveys(workspaceId: string, landId: string): Observable<LandSurvey[]> {
    return this.http.get<ApiResponse<LandSurvey[]>>(`${this.base(workspaceId)}/${landId}/surveys`).pipe(map(res => res.data));
  }

  addSurvey(workspaceId: string, landId: string, request: LandSurveyRequest): Observable<LandSurvey> {
    return this.http
      .post<ApiResponse<LandSurvey>>(`${this.base(workspaceId)}/${landId}/surveys`, request)
      .pipe(map(res => res.data));
  }

  updateSurvey(workspaceId: string, landId: string, surveyId: string, request: LandSurveyRequest): Observable<LandSurvey> {
    return this.http
      .put<ApiResponse<LandSurvey>>(`${this.base(workspaceId)}/${landId}/surveys/${surveyId}`, request)
      .pipe(map(res => res.data));
  }

  deleteSurvey(workspaceId: string, landId: string, surveyId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${landId}/surveys/${surveyId}`);
  }

  getDeeds(workspaceId: string, landId: string): Observable<LandDeed[]> {
    return this.http.get<ApiResponse<LandDeed[]>>(`${this.base(workspaceId)}/${landId}/deeds`).pipe(map(res => res.data));
  }

  addDeed(workspaceId: string, landId: string, request: LandDeedRequest): Observable<LandDeed> {
    return this.http.post<ApiResponse<LandDeed>>(`${this.base(workspaceId)}/${landId}/deeds`, request).pipe(map(res => res.data));
  }

  updateDeed(workspaceId: string, landId: string, deedId: string, request: LandDeedRequest): Observable<LandDeed> {
    return this.http
      .put<ApiResponse<LandDeed>>(`${this.base(workspaceId)}/${landId}/deeds/${deedId}`, request)
      .pipe(map(res => res.data));
  }

  deleteDeed(workspaceId: string, landId: string, deedId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${landId}/deeds/${deedId}`);
  }

  getBoundaries(workspaceId: string, landId: string): Observable<LandBoundary[]> {
    return this.http.get<ApiResponse<LandBoundary[]>>(`${this.base(workspaceId)}/${landId}/boundaries`).pipe(map(res => res.data));
  }

  addBoundary(workspaceId: string, landId: string, request: LandBoundaryRequest): Observable<LandBoundary> {
    return this.http
      .post<ApiResponse<LandBoundary>>(`${this.base(workspaceId)}/${landId}/boundaries`, request)
      .pipe(map(res => res.data));
  }

  updateBoundary(workspaceId: string, landId: string, boundaryId: string, request: LandBoundaryRequest): Observable<LandBoundary> {
    return this.http
      .put<ApiResponse<LandBoundary>>(`${this.base(workspaceId)}/${landId}/boundaries/${boundaryId}`, request)
      .pipe(map(res => res.data));
  }

  deleteBoundary(workspaceId: string, landId: string, boundaryId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${landId}/boundaries/${boundaryId}`);
  }

  delete(workspaceId: string, landId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${landId}`);
  }

  listPhotos(workspaceId: string, landId: string): Observable<LandPhoto[]> {
    return this.http.get<ApiResponse<LandPhoto[]>>(`${this.base(workspaceId)}/${landId}/photos`).pipe(map(res => res.data));
  }

  uploadPhoto(workspaceId: string, landId: string, file: File): Observable<LandPhoto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ApiResponse<LandPhoto>>(`${this.base(workspaceId)}/${landId}/photos`, form).pipe(map(res => res.data));
  }

  /** Blob fetch, not a bare <img src> - the JWT rides an Authorization header the jwtInterceptor only attaches to HttpClient requests, same reasoning as DocumentService.getFileBlob. */
  getPhotoBlob(workspaceId: string, landId: string, photoId: string): Observable<Blob> {
    return this.http.get(`${this.base(workspaceId)}/${landId}/photos/${photoId}`, { responseType: 'blob' });
  }

  deletePhoto(workspaceId: string, landId: string, photoId: string): Observable<void> {
    return this.http.delete<void>(`${this.base(workspaceId)}/${landId}/photos/${photoId}`);
  }
}
