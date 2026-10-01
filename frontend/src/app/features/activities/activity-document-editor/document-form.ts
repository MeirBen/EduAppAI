import { applyEach, maxLength, schema, validate } from '@angular/forms/signals';
import { ActivityDocument, EditableActivity, QuestionFormat } from '../../../core/api/models';
import { isIntegerInput } from '../../../shared/forms/integer-input';
import { Projection } from '../plan-editor/plan-form';

/** Editable-only values. Blank/invalid numeric keystrokes never become an accidental zero. */
export interface DocumentForm {
  title: string;
  instructions: string;
  materials: { id: string; title: string; body: string }[];
  questions: {
    id: string;
    /** Stable local identity for unsaved questions; never sent to the server. */
    key: string;
    prompt: string;
    type: QuestionFormat;
    options: { value: string }[];
    answer: string;
    points: string;
  }[];
}
export const documentSchema = schema<DocumentForm>((path) => {
  maxLength(path.title, 100);
  maxLength(path.instructions, 1000);
  applyEach(path.materials, (material) => {
    maxLength(material.title, 100);
    maxLength(material.body, 4000);
  });
  applyEach(path.questions, (question) => {
    maxLength(question.prompt, 500);
    maxLength(question.answer, 200);
    applyEach(question.options, (option) => maxLength(option.value, 200));
    validate(question.points, ({ value }) =>
      validPoints(value()) ? [] : [{ kind: 'points', message: 'יש להזין מספר שלם בין 0 ל־100.' }],
    );
  });
});
export function documentForm(document?: EditableActivity | ActivityDocument): DocumentForm {
  return {
    title: document?.title ?? '',
    instructions: document?.instructions ?? '',
    materials:
      document?.materials.map((m) => ({ id: m.id, title: m.title ?? '', body: m.body })) ?? [],
    questions:
      document?.questions.map((q) => ({
        id: q.id ?? '',
        key: q.id ?? crypto.randomUUID(),
        prompt: q.prompt,
        type: q.interaction.type,
        options: q.interaction.options?.map((value) => ({ value })) ?? [],
        answer: q.answer?.value ?? '',
        points: String(q.points),
      })) ?? [],
  };
}
const validPoints = (value: string) =>
  isIntegerInput(value) && Number(value) >= 0 && Number(value) <= 100;
/** Lenient draft projection: structural bounds only; incomplete answers stay repairable server diagnostics. */
export function documentValue(raw: DocumentForm): Projection<EditableActivity> {
  const errors: string[] = [];
  let total = 0;
  const check = (value: string, max: number) => {
    total += value.length;
    if (value.length > max) errors.push(`שדה תוכן ארוך מדי (עד ${max} תווים).`);
  };
  check(raw.title, 100);
  check(raw.instructions, 1000);
  for (const m of raw.materials) {
    check(m.title, 100);
    check(m.body, 4000);
  }
  for (const q of raw.questions) {
    check(q.prompt, 500);
    check(q.answer, 200);
    if (!validPoints(q.points)) errors.push('נקודות: יש להזין מספר שלם בין 0 ל־100.');
    if (q.type === 'single-choice') {
      if (q.options.length > 6) errors.push('אפשר להזין עד שש אפשרויות.');
      for (const o of q.options) check(o.value, 200);
    }
  }
  if (total > 8000 || raw.materials.length > 4 || raw.questions.length > 3999)
    errors.push('תוכן הפעילות חורג מהמגבלה: עד 8,000 תווים, ארבעה חומרים ו־3,999 שאלות.');
  return {
    errors,
    value: errors.length
      ? undefined
      : {
          title: raw.title,
          instructions: raw.instructions || null,
          materials: raw.materials.map((m) => ({ id: m.id, title: m.title || null, body: m.body })),
          questions: raw.questions.map((q) => ({
            id: q.id || null,
            prompt: q.prompt,
            interaction: {
              type: q.type,
              options: q.type === 'single-choice' ? q.options.map((o) => o.value) : null,
            },
            answer: { value: q.answer },
            points: Number(q.points),
          })),
        },
  };
}
