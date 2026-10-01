import { ActivityDetail, QuestionFormat } from '../../core/api/models';
import { isIntegerInput } from '../../shared/forms/integer-input';
import { InputForm, LengthForm, PlanForm } from './plan-editor/plan-form';

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

/** Formats every generated activity uses: the selected one for selectable plans, otherwise the fixed mixture. */
export function activityFormats(plan: PlanForm, input: InputForm): QuestionFormat[] {
  const allowed = (['numeric-input', 'text-input', 'single-choice'] as const).filter(
    (format) =>
      ({
        'numeric-input': plan.questions.numeric,
        'text-input': plan.questions.text,
        'single-choice': plan.questions.choice,
      })[format],
  );
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

function lengthPhrase(length: LengthForm, chosen: string): string | undefined {
  if (length.mode === 'range') return `${length.lower}–${length.upper} מילים`;
  const words = isIntegerInput(chosen) ? chosen : length.value;
  if (length.mode === 'exact') return `בדיוק ${words} מילים`;
  if (length.mode === 'target') return `כ־${words} מילים`;
  return undefined;
}

/**
 * One-line reading of the current local plan and choices. Derived on every render and never
 * persisted; unfinished typing falls back to the plan value instead of inventing a number.
 */
export function activitySummary(plan: PlanForm, input: InputForm): string[] {
  const settings = input.settings;
  const parts = [settings.topic.trim(), settings.audience.trim()];
  parts.push(difficultyNames[settings.difficulty] ?? '');
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

/** Saved content that needs the parent's explicit review because its requirements or sources changed. */
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

/**
 * Parent wording for server-derived release diagnostics. Unknown keys keep the server's Hebrew
 * message, so no blocker is ever hidden. Length keys are omitted: measurements show them beside
 * the required and actual counts.
 */
export function reviewIssues(saved: ActivityDetail | undefined): string[] {
  if (!saved) return [];
  const issues: string[] = [];
  const label = (id: string) => saved.plan.materials.find((m) => m.id === id)?.label ?? 'הטקסט';
  const staleQuestions: number[] = [];
  for (const [key, messages] of Object.entries(saved.diagnostics)) {
    const message = messages[0] ?? '';
    const question = /^questions\[(\d+)\]\.(\w+)$/.exec(key);
    const material = /^materials\.([^.]+)(?:\.(\w+))?$/.exec(key);
    if (key.startsWith('length.')) continue;
    if (question) {
      const index = Number(question[1]),
        number = index + 1,
        savedQuestion = saved.document.questions[index];
      switch (question[2]) {
        case 'stale':
          staleQuestions.push(index);
          break;
        case 'prompt':
          issues.push(`בשאלה ${number} חסר נוסח.`);
          break;
        case 'format':
          issues.push(`סוג התשובה בשאלה ${number} אינו מתאים להגדרות הפעילות.`);
          break;
        case 'answer':
          issues.push(
            !savedQuestion?.answer?.value.trim()
              ? `בשאלה ${number} חסרה תשובה נכונה.`
              : savedQuestion.interaction.type === 'single-choice'
                ? `בשאלה ${number} התשובה הנכונה כבר אינה תואמת לאחת האפשרויות. בחרו תשובה נכונה מחדש.`
                : `בשאלה ${number}: ${message}`,
          );
          break;
        default:
          issues.push(`בשאלה ${number}: ${message}`);
      }
    } else if (material && material[1] !== 'capacity') {
      const name = label(material[1]);
      issues.push(
        material[2] === 'stale'
          ? `הטקסט "${name}" נוצר לפי הגדרות קודמות. בדקו אותו או צרו אותו מחדש.`
          : material[2] === 'body'
            ? `הטקסט "${name}" ריק.`
            : !material[2]
              ? `עדיין אין טקסט עבור "${name}".`
              : message,
      );
    } else if (key === 'questions') {
      const count = saved.document.questions.length;
      issues.push(
        count
          ? `מספר השאלות בפעילות (${count}) שונה מהמספר שנבחר (${saved.input.settings.questionCount}).`
          : 'עדיין אין שאלות בפעילות.',
      );
    } else if (key === 'questions.formats') issues.push('חסרים סוגי שאלות שנבחרו בהגדרות.');
    else if (key === 'title') issues.push('חסרה כותרת לפעילות.');
    else issues.push(message);
  }
  if (staleQuestions.length) {
    // Acceptance records the material revisions a question was written against.
    const sourceChanged = staleQuestions.some((index) => {
      const acceptance = saved.document.questions[index]?.acceptance;
      return (
        !acceptance ||
        acceptance.sources.length !== saved.document.materials.length ||
        acceptance.sources.some(
          (source) =>
            saved.document.materials.find((m) => m.id === source.id)?.revision !== source.revision,
        )
      );
    });
    issues.push(
      sourceChanged && saved.document.materials.length
        ? 'הטקסט השתנה מאז שנוצרו השאלות. בדקו את השאלות או צרו אותן מחדש.'
        : 'ההגדרות השתנו מאז שנוצרו השאלות. בדקו את השאלות או צרו אותן מחדש.',
    );
  }
  return issues;
}
