import { ActivityDetail } from '../../core/api/models';
import { activitySummary, reviewIssues, staleContent } from './activity-presentation';
import { numericPlan, readingPlan } from './learning-plan.fixture';
import { inputForm, planForm } from './plan-editor/plan-form';

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
  input: { settings: readingPlan.defaults },
  document: { title: 'פעילות', instructions: null, materials: [], questions: [] },
  diagnostics: {},
  measurements: [],
  activeOperationId: null,
  templateVersionId: null,
  releasedSnapshotId: null,
  releasedSourceRevision: null,
  createdAtUtc: '2026-10-01T00:00:00Z',
  updatedAtUtc: '2026-10-01T00:00:00Z',
  ...overrides,
});

describe('Parent-facing activity presentation', () => {
  it('summarizes a reading plan from the current choices without internal vocabulary', () => {
    const plan = planForm(readingPlan),
      input = inputForm(readingPlan);
    expect(activitySummary(plan, input)).toEqual([
      'דינוזאורים',
      'כיתה ג׳',
      'בינוני',
      '5 שאלות אמריקאיות',
      'כ־300 מילים',
    ]);
    input.materials[0].wordCount = '450';
    input.questionFormat = 'text-input';
    expect(activitySummary(plan, input)).toContain('כ־450 מילים');
    expect(activitySummary(plan, input)).toContain('5 שאלות עם תשובה קצרה');
    // Unfinished typing falls back to the plan value instead of an invented number.
    input.materials[0].wordCount = '4.';
    expect(activitySummary(plan, input)).toContain('כ־300 מילים');
  });

  it('summarizes question-only and mixed plans', () => {
    const plan = planForm({
      ...numericPlan,
      questions: { ...numericPlan.questions, formats: ['numeric-input', 'text-input'] },
    });
    const summary = activitySummary(plan, inputForm(numericPlan));
    expect(summary).toContain('ללא טקסט מקדים');
    expect(summary).toContain('2 שאלות (מספר + תשובה קצרה)');
    expect(activitySummary(planForm(numericPlan), inputForm(numericPlan))).toContain(
      '2 שאלות מספריות',
    );
  });

  it('translates release diagnostics into actionable parent copy without hiding unknown blockers', () => {
    const saved = draft({
      document: {
        title: 'פעילות',
        instructions: null,
        materials: [],
        questions: [question('q1', ['א', 'ב'], 'ג'), question('q2', null, '')],
      },
      diagnostics: {
        'questions[0].answer': ['התשובה הנכונה חייבת להיות אחת מהאפשרויות.'],
        'questions[1].answer': ['יש להזין תשובה באורך של 1 עד 200 תווים.'],
        'length.22222222222222222222222222222222': ['אורך החומר אינו עומד בדרישה.'],
        'materials.capacity': ['לא נשאר מספיק מקום לחומרים ולשאלות המבוקשות.'],
      },
    });
    expect(reviewIssues(saved)).toEqual([
      'בשאלה 1 התשובה הנכונה כבר אינה תואמת לאחת האפשרויות. בחרו תשובה נכונה מחדש.',
      'בשאלה 2 חסרה תשובה נכונה.',
      'לא נשאר מספיק מקום לחומרים ולשאלות המבוקשות.',
    ]);
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
