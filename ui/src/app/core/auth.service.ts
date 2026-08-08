import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface AuthResponse {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private apiUrl = `${environment.apiBaseUrl}/auth`;
  private isAuthenticatedSubject = new BehaviorSubject<boolean>(this.hasToken());

  isAuthenticated$ = this.isAuthenticatedSubject.asObservable();

  constructor(private http: HttpClient) {}

  register(email: string, password: string, firstName: string, lastName: string): Observable<AuthResponse> {
    return this.http.post<ApiResponse<AuthResponse>>(`${this.apiUrl}/register`, {
      email,
      password,
      firstName,
      lastName,
    }).pipe(
      map(res => res.data),
      tap(response => this.setToken(response.accessToken, response.refreshToken))
    );
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<ApiResponse<AuthResponse>>(`${this.apiUrl}/login`, {
      email,
      password,
    }).pipe(
      map(res => res.data),
      tap(response => this.setToken(response.accessToken, response.refreshToken))
    );
  }

  verifyOtp(email: string, otpCode: string): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/verify-otp`, { email, otpCode }).pipe(
      map(() => undefined)
    );
  }

  logout(): void {
    localStorage.removeItem(environment.jwtTokenKey);
    localStorage.removeItem(environment.refreshTokenKey);
    this.isAuthenticatedSubject.next(false);
  }

  private setToken(accessToken: string, refreshToken: string): void {
    localStorage.setItem(environment.jwtTokenKey, accessToken);
    localStorage.setItem(environment.refreshTokenKey, refreshToken);
    this.isAuthenticatedSubject.next(true);
  }

  private hasToken(): boolean {
    return !!localStorage.getItem(environment.jwtTokenKey);
  }

  getCurrentEmail(): string | null {
    const token = localStorage.getItem(environment.jwtTokenKey);
    if (!token) return null;

    try {
      const payload = token.split('.')[1];
      const decoded = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
      return decoded['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'] ?? decoded['email'] ?? null;
    } catch {
      return null;
    }
  }
}
