import { Routes } from '@angular/router';
import type { ActivityWorkspace } from './activity-workspace/activity-workspace';

const workspace = () =>
  import('./activity-workspace/activity-workspace').then((module) => module.ActivityWorkspace);
const leave = (component: ActivityWorkspace) => component.canLeave();

/** Direct workspace routes for the isolated composition; activation belongs to the gated cutover. */
export const activityWorkspaceRoutes: Routes = [
  {
    path: 'templates/new',
    title: 'תבנית חדשה · לומדים ביחד',
    data: { context: 'template' },
    loadComponent: workspace,
    canDeactivate: [leave],
  },
  {
    path: 'templates/:templateId/edit',
    title: 'עריכת תבנית · לומדים ביחד',
    data: { context: 'template' },
    loadComponent: workspace,
    canDeactivate: [leave],
  },
  {
    path: 'templates/:templateId/create',
    title: 'תכנון פעילות · לומדים ביחד',
    data: { context: 'activity' },
    loadComponent: workspace,
    canDeactivate: [leave],
  },
  {
    path: 'activities/new',
    title: 'פעילות חדשה · לומדים ביחד',
    data: { context: 'activity' },
    loadComponent: workspace,
    canDeactivate: [leave],
  },
  {
    path: 'activities/:activityId',
    title: 'עריכת תכנית הפעילות · לומדים ביחד',
    data: { context: 'activity' },
    loadComponent: workspace,
    canDeactivate: [leave],
  },
];
