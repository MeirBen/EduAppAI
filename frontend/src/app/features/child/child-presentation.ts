import { LearnerAssignmentSummary } from '../../core/api/child-models';

/** How the child's list shows each state of their work: its badge, tile icon and badge tone. */
export function learnerState({ status, hasStarted }: LearnerAssignmentSummary) {
  if (status === 'assigned')
    return hasStarted
      ? { label: 'להמשיך', icon: 'icon-play', tone: null }
      : { label: 'חדשה', icon: 'icon-book', tone: 'new' };
  return status === 'awaiting-review'
    ? { label: 'מחכה לבדיקה', icon: 'icon-clock', tone: 'quiet' }
    : { label: 'נבדקה', icon: 'icon-check', tone: 'done' };
}
