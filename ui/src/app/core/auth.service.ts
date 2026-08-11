import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, throwError } from 'rxjs';
import { catchError, finalize, map, shareReplay, tap } from 'rxjs/operators';
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

  /** Shared across concurrent 401s so only one refresh ever runs at a time - see refreshToken(). */
  private refreshInFlight: Observable<AuthResponse> | null = null;

  isAuthenticated$ = this.isAuthenticatedSubject.asObservable();

  constructor(private http: HttpClient) {}

  register(email: string, password: string, confirmPassword: string, firstName: string, lastName: string): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/register`, {
      email,
      password,
      confirmPassword,
      firstName,
      lastName,
    }).pipe(map(() => undefined));
  }

  resendOtp(email: string): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/resend-otp`, { email }).pipe(map(() => undefined));
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

  /** Always succeeds from the caller's point of view - the API deliberately doesn't reveal whether the email exists. */
  forgotPassword(email: string): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/forgot-password`, { email }).pipe(map(() => undefined));
  }

  resetPassword(email: string, otpCode: string, newPassword: string): Observable<void> {
    return this.http
      .post<ApiResponse<unknown>>(`${this.apiUrl}/reset-password`, { email, otpCode, newPassword })
      .pipe(map(() => undefined));
  }

  verifyOtp(email: string, otpCode: string): Observable<void> {
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/verify-otp`, { email, otpCode }).pipe(
      map(() => undefined)
    );
  }

  /**
   * Exchanges the stored refresh token for a fresh access token. Concurrent callers share
   * one in-flight request: the server rotates the refresh token and treats a replayed one
   * as theft (revoking every session), so firing several refreshes at once would log the
   * user out entirely. Every 401 handler must go through here, never call the endpoint directly.
   */
  refreshToken(): Observable<AuthResponse> {
    if (this.refreshInFlight) return this.refreshInFlight;

    const refreshToken = localStorage.getItem(environment.refreshTokenKey);
    if (!refreshToken) return throwError(() => new Error('No refresh token'));

    this.refreshInFlight = this.http
      .post<ApiResponse<AuthResponse>>(`${this.apiUrl}/refresh-token`, { refreshToken })
      .pipe(
        map(res => res.data),
        tap(response => this.setToken(response.accessToken, response.refreshToken)),
        catchError(error => {
          this.clearSession();
          return throwError(() => error);
        }),
        finalize(() => (this.refreshInFlight = null)),
        shareReplay(1)
      );

    return this.refreshInFlight;
  }

  logout(): void {
    // Best-effort server-side revoke so the refresh token dies with the session rather
    // than staying valid for its full lifetime. Local state is cleared either way.
    const refreshToken = localStorage.getItem(environment.refreshTokenKey);
    if (refreshToken) {
      this.http.post(`${this.apiUrl}/logout`, { refreshToken }).subscribe({ error: () => {} });
    }
    this.clearSession();
  }

  /** Drops local session state without calling the server - used when the session is already dead. */
  clearSession(): void {
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
    return this.decodedClaim(['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress', 'email']);
  }

  /** Stable identity for the caller - use this over email, which PID-only members lack. */
  getCurrentUserId(): string | null {
    return this.decodedClaim(['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', 'nameid', 'sub']);
  }

  private decodedClaim(keys: string[]): string | null {
    const token = localStorage.getItem(environment.jwtTokenKey);
    if (!token) return null;

    try {
      const payload = token.split('.')[1];
      const decoded = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
      for (const key of keys) {
        if (decoded[key]) return decoded[key];
      }
      return null;
    } catch {
      return null;
    }
  }
}
