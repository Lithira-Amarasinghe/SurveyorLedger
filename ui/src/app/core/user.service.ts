import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

// Address is the app's one address shape - reused rather than redeclared. Both services
// live in core/, so this is a flat import, not a layering dependency.
import { Address } from './land.service';

export interface UserProfile {
  userId: string;
  email: string | null;
  firstName: string;
  lastName: string;
  phone: string | null;
  address: Address | null;
  emailVerified: boolean;
  createdAt: string;
}

export interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
  phone?: string;
  address?: Partial<Address>;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class UserService {
  private apiUrl = `${environment.apiBaseUrl}/user`;

  constructor(private http: HttpClient) {}

  getProfile(): Observable<UserProfile> {
    return this.http.get<ApiResponse<UserProfile>>(`${this.apiUrl}/profile`).pipe(map(res => res.data));
  }

  updateProfile(request: UpdateProfileRequest): Observable<UserProfile> {
    return this.http.put<ApiResponse<UserProfile>>(`${this.apiUrl}/profile`, request).pipe(map(res => res.data));
  }
}
