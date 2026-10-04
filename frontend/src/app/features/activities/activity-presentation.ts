import { ActivityDetail, LearningPlan, PlanChange, QuestionFormat } from '../../core/api/models';
import { isIntegerInput } from '../../shared/forms/integer-input';
import { lengthText } from './activity-document-view/measurements';
import {
  formFormats,
  InputForm,
  LengthForm,
  PlanForm,
  planControls,
} from './plan-editor/plan-form';

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

/** Formats a generated activity uses: the chosen one for selectable plans, otherwise the fixed mixture. */
export function activityFormats(plan: PlanForm, input: InputForm): QuestionFormat[] {
  const allowed = formFormats(plan.questions);
  const selected = input.questionFormat || plan.questions.defaultFormat;
  return plan.questions.selectableFormat && selected && allowed.includes(selected)
    ? [selected]
    : allowed;
}

function questionPhrase(count: number, formats: QuestionFormat[]): string {
  const counted = count === 1 ? 'שאלה אחת' : `${count} שאלות`;
  if (formats.length !== 1)
    return `${counted} (${formats.map((f) => mixtureNames[f]).join(' + ')})`;
  if (formats[0] === 'text-input') return `${counted} עם תשובה קצרה`;
  const [one, many] =
    formats[0] === 'single-choice' ? ['אמריקאית', 'אמריקאיות'] : ['מספרית', 'מספריות'];
  return count === 1 ? `שאלה ${one} אחת` : `${count} שאלות ${many}`;
}

/** Unfinished typing falls back to the plan's value instead of inventing a number. */
function lengthPhrase(length: LengthForm, chosen: string): string | undefined {
  const { mode, value, lower, upper } = length;
  if (!mode) return undefined;
  return lengthText({ mode, value: isIntegerInput(chosen) ? chosen : value, lower, upper });
}

/** One-line reading of the current local plan and choices; derived on every change, never stored. */
export function activitySummary(plan: PlanForm, input: InputForm): string[] {
  const settings = input.settings;
  const parts = [
    settings.topic.trim(),
    settings.audience.trim(),
    difficultyNames[settings.difficulty],
  ];
  if (isIntegerInput(settings.questionCount) && Number(settings.questionCount) > 0)
    parts.push(questionPhrase(Number(settings.questionCount), activityFormats(plan, input)));
  if (!plan.materials.length) parts.push('ללא טקסט מקדים');
  for (const material of plan.materials) {
    const chosen = input.materials.find((value) => value.id === material.id)?.wordCount ?? '';
    parts.push(
      material.source === 'generated'
        ? (lengthPhrase(material.length, chosen) ?? material.label.trim())
        : 'טקסט משלכם',
    );
  }
  if (plan.materials.some((material) => material.source === 'generated'))
    parts.push(lengthPhrase(plan.totalLength, input.totalWordCount) ?? '');
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
export type QuestionIssues = Partial<Record<'prompt' | 'type' | 'options' | 'answer', string>>;

const questionField = /^questions\[(\d+)\]\.(prompt|format|options|answer)$/;

/** Question diagnostics in parent words by saved identity, so each shows at its own field. */
export function savedQuestionIssues(saved: ActivityDetail | undefined) {
  const issues = new Map<string, QuestionIssues>();
  for (const [key, [message = '']] of Object.entries(saved?.diagnostics ?? {})) {
    const match = questionField.exec(key);
    const question = match && saved!.document.questions[Number(match[1])];
    if (!match || !question?.id) continue;
    const field = match[2] === 'format' ? 'type' : (match[2] as keyof QuestionIssues);
    issues.set(question.id, {
      ...issues.get(question.id),
      [field]: questionIssue(question, field, message),
    });
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

/** Points the review at the questions whose fields show a release problem, numbered as shown. */
export function flaggedQuestions(numbers: number[]): string[] {
  if (!numbers.length) return [];
  const list = new Intl.ListFormat('he', { type: 'conjunction' }).format(numbers.map(String));
  return [`יש לתקן את המסומן ${numbers.length === 1 ? 'בשאלה' : 'בשאלות'} ${list}.`];
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
 * message, so no blocker is ever hidden. Length keys and question fields are omitted: measurements
 * show lengths beside their counts, and each question field shows its own problem.
 */
export function reviewIssues(saved: ActivityDetail | undefined): string[] {
  if (!saved) return [];
  const issues: string[] = [],
    staleQuestions: number[] = [];
  const label = (id: string) => saved.plan.materials.find((m) => m.id === id)?.label ?? 'הטקסט';
  for (const [key, [message = '']] of Object.entries(saved.diagnostics)) {
    const question = /^questions\[(\d+)\]\.(\w+)$/.exec(key);
    const material = /^materials\.([^.]+)(?:\.(\w+))?$/.exec(key);
    if (key.startsWith('length.') || questionField.test(key)) continue;
    if (question?.[2] === 'stale') staleQuestions.push(Number(question[1]));
    else if (question) issues.push(`בשאלה ${Number(question[1]) + 1}: ${message}`);
    else if (material && material[1] !== 'capacity')
      issues.push(
        material[2] === 'stale'
          ? `הטקסט "${label(material[1])}" נוצר לפי הגדרות קודמות. בדקו אותו או צרו אותו מחדש.`
          : material[2] === 'body'
            ? `הטקסט "${label(material[1])}" ריק.`
            : material[2]
              ? message
              : `עדיין אין טקסט עבור "${label(material[1])}".`,
      );
    else if (key === 'questions')
      issues.push(
        saved.document.questions.length
          ? `מספר השאלות בפעילות (${saved.document.questions.length}) שונה מהמספר שנבחר (${saved.input.settings.questionCount}).`
          : 'עדיין אין שאלות בפעילות.',
      );
    else if (key === 'questions.formats') issues.push('חסרים סוגי שאלות שנבחרו בהגדרות.');
    else if (key === 'title') issues.push('חסרה כותרת לפעילות.');
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
  name: 'שם התבנית',
  goal: 'מטרת הפעילות',
  guidance: 'ההנחיות',
  defaults: 'הגדרות ברירת המחדל',
  totalLength: 'האורך הכולל',
  questions: 'הגדרות השאלות',
};
const changeKinds = { added: 'נוסף', removed: 'הוסר', moved: 'הועבר', changed: 'עודכן' };

/** Parent wording for one computed plan change, naming the affected material, choice or field. */
export function planChangeLabel(
  change: PlanChange,
  before: LearningPlan,
  after: LearningPlan,
): string {
  if (change.path === 'plan') return 'נוספו הגדרות';
  const plan = change.kind === 'removed' ? before : after;
  const label =
    [...plan.materials, ...planControls(plan)].find((item) => item.id === change.id)?.label ??
    planFieldNames[change.path] ??
    'הגדרות הפעילות';
  return `${changeKinds[change.kind]}: ${label}`;
}
