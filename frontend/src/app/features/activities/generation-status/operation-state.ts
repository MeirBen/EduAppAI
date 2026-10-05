import { GenerationOperation } from '../../../core/api/models';

/** Semantic names for operation stages, shared by status evidence and result inspection. */
export const stageNames: Record<string, string> = {
  'material-ideas': 'רעיונות לטקסט',
  materials: 'טקסט שנוצר',
  questions: 'שאלות שנוצרו',
  'replace-material': 'טקסט חלופי',
  'replace-question': 'שאלה חלופית',
};

/** Whether the operation can still change the draft; every other status is terminal. */
export const isRunning = (operation: GenerationOperation) =>
  operation.status === 'queued' || operation.status === 'calling';
