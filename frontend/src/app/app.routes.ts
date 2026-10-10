import { Routes } from '@angular/router';
import { appEntryGuard } from './core/auth/app-entry-guard';
import { parentGuard } from './core/auth/parent-guard';
import { loginGuard, signOutGuard } from './core/auth/login-guard';
import { activityWorkspaceRoutes } from './features/activities/activity.routes';
import type { ChildrenPage } from './features/children/children-page';
import type { AssignmentResult } from './features/assignments/assignment-result';

/** Lazy parent routes; server authorization remains authoritative. */
export const parentRoutes: Routes = [
  {
    path: 'login',
    canMatch: [loginGuard],
    canActivate: [signOutGuard],
    // Sign-out can arrive while login is still waiting for private navigation.
    runGuardsAndResolvers: 'always',
    title: 'כניסת הורים · לומדים ביחד',
    loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
  },
  {
    path: 'access-unavailable',
    title: 'המרחב לא זמין · לומדים ביחד',
    loadComponent: () =>
      import('./features/auth/access-unavailable/access-unavailable').then(
        (m) => m.AccessUnavailable,
      ),
  },
  {
    path: '',
    canMatch: [parentGuard],
    children: [
      ...activityWorkspaceRoutes,
      {
        path: 'children',
        title: 'הילדים והמכשירים · לומדים ביחד',
        loadComponent: () =>
          import('./features/children/children-page').then((m) => m.ChildrenPage),
        canDeactivate: [(component: ChildrenPage) => component.canLeave()],
      },
      {
        path: 'assignments',
        title: 'פעילויות לילדים · לומדים ביחד',
        loadComponent: () =>
          import('./features/assignments/assignment-list').then((m) => m.AssignmentList),
      },
      {
        path: 'assignments/:assignmentId',
        title: 'הגשה ובדיקה להורים · לומדים ביחד',
        loadComponent: () =>
          import('./features/assignments/assignment-result').then((m) => m.AssignmentResult),
        canDeactivate: [(component: AssignmentResult) => component.canLeave()],
      },
      {
        path: 'instances/:instanceId',
        title: 'פעילות מוכנה · לומדים ביחד',
        loadComponent: () =>
          import('./features/instances/snapshot-preview/snapshot-preview').then(
            (m) => m.SnapshotPreviewPage,
          ),
      },
      {
        path: 'activities',
        title: 'המרחב שלנו · לומדים ביחד',
        loadComponent: () =>
          import('./features/library/activity-library/activity-library').then(
            (module) => module.ActivityLibrary,
          ),
      },
    ],
  },
  { path: '**', redirectTo: 'activities/new' },
];

/** Child routes match before the parent shell and never run parent guards. */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    // Installed apps launch here regardless of which page was open during installation.
    canMatch: [appEntryGuard],
    title: 'ברוכים הבאים · לומדים ביחד',
    loadComponent: () => import('./features/auth/app-entry').then((m) => m.AppEntry),
  },
  {
    path: 'child',
    loadChildren: () => import('./features/child/child.routes').then((m) => m.childRoutes),
  },
  {
    path: '',
    loadComponent: () => import('./features/auth/parent-shell').then((m) => m.ParentShell),
    children: parentRoutes,
  },
];
