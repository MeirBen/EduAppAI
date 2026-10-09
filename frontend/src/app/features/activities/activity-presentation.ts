import {
  ActivityDetail,
  LearningPlan,
  LengthExpectation,
  PlanChange,
  QuestionFormat,
} from '../../core/api/models';
import { DocumentForm } from './activity-document-editor/document-form';
import { lengthText } from './activity-document-view/measurements';
import { PlanForm } from './activity-workspace/plan-form';

/** Parent wording for app-owned question formats; raw enum values never reach the page. */
export const formatNames: Record<QuestionFormat, string> = {
  'numeric-input': 'תשובה מספרית',
  'text-input': 'תשובה קצרה',
  'single-choice': 'בחירה מתוך אפשרויות',
};
const mixtureNames: Record<QuestionFormat, string> = {
  'numeric-input': 'מספר',
  'text-input': 'תשובה קצרה',
  'single-choice': 'בחירה',
};
const difficultyNames = { easy: 'קל', medium: 'בינוני', hard: 'קשה' };

function questionPhrase(count: number, formats: QuestionFormat[]): string {
  const counted = count === 1 ? 'שאלה אחת' : `${count} שאלות`;
  if (formats.length !== 1)
    return `${counted} (${formats.map((f) => mixtureNames[f]).join(' + ')})`;
  if (formats[0] === 'text-input') return `${counted} עם תשובה קצרה`;
  const [one, many] =
    formats[0] === 'single-choice' ? ['אמריקאית', 'אמריקאיות'] : ['מספרית', 'מספריות'];
  return count === 1 ? `שאלה ${one} אחת` : `${count} שאלות ${many}`;
}

function lengthPhrase(length: LengthExpectation | null | undefined): string | undefined {
  return length
    ? lengthText({
        mode: length.mode,
        value: length.mode === 'target' ? length.count : null,
        lower: length.mode === 'range' ? length.lower : null,
        upper: length.mode === 'range' ? length.upper : null,
      })
    : undefined;
}

/** One-line reading of the current local plan and choices; derived on every change, never stored. */
export function activitySummary(plan: PlanForm): string[] {
  const settings = plan.settings;
  const parts = [
    settings.topic.trim(),
    settings.audience.trim(),
    difficultyNames[settings.difficulty],
  ];
  parts.push(questionPhrase(settings.questionCount, plan.questions.formats));
  if (!plan.materials.length) parts.push('ללא טקסט מקדים');
  for (const material of plan.materials) {
    parts.push(
      material.source === 'generated'
        ? (lengthPhrase(material.length) ?? material.label.trim())
        : 'טקסט משלכם',
    );
  }
  if (plan.materials.some((material) => material.source === 'generated'))
    parts.push(lengthPhrase(plan.totalLength) ?? '');
  return [...new Set(parts.filter(Boolean))];
}

/** Saved content whose diagnostics ask for the parent's review because requirements or sources changed. */
export function staleContent(saved: ActivityDetail | undefined) {
  const materials = new Set<string>(),
    questions = new Set<string>();
  for (const key of Object.keys(saved?.diagnostics ?? {})) {
    const material = /^materials\.(.+)\.stale$/.exec(key),
      question = /^questions\[(\d+)\]\.stale$/.exec(key);
    if (material) materials.add(material[1]);
    const id = question && saved!.document.questions[Number(question[1])]?.id;
    if (id) questions.add(id);
  }
  return { materials, questions };
}

/** A saved question's release problems, keyed by the form field each one concerns. */
type QuestionIssues = Partial<Record<'prompt' | 'type' | 'options' | 'answer', string>>;

/** Saved release problems that belong to one editable field, by material and question identity. */
export interface ContentIssues {
  title?: string;
  materials: Map<string, string>;
  questions: Map<string, QuestionIssues>;
}

const questionField = /^questions\[(\d+)\]\.(prompt|format|options|answer)$/;
const materialBody = /^materials\.([^.]+)\.body$/;

/**
 * The saved check's field problems in parent words. Each stays while its field in `now` still
 * holds the checked value from `then`, the saved form, so a fix hides it until the next save.
 */
export function savedContentIssues(
  saved: ActivityDetail | undefined,
  now: DocumentForm,
  then: DocumentForm,
): ContentIssues {
  const issues: ContentIssues = { materials: new Map(), questions: new Map() };
  const unchanged = (read: (form: DocumentForm) => unknown) => {
    const value = read(now);
    return value !== undefined && JSON.stringify(value) === JSON.stringify(read(then));
  };
  for (const [key, [message = '']] of Object.entries(saved?.diagnostics ?? {})) {
    const material = materialBody.exec(key)?.[1],
      question = questionField.exec(key),
      target = question && saved!.document.questions[Number(question[1])];
    if (key === 'title') {
      if (unchanged((form) => form.title)) issues.title = 'חסרה כותרת לפעילות.';
    } else if (material) {
      if (unchanged((form) => form.materials.find((m) => m.id === material)?.body))
        issues.materials.set(material, 'הטקסט ריק.');
    } else if (question && target?.id) {
      const id = target.id,
        field = question[2] === 'format' ? 'type' : (question[2] as keyof QuestionIssues);
      if (unchanged((form) => form.questions.find((q) => q.id === id)?.[field]))
        issues.questions.set(id, {
          ...issues.questions.get(id),
          [field]: questionIssue(target, field, message),
        });
    }
  }
  return issues;
}

