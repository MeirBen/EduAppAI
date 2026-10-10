import { inject } from '@angular/core';
import { CanMatchFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { Auth } from './auth';
import { ChildAuth } from './child-auth';
import { DeviceEntry } from './device-entry';

/** Restores the device's entry screen; its destination guard still verifies access on every visit. */
export const appEntryGuard: CanMatchFn = () => {
  const router = inject(Router);
  const entry = inject(DeviceEntry).read();
  const childHome = new RedirectCommand(router.parseUrl('/child'), { replaceUrl: true });
  const parentHome = new RedirectCommand(router.parseUrl('/activities/new'), { replaceUrl: true });
  if (entry) return entry === 'child' ? childHome : parentHome;

  // Existing installations can have a valid cookie before their first remembered entry.
  const auth = inject(Auth);
  return inject(ChildAuth)
    .loadSession()
    .pipe(
      switchMap((identity) =>
        identity
          ? of(childHome)
          : auth.loadSession().pipe(map((signedIn) => (signedIn ? parentHome : true))),
      ),
      catchError(() =>
        of(new RedirectCommand(router.parseUrl('/access-unavailable'), { replaceUrl: true })),
      ),
    );
};
