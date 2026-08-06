import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { environment } from '../../environments/environment';

export const authGuard: CanActivateFn = (route, state) => {
  const router = inject(Router);
  const hasToken = !!localStorage.getItem(environment.jwtTokenKey);

  if (!hasToken) {
    router.navigate(['/auth/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  return true;
};
