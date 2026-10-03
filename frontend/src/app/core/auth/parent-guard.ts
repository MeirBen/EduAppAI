import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { Limits } from '../api/limits';
import { Auth } from './auth';

/**
 * Checks the session, then loads the server's content limits for private pages; superseded
 * navigation cancels these reads. This is a navigation aid; API policies enforce access.
 */
export const parentGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  const limits = inject(Limits);
  const router = inject(Router);
  return auth.loadSession().pipe(
    switchMap((signedIn) =>
      signedIn ? limits.load().pipe(map(() => true)) : of(router.createUrlTree(['/login'])),
    ),
    catchError(() =>
      of(router.createUrlTree(['/login'], { queryParams: { connection: 'unavailable' } })),
    ),
  );
};
