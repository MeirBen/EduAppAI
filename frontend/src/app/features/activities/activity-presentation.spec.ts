import { ActivityDetail } from '../../core/api/models';
import { documentForm } from './activity-document-editor/document-form';
import {
  activitySummary,
  fieldPointers,
  reviewIssues,
  savedContentIssues,
  staleContent,
} from './activity-presentation';
import { numericPlan, readingPlan } from './learning-plan.fixture';
import { planForm } from './plan-editor/plan-form';

const question = (id: string, options: string[] | null, answer: string) => ({
  id,
  prompt: 'שאלה',
  interaction: { type: options ? ('single-choice' as const) : ('numeric-input' as const), options },
  answer: { value: answer },
  points: 1,
  origin: { kind: 'generated' },
  acceptance: { inputFingerprint: 'f'.repeat(64), sources: [{ id: 'm', revision: 1 }] },
});
const draft = (overrides: Partial<ActivityDetail>): ActivityDetail => ({
  id: 'draft',
  revision: 2,
  plan: readingPlan,

  document: { title: 'פעילות', instructions: null, materials: [], questions: [] },
  diagnostics: {},
  measurements: [],
  activeOperationId: null,
  templateVersionId: null,
  releasedSnapshotId: null,
  releasedSourceRevision: null,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: '2026-10-01T00:00:00Z',
  chat: [],
  canUndo: false,
  ...overrides,
});

describe('Parent-facing activity presentation', () => {
  it('summarizes a reading plan from the current choices without internal vocabulary', () => {
    const plan = planForm(readingPlan);
    expect(activitySummary(plan)).toEqual([
      'דינוזאורים',
      'כיתה ג׳',
      'בינוני',
      '5 שאלות אמריקאיות',
      'בערך 300 מילים',
    ]);
    plan.materials[0].length.value = '450';
    plan.questions.choice = false;
    plan.questions.text = true;
    expect(activitySummary(plan)).toContain('בערך 450 מילים');
    expect(activitySummary(plan)).toContain('5 שאלות עם תשובה קצרה');
  });

  it('summarizes question-only and mixed plans', () => {
    const plan = planForm({
      ...numericPlan,
      questions: { ...numericPlan.questions, formats: ['numeric-input', 'text-input'] },
    });
    const summary = activitySummary(plan);
    expect(summary).toContain('ללא טקסט מקדים');
    expect(summary).toContain('2 שאלות (מספר + תשובה קצרה)');
    expect(activitySummary(planForm(numericPlan))).toContain('2 שאלות מספריות');
  });

  it('places each field problem at its field until it changes and points the review there', () => {
    const material = readingPlan.materials[0];
    const saved = draft({
      document: {
        title: '',
        instructions: null,
        materials: [
          {
            id: material.id,
            title: null,
            body: '',
            revision: 1,
            origin: { kind: 'generated' },
            acceptance: null,
          },
        ],
        questions: [question('q1', ['א', 'ב'], 'ג'), question('q2', null, '')],
      },
      diagnostics: {
        title: ['יש למלא תוכן בשדה הזה.'],
        [`materials.${material.id}.body`]: ['יש למלא תוכן בשדה הזה.'],
        'questions[0].answer': ['התשובה הנכונה חייבת להיות אחת מהאפשרויות.'],
        'questions[1].answer': ['יש להזין תשובה באורך של 1 עד 200 תווים.'],
        'length.22222222222222222222222222222222': ['אורך הטקסט אינו עומד בדרישה.'],
        'materials.capacity': ['אין מספיק מקום לטקסטים ולשאלות שהתבקשו.'],
      },
    });
    const form = documentForm(saved.document);
    const issues = savedContentIssues(saved, form, form);
    expect(issues).toEqual({
      title: 'חסרה כותרת לפעילות.',
      materials: new Map([[material.id, 'הטקסט ריק.']]),
      questions: new Map([
        ['q1', { answer: 'התשובה הנכונה כבר אינה תואמת לאחת האפשרויות. בחרו אותה מחדש.' }],
        ['q2', { answer: 'חסרה תשובה נכונה.' }],
      ]),
    });
    expect(reviewIssues(saved)).toEqual(['אין מספיק מקום לטקסטים ולשאלות שהתבקשו.']);
    expect(fieldPointers(issues, form, saved.plan)).toEqual([
      'תקנו את המסומן בכותרת.',
      `תקנו את המסומן בטקסט "${material.label}".`,
      'תקנו את המסומן בשאלות 1 ו-2.',
    ]);
    expect(savedContentIssues(saved, { ...form, title: 'פעילות' }, form).title).toBeUndefined();
  });

  it('maps stale diagnostics to saved identities and explains a changed source text', () => {
    const material = {
      id: 'm',
      title: null,
      body: 'טקסט שנערך',
      revision: 2,
      origin: { kind: 'generated' },
      acceptance: null,
    };
    const saved = draft({
      document: {
        title: 'פעילות',
        instructions: null,
        materials: [material],
        questions: [question('q1', null, '2'), question('q2', null, '3')],
      },
      diagnostics: { 'questions[1].stale': ['השאלה דורשת יצירה מחדש או אימוץ.'] },
    });
    expect([...staleContent(saved).questions]).toEqual(['q2']);
    expect(reviewIssues(saved)).toEqual([
      'הטקסט השתנה מאז שנוצרו השאלות. בדקו את השאלות או צרו אותן מחדש.',
    ]);
    const settingsOnly = draft({
      ...saved,
      document: { ...saved.document, materials: [{ ...material, revision: 1 }] },
    });
    expect(reviewIssues(settingsOnly)).toEqual([
      'ההגדרות השתנו מאז שנוצרו השאלות. בדקו את השאלות או צרו אותן מחדש.',
    ]);
  });
});
