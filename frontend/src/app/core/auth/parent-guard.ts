import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Auth } from './auth';

export const parentGuard: CanActivateFn = async () => {
  const auth = inject(Auth);
  const router = inject(Router);
  try {
    return (await auth.loadSession()) || router.createUrlTree(['/login']);
  } catch {
    return router.createUrlTree(['/login'], { queryParams: { connection: 'unavailable' } });
  }
};
