import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { Auth } from './auth';

/**
 * Checks the session for private navigation; superseded navigation cancels both HTTP reads.
 * This is a navigation aid; API policies and ownership queries enforce access.
 */
export const parentGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  const router = inject(Router);
  return auth.loadSession().pipe(
    map((signedIn) => signedIn || router.createUrlTree(['/login'])),
    catchError(() =>
      of(router.createUrlTree(['/login'], { queryParams: { connection: 'unavailable' } })),
    ),
  );
};
