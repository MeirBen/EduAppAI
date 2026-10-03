import { LearningPlan } from '../../core/api/models';

/** Canonical synthetic plans for isolated tests; never seeded into the application. */
export const numericPlan: LearningPlan = {
  schemaVersion: 1,
  name: 'מספרים',
  goal: 'תרגול חשבון',
  guidance: '',
  defaults: { topic: 'חשבון', audience: 'כיתה ג', difficulty: 'medium', questionCount: 2 },
  materials: [],
  controls: [],
  questions: {
    formats: ['numeric-input'],
    selectableFormat: false,
    defaultFormat: null,
    choiceCount: null,
    guidance: '',
    controls: [],
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
      source: 'fixed',
      guidance: '',
      text: sourceText,
      length: null,
      controls: [],
    },
  ],
};
/** A common generated reading activity: approximate adjustable length and one selectable format. */
export const readingPlan: LearningPlan = {
  ...numericPlan,
  name: 'קריאה',
  goal: 'הבנת הנקרא',
  defaults: { topic: 'דינוזאורים', audience: 'כיתה ג׳', difficulty: 'medium', questionCount: 5 },
  materials: [
    {
      id: '22222222222222222222222222222222',
      label: 'קטע קריאה',
      source: 'generated',
      guidance: '',
      text: null,
      length: { mode: 'target', count: { value: 300, adjustable: true } },
      controls: [],
    },
  ],
  questions: {
    ...numericPlan.questions,
    formats: ['text-input', 'single-choice'],
    selectableFormat: true,
    defaultFormat: 'single-choice',
    choiceCount: { value: 4, adjustable: true },
  },
};
