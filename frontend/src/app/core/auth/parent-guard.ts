import { inject } from '@angular/core';
import { CanMatchFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { Limits } from '../api/limits';
import { Auth } from './auth';

/**
 * Checks the session, then loads the server's content limits for private pages; superseded
 * navigation cancels these reads. Matching resolves redirects before any unsaved-work warning.
 * This is a navigation aid; API policies enforce access.
 */
export const parentGuard: CanMatchFn = () => {
  const auth = inject(Auth);
  const limits = inject(Limits);
  const router = inject(Router);
  return auth.loadSession().pipe(
    switchMap((signedIn) =>
      signedIn ? limits.load().pipe(map(() => true)) : of(router.createUrlTree(['/login'])),
    ),
    catchError(() =>
      of(new RedirectCommand(router.parseUrl('/access-unavailable'), { replaceUrl: true })),
    ),
  );
};
