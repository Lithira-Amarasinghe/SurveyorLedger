import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { LandMapPoint, LandMapPointRequest } from './land.service';

export interface LandLocationLinkPreview {
  addressLine: string;
  /** Every point already set, read-only for the recipient - they can add more but never edit/delete an existing one. */
  points: LandMapPoint[];
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

/**
 * Never sends a workspace/land id or auth header - structurally can't, mirrors
 * DocumentRequestLinkService's trust-boundary reasoning for the same public-token pattern.
 */
@Injectable({ providedIn: 'root' })
export class LandLocationLinkService {
  constructor(private http: HttpClient) {}

  private base(token: string): string {
    return `${environment.apiBaseUrl}/land-location-links/${token}`;
  }

  getPreview(token: string): Observable<LandLocationLinkPreview> {
    return this.http.get<ApiResponse<LandLocationLinkPreview>>(this.base(token)).pipe(map(res => res.data));
  }

  /** Add-only: the token can never edit or delete an existing point. */
  addPoint(token: string, request: LandMapPointRequest): Observable<LandMapPoint> {
    return this.http.post<ApiResponse<LandMapPoint>>(`${this.base(token)}/points`, request).pipe(map(res => res.data));
  }
}
