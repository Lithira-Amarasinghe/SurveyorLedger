import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { LandMapPoint } from './land.service';

export interface LandMapViewLinkPreview {
  addressLine: string;
  points: LandMapPoint[];
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

/**
 * Read-only counterpart to LandLocationLinkService - never sends a workspace/land id or
 * auth header, and has no write endpoint at all (unlike the add-a-point link).
 */
@Injectable({ providedIn: 'root' })
export class LandMapViewLinkService {
  constructor(private http: HttpClient) {}

  getPreview(token: string): Observable<LandMapViewLinkPreview> {
    return this.http
      .get<ApiResponse<LandMapViewLinkPreview>>(`${environment.apiBaseUrl}/land-map-view-links/${token}`)
      .pipe(map(res => res.data));
  }
}
