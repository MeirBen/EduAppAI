import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { Limits } from '../api/limits';
import { Auth } from './auth';

/** Transient redirect feedback; never persisted in the URL or browser history. */
export const parentAccessUnavailable = 'parent-access-unavailable';

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
      of(
        new RedirectCommand(router.createUrlTree(['/login']), {
          info: parentAccessUnavailable,
          // A failed sign-in retry can return to the login page that is already displayed.
          onSameUrlNavigation: 'reload',
        }),
      ),
    ),
  );
};
