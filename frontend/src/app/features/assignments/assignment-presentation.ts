import { AssignmentSummary, AssignmentView } from '../../core/api/assignment-models';

/**
 * How the parent's pages show an assignment's state: its badge label and tone. Work awaiting the
 * parent's grading asks for attention; finished and withdrawn work stay calm.
 */
export function assignmentState({ status, hasStarted }: AssignmentSummary) {
  switch (status) {
    case 'assigned':
      return { label: hasStarted ? 'בעבודה' : 'עוד לא התחילה', tone: null };
    case 'awaiting-review':
      return { label: 'מחכה לבדיקה', tone: 'new' };
    case 'completed':
      return { label: 'הושלמה', tone: 'done' };
    case 'withdrawn':
      return { label: 'בוטלה', tone: 'quiet' };
  }
}

/** The parent's list views; open work waits for the child or for the parent's grading. */
export const assignmentViews: { value: AssignmentView; label: string; empty: string }[] = [
  {
    value: 'open',
    label: 'פתוחות',
    empty: 'אין כרגע פעילויות פתוחות. פעילות שתקצו תופיע כאן עד שתושלם.',
  },
  { value: 'completed', label: 'הושלמו', empty: 'עוד אין פעילויות שהושלמו.' },
  { value: 'withdrawn', label: 'בוטלו', empty: 'אין הקצאות שבוטלו.' },
];
