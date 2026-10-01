import { Routes } from '@angular/router';
import { parentGuard } from './core/auth/parent-guard';
import { activityWorkspaceRoutes } from './features/activities/activity.routes';

/** Lazy parent routes; server authorization remains authoritative. */
export const routes: Routes = [
  {
    path: 'login',
    title: 'כניסת הורים · לומדים ביחד',
    loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivateChild: [parentGuard],
    children: [
      ...activityWorkspaceRoutes,
      {
        path: 'instances/:instanceId',
        title: 'פעילות מוכנה · לומדים ביחד',
        loadComponent: () =>
          import('./features/instances/snapshot-preview/snapshot-preview').then(
            (m) => m.SnapshotPreviewPage,
          ),
      },
      {
        path: 'templates',
        title: 'המרחב שלנו · לומדים ביחד',
        loadComponent: () =>
          import('./features/library/activity-library/activity-library').then(
            (module) => module.ActivityLibrary,
          ),
      },
      { path: '', pathMatch: 'full', redirectTo: 'activities/new' },
    ],
  },
  { path: '**', redirectTo: 'activities/new' },
];
