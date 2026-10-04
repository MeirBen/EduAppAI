import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Auth } from './auth';

/** Explicit sign-out intent for one navigation; never stored in the URL or browser history. */
export const parentSignOut = 'parent-sign-out';

/**
 * Runs after outgoing canDeactivate guards, so cancelling unsaved work preserves the session.
 * A failed sign-out rejects navigation and leaves the current page open for the shell's feedback.
 */
export const signOutGuard: CanActivateFn = () =>
  inject(Router).currentNavigation()?.extras.info === parentSignOut
    ? inject(Auth)
        .logout()
        .then(() => true)
    : true;
