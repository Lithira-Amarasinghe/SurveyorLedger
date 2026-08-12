import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { LandLocation } from './land.service';

export interface LandLocationLinkPreview {
  addressLine: string;
  latitude: number | null;
  longitude: number | null;
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

  setLocation(token: string, location: LandLocation): Observable<void> {
    return this.http.put<void>(this.base(token), { latitude: location.lat, longitude: location.lng });
  }
}
