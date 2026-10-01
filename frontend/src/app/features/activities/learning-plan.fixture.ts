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
    countBounds: null,
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
