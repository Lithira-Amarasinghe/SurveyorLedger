import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import { CurrentWorkspaceService } from './current-workspace.service';

/** Auth endpoints must never trigger a refresh-and-retry - a failed login is a real 401. */
function isAuthEndpoint(req: HttpRequest<unknown>): boolean {
  return req.url.includes('/auth/');
}

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const authService = inject(AuthService);
  const currentWorkspace = inject(CurrentWorkspaceService);

  const withAuth = (request: HttpRequest<unknown>, token: string | null) => {
    let authReq = request;
    if (token) {
      authReq = authReq.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
    }
    const workspaceId = currentWorkspace.current()?.workspaceId;
    if (workspaceId) {
      authReq = authReq.clone({ setHeaders: { 'X-Workspace-Id': workspaceId } });
    }
    return authReq;
  };

  return next(withAuth(req, localStorage.getItem(environment.jwtTokenKey))).pipe(
    catchError((error: HttpErrorResponse) => {
      // Access tokens are short-lived by design, so a 401 usually just means "expired" -
      // recover by refreshing and replaying the request instead of dumping the user at
      // the login screen. Only give up if the refresh itself fails.
      if (error.status !== 401 || isAuthEndpoint(req)) {
        return throwError(() => error);
      }

      return authService.refreshToken().pipe(
        switchMap(response => next(withAuth(req, response.accessToken))),
        catchError(refreshError => {
          authService.clearSession();
          router.navigate(['/auth/login'], { queryParams: { returnUrl: router.url } });
          return throwError(() => refreshError);
        })
      );
    })
  );
};
