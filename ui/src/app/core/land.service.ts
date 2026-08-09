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

export interface Land {
  landId: string;
  address: Address;
  size: number | null;
  sizeUnit: string | null;
  gpsCoordinates: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface LandRequest {
  address?: Address;
  size?: number;
  sizeUnit?: string;
  gpsCoordinates?: string;
  notes?: string;
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
}
