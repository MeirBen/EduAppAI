import { LearningPlan } from '../../core/api/models';

/** Canonical synthetic plans for isolated tests; never seeded into the application. */
export const numericPlan: LearningPlan = {
  schemaVersion: 2,
  name: 'מספרים',
  goal: 'תרגול חשבון',
  guidance: '',
  settings: { topic: 'חשבון', audience: 'כיתה ג', difficulty: 'medium', questionCount: 2 },
  materials: [],

  questions: {
    formats: ['numeric-input'],

    choiceCount: null,
    guidance: '',
  },
  totalLength: null,
};
export const sourceText = '"שָׁלוֹם" — Hello!\nDon\'t change בעלי־חיים.\n';
export const suppliedPlan: LearningPlan = {
  ...numericPlan,
  materials: [
    {
      id: '11111111111111111111111111111111',
      label: 'מקור',
      source: 'supplied',
      guidance: '',
      text: sourceText,
      length: null,
    },
  ],
};
/** A common generated reading activity: approximate length and one concrete format. */
export const readingPlan: LearningPlan = {
  ...numericPlan,
  name: 'קריאה',
  goal: 'הבנת הנקרא',
  settings: { topic: 'דינוזאורים', audience: 'כיתה ג׳', difficulty: 'medium', questionCount: 5 },
  materials: [
    {
      id: '22222222222222222222222222222222',
      label: 'קטע קריאה',
      source: 'generated',
      guidance: '',
      text: null,
      length: { mode: 'target', count: 300 },
    },
  ],
  questions: {
    ...numericPlan.questions,
    formats: ['single-choice'],

    choiceCount: 4,
  },
};
