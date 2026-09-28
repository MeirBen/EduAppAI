import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Auth } from './auth';

/**
 * Loads session state before private navigation and routes failures to sign-in.
 * This is a navigation aid; API policies and ownership queries enforce access.
 */
export const parentGuard: CanActivateFn = async () => {
  const auth = inject(Auth);
  const router = inject(Router);
  try {
    return (await auth.loadSession()) || router.createUrlTree(['/login']);
  } catch {
    return router.createUrlTree(['/login'], { queryParams: { connection: 'unavailable' } });
  }
};
