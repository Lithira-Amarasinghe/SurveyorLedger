import { Injectable } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root',
})
export class AuthGuard {
  constructor(private router: Router) {}
}

export const authGuard: CanActivateFn = (route, state) => {
  const hasToken = !!localStorage.getItem(environment.jwtTokenKey);

  if (!hasToken) {
    const router = new AuthGuard(new Router([], null, null, null, null, null, null, null, null, null));
    router.router.navigate(['/auth/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  return true;
};
