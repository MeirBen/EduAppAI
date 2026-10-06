import { Routes } from '@angular/router';
import { childActivationGuard, childGuard, childSignOutGuard } from '../../core/auth/child-guard';
import type { ChildPlayer } from './child-player';
import { ChildShell } from './child-shell';

/** Child pages consume learner contracts only; parent routes remain outside this shell. */
export const childRoutes: Routes = [
  {
    path: '',
    component: ChildShell,
    children: [
      {
        path: 'activate',
        canMatch: [childActivationGuard],
        canActivate: [childSignOutGuard],
        runGuardsAndResolvers: 'always',
        title: 'הפעלת מכשיר · לומדים ביחד',
        loadComponent: () => import('./child-activation').then((m) => m.ChildActivation),
      },
      {
        path: 'access-unavailable',
        title: 'הפעילויות לא זמינות · לומדים ביחד',
        loadComponent: () =>
          import('./child-access-unavailable').then((m) => m.ChildAccessUnavailable),
      },
      {
        path: '',
        canMatch: [childGuard],
        children: [
          {
            path: '',
            pathMatch: 'full',
            title: 'הפעילויות שלי · לומדים ביחד',
            loadComponent: () => import('./child-inbox').then((m) => m.ChildInbox),
          },
          {
            path: 'assignments/:assignmentId',
            title: 'הפעילות שלי · לומדים ביחד',
            loadComponent: () => import('./child-player').then((m) => m.ChildPlayer),
            canDeactivate: [(component: ChildPlayer) => component.canLeave()],
          },
        ],
      },
      { path: '**', redirectTo: '' },
    ],
  },
];
