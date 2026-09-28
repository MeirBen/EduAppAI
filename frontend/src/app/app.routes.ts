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
        loadComponent: () => import('./features/library/library').then((m) => m.Library),
      },
      {
        path: 'templates/new',
        title: 'תבנית חדשה · לומדים ביחד',
        loadComponent: () =>
          import('./features/templates/template-editor/template-editor').then(
            (m) => m.TemplateEditor,
          ),
      },
      {
        path: 'templates/:templateId/edit',
        title: 'עריכת תבנית · לומדים ביחד',
        loadComponent: () =>
          import('./features/templates/template-editor/template-editor').then(
            (m) => m.TemplateEditor,
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
