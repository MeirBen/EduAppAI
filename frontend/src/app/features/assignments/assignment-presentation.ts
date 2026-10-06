import { AssignmentStatus } from '../../core/api/assignment-models';

export const assignmentStatuses: Record<AssignmentStatus, string> = {
  assigned: 'זמינה לעבודה',
  withdrawn: 'ההקצאה בוטלה',
  'awaiting-review': 'ממתינה לבדיקה',
  completed: 'הושלמה',
};
