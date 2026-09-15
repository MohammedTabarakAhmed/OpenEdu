import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { landingPathFor } from './auth.models';
import { SessionService } from './session.service';

/**
 * SDD 17.3: route protection reflecting the user's roles and permissions. A usability
 * measure only; authorisation is enforced server-side on every request (SEC-13).
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);

  return session.isAuthenticated()
    ? true
    : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export function roleGuard(...roles: readonly string[]): CanActivateFn {
  return (_route, state) => {
    const session = inject(SessionService);
    const router = inject(Router);

    if (!session.isAuthenticated()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    return session.hasAnyRole(roles) ? true : router.createUrlTree([landingPathFor(session.principal()?.roles ?? [])]);
  };
}

export function permissionGuard(code: string): CanActivateFn {
  return (_route, state) => {
    const session = inject(SessionService);
    const router = inject(Router);

    if (!session.isAuthenticated()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    return session.hasPermission(code) ? true : router.createUrlTree([landingPathFor(session.principal()?.roles ?? [])]);
  };
}

/** Sends an authenticated user to their role shell and an anonymous one to authentication. */
export const landingRedirectGuard: CanActivateFn = () => {
  const session = inject(SessionService);
  const router = inject(Router);

  return router.createUrlTree([session.isAuthenticated() ? landingPathFor(session.principal()?.roles ?? []) : '/login']);
};

/** Keeps an authenticated user away from the login page. */
export const anonymousOnlyGuard: CanActivateFn = () => {
  const session = inject(SessionService);
  const router = inject(Router);

  return session.isAuthenticated() ? router.createUrlTree([landingPathFor(session.principal()?.roles ?? [])]) : true;
};
