import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { environment } from '../../environments/environment';

export const guestGuard: CanActivateFn = () => {
  const router = inject(Router);
  const hasToken = !!localStorage.getItem(environment.jwtTokenKey);

  if (hasToken) {
    router.navigate(['/app/dashboard']);
    return false;
  }

  return true;
};
