import { GenerationOperation } from '../../../core/api/models';

/** Semantic names for operation stages in technical evidence. */
export const stageNames: Record<string, string> = {
  revise: 'תכנון השינוי',
  'rewrite-material': 'עדכון טקסט',
  'append-questions': 'הוספת שאלות',
  'revise-question': 'עדכון שאלה',
  'material-ideas': 'רעיונות לטקסט',
  materials: 'טקסט שנוצר',
  'material-polish': 'טקסט בניסוח משופר',
  questions: 'שאלות שנוצרו',
  'replace-material': 'טקסט חלופי',
  'replace-question': 'שאלה חלופית',
};

/** Whether the operation can still change the draft; every other status is terminal. */
export const isRunning = (operation: GenerationOperation) =>
  operation.status === 'queued' || operation.status === 'calling';
