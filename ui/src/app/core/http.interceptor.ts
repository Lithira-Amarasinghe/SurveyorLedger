import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { CurrentWorkspaceService } from './current-workspace.service';

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const currentWorkspace = inject(CurrentWorkspaceService);
  const token = localStorage.getItem(environment.jwtTokenKey);

  let authReq = req;
  if (token) {
    authReq = authReq.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  const workspaceId = currentWorkspace.current()?.workspaceId;
  if (workspaceId) {
    authReq = authReq.clone({ setHeaders: { 'X-Workspace-Id': workspaceId } });
  }

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        localStorage.removeItem(environment.jwtTokenKey);
        localStorage.removeItem(environment.refreshTokenKey);
        router.navigate(['/auth/login'], { queryParams: { returnUrl: router.url } });
      }
      return throwError(() => error);
    })
  );
};
