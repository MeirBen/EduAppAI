import { Routes } from '@angular/router';
import { parentGuard } from './core/auth/parent-guard';

/** Lazy parent routes; route parameter names match component inputs. */
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
      { path: '', pathMatch: 'full', redirectTo: 'templates' },
      {
        path: 'templates',
        title: 'המרחב שלנו · לומדים ביחד',
        loadComponent: () =>
          import('./features/templates/template-list/template-list').then((m) => m.TemplateList),
      },
      {
        path: 'templates/new',
        title: 'תבנית חדשה · לומדים ביחד',
        loadComponent: () =>
          import('./features/templates/create-template/create-template').then(
            (m) => m.CreateTemplate,
          ),
      },
      {
        path: 'templates/:templateId/create',
        title: 'יצירת תרגול · לומדים ביחד',
        loadComponent: () =>
          import('./features/instances/create-instance/create-instance').then(
            (m) => m.CreateInstance,
          ),
      },
      {
        path: 'instances/:instanceId',
        title: 'תצוגת תרגול · לומדים ביחד',
        loadComponent: () =>
          import('./features/instances/instance-preview/instance-preview').then(
            (m) => m.InstancePreviewPage,
          ),
      },
    ],
  },
  { path: '**', redirectTo: 'templates' },
];