function questionIssue(
  question: ActivityDetail['document']['questions'][number],
  field: keyof QuestionIssues,
  message: string,
) {
  if (field === 'prompt') return 'חסר נוסח לשאלה.';
  if (field === 'type') return 'סוג התשובה אינו מתאים להגדרות הפעילות.';
  if (field === 'answer' && !question.answer?.value.trim()) return 'חסרה תשובה נכונה.';
  if (field === 'answer' && question.interaction.type === 'single-choice')
    return 'התשובה הנכונה כבר אינה תואמת לאחת האפשרויות. בחרו אותה מחדש.';
  return message;
}

/** Review lines naming where a field shows a saved problem; questions are numbered as they read now. */
export function fieldPointers(
  issues: ContentIssues,
  document: DocumentForm,
  plan: LearningPlan | undefined,
): string[] {
  const label = (id: string) => plan?.materials.find((m) => m.id === id)?.label ?? 'הטקסט';
  const numbers = document.questions.flatMap((q, i) => (issues.questions.has(q.id) ? [i + 1] : []));
  const list = new Intl.ListFormat('he', { type: 'conjunction' }).format(numbers.map(String));
  return [
    ...(issues.title ? ['תקנו את המסומן בכותרת.'] : []),
    ...[...issues.materials.keys()].map((id) => `תקנו את המסומן בטקסט "${label(id)}".`),
    ...(numbers.length
      ? [`תקנו את המסומן ${numbers.length === 1 ? 'בשאלה' : 'בשאלות'} ${list}.`]
      : []),
  ];
}

/** Acceptance records the material revisions a question was written against. */
function sourceChanged(saved: ActivityDetail, indexes: number[]) {
  const materials = saved.document.materials;
  return (
    !!materials.length &&
    indexes.some((index) => {
      const acceptance = saved.document.questions[index]?.acceptance;
      return (
        !acceptance ||
        acceptance.sources.length !== materials.length ||
        acceptance.sources.some(
          (source) => materials.find((m) => m.id === source.id)?.revision !== source.revision,
        )
      );
    })
  );
}

/**
 * Parent wording for server-derived release diagnostics. Unknown keys keep the server's Hebrew
 * message, so no blocker is ever hidden. Lengths and field problems are omitted: measurements show
 * lengths beside their counts, and `savedContentIssues` places each field problem at its field.
 */
export function reviewIssues(saved: ActivityDetail | undefined): string[] {
  if (!saved) return [];
  const issues: string[] = [],
    staleQuestions: number[] = [];
  const label = (id: string) => saved.plan.materials.find((m) => m.id === id)?.label ?? 'הטקסט';
  for (const [key, [message = '']] of Object.entries(saved.diagnostics)) {
    const question = /^questions\[(\d+)\]\.(\w+)$/.exec(key);
    const material = /^materials\.([^.]+)(?:\.(\w+))?$/.exec(key);
    if (
      key === 'title' ||
      key.startsWith('length.') ||
      materialBody.test(key) ||
      questionField.test(key)
    )
      continue;
    if (question?.[2] === 'stale') staleQuestions.push(Number(question[1]));
    else if (question) issues.push(`בשאלה ${Number(question[1]) + 1}: ${message}`);
    else if (material && material[1] !== 'capacity')
      issues.push(
        material[2] === 'stale'
          ? `הטקסט "${label(material[1])}" נוצר לפי הגדרות קודמות. בדקו אותו או צרו אותו מחדש.`
          : material[2]
            ? message
            : `עדיין אין טקסט עבור "${label(material[1])}".`,
      );
    else if (key === 'questions')
      issues.push(
        saved.document.questions.length
          ? `מספר השאלות בפעילות (${saved.document.questions.length}) שונה מהמספר שנבחר (${saved.plan.settings.questionCount}).`
          : 'עדיין אין שאלות בפעילות.',
      );
    else if (key === 'questions.formats') issues.push('חסרים סוגי שאלות שנבחרו בהגדרות.');
    else issues.push(message);
  }
  if (staleQuestions.length)
    issues.push(
      sourceChanged(saved, staleQuestions)
        ? 'הטקסט השתנה מאז שנוצרו השאלות. בדקו את השאלות או צרו אותן מחדש.'
        : 'ההגדרות השתנו מאז שנוצרו השאלות. בדקו את השאלות או צרו אותן מחדש.',
    );
  return issues;
}

const planFieldNames: Record<string, string> = {
  name: 'שם הפעילות',
  goal: 'מטרת הפעילות',
  guidance: 'ההנחיות',
  settings: 'הגדרות הפעילות',
  totalLength: 'האורך הכולל',
  questions: 'הגדרות השאלות',
};
const changeKinds = { added: 'נוסף', removed: 'הוסר', moved: 'הועבר', changed: 'עודכן' };

/** Parent wording for one computed plan change, naming the affected material or field. */
export function planChangeLabel(
  change: PlanChange,
  before: LearningPlan,
  after: LearningPlan,
): string {
  if (change.path === 'plan') return 'נוספו הגדרות';
  const plan = change.kind === 'removed' ? before : after;
  const label =
    plan.materials.find((item) => item.id === change.id)?.label ??
    planFieldNames[change.path] ??
    'הגדרות הפעילות';
  return `${changeKinds[change.kind]}: ${label}`;
}
