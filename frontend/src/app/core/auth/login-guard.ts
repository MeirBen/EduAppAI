import { inject } from '@angular/core';
import { CanActivateFn, CanMatchFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { Auth } from './auth';

/** Explicit sign-out intent for one navigation; never stored in the URL or browser history. */
export const parentSignOut = 'parent-sign-out';

/**
 * Resolves login's destination before canDeactivate, so redirects ask about unsaved work once.
 * Explicit sign-out must match this route; its activation guard owns the later session mutation.
 */
export const loginGuard: CanMatchFn = () => {
  const auth = inject(Auth);
  const router = inject(Router);
  if (router.currentNavigation()?.extras.info === parentSignOut) return true;

  return auth.loadSession().pipe(
    map((signedIn) =>
      signedIn ? new RedirectCommand(router.parseUrl('/'), { replaceUrl: true }) : true,
    ),
    catchError(() =>
      of(new RedirectCommand(router.parseUrl('/access-unavailable'), { replaceUrl: true })),
    ),
  );
};

/** Signs out only after canDeactivate accepts; failures preserve the page for shell feedback. */
export const signOutGuard: CanActivateFn = () =>
  inject(Router).currentNavigation()?.extras.info === parentSignOut
    ? inject(Auth)
        .logout()
        .then(() => true)
    : true;
