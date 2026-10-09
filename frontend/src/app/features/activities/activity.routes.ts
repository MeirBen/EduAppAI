import { Route, Routes } from '@angular/router';
import type { ActivityWorkspace } from './activity-workspace/activity-workspace';

const route = (path: string, title: string): Route => ({
  path,
  title: `${title} · לומדים ביחד`,
  loadComponent: () =>
    import('./activity-workspace/activity-workspace').then((module) => module.ActivityWorkspace),
  canDeactivate: [(component: ActivityWorkspace) => component.canLeave()],
});

/** Activity drafts share one workspace and its unsaved-change guard. */
export const activityWorkspaceRoutes: Routes = [
  route('activities/new', 'פעילות חדשה'),
  route('activities/:activityId', 'טיוטת פעילות'),
];
