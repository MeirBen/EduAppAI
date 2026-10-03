import { Route, Routes } from '@angular/router';
import type { ActivityWorkspace } from './activity-workspace/activity-workspace';

const route = (path: string, title: string, context: 'template' | 'activity'): Route => ({
  path,
  title: `${title} · לומדים ביחד`,
  data: { context },
  loadComponent: () =>
    import('./activity-workspace/activity-workspace').then((module) => module.ActivityWorkspace),
  canDeactivate: [(component: ActivityWorkspace) => component.canLeave()],
});

/** Template and activity routes share one workspace and its unsaved-change guard. */
export const activityWorkspaceRoutes: Routes = [
  route('templates/new', 'תבנית חדשה', 'template'),
  route('templates/:templateId/edit', 'עריכת תבנית', 'template'),
  route('templates/:templateId/create', 'פעילות חדשה מתבנית', 'activity'),
  route('activities/new', 'פעילות חדשה', 'activity'),
  route('activities/:activityId', 'עריכת פעילות', 'activity'),
];
