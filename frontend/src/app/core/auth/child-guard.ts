import { inject } from '@angular/core';
import { CanActivateFn, CanMatchFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { ChildAuth } from './child-auth';

/** Explicit intent stays in navigation memory, never in a URL. */
export const childSignOut = 'child-sign-out';

/** Child checks never load the parent identity or content limits. The server still authorizes every request. */
export const childGuard: CanMatchFn = () => {
  const router = inject(Router);
  return inject(ChildAuth)
    .loadSession()
    .pipe(
      map((identity) =>
        identity
          ? true
          : new RedirectCommand(router.parseUrl('/child/activate'), { replaceUrl: true }),
      ),
      catchError(() =>
        of(new RedirectCommand(router.parseUrl('/child/access-unavailable'), { replaceUrl: true })),
      ),
    );
};
/** Match before canDeactivate so redirects ask about unsaved answers only once. */
export const childActivationGuard: CanMatchFn = () => {
  const router = inject(Router);
  if (router.currentNavigation()?.extras.info === childSignOut) return true;
  return inject(ChildAuth)
    .loadSession()
    .pipe(
      map((identity) =>
        identity ? new RedirectCommand(router.parseUrl('/child'), { replaceUrl: true }) : true,
      ),
      catchError(() =>
        of(new RedirectCommand(router.parseUrl('/child/access-unavailable'), { replaceUrl: true })),
      ),
    );
};
export const childSignOutGuard: CanActivateFn = () =>
  inject(Router).currentNavigation()?.extras.info === childSignOut
    ? inject(ChildAuth)
        .logout()
        .then(() => true)
    : true;
