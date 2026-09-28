import { Routes } from '@angular/router';
import { parentGuard } from './core/auth/parent-guard';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · Family Learning',
    loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivateChild: [parentGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'templates' },
      {
        path: 'templates',
        title: 'Your learning space · Family Learning',
        loadComponent: () =>
          import('./features/templates/template-list/template-list').then((m) => m.TemplateList),
      },
      {
        path: 'templates/new',
        title: 'Create a template · Family Learning',
        loadComponent: () =>
          import('./features/templates/create-template/create-template').then(
            (m) => m.CreateTemplate,
          ),
      },
      {
        path: 'templates/:templateId/create',
        title: 'Create a task · Family Learning',
        loadComponent: () =>
          import('./features/instances/create-instance/create-instance').then(
            (m) => m.CreateInstance,
          ),
      },
      {
        path: 'instances/:instanceId',
        title: 'Task preview · Family Learning',
        loadComponent: () =>
          import('./features/instances/instance-preview/instance-preview').then(
            (m) => m.InstancePreviewPage,
          ),
      },
    ],
  },
  { path: '**', redirectTo: 'templates' },
];
