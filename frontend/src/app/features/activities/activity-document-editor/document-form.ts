import { applyEach, maxLength, schema, validate } from '@angular/forms/signals';
import { count } from '../../../core/api/limits';
import {
  ActivityDocument,
  ContentLimits,
  EditableActivity,
  QuestionFormat,
} from '../../../core/api/models';
import { isIntegerInput } from '../../../shared/forms/integer-input';
import { Projection } from '../../../shared/forms/projection';

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
/** Native field metadata and feedback; the server still validates every saved draft. */
export const documentSchema = (limits: ContentLimits) =>
  schema<DocumentForm>((path) => {
    maxLength(path.title, limits.titleLength);
    maxLength(path.instructions, limits.instructionsLength);
    applyEach(path.materials, (material) => {
      maxLength(material.title, limits.titleLength);
      maxLength(material.body, limits.bodyLength);
    });
    applyEach(path.questions, (question) => {
      maxLength(question.prompt, limits.promptLength);
      maxLength(question.answer, limits.answerLength);
      applyEach(question.options, (option) => maxLength(option.value, limits.answerLength));
      validate(question.points, ({ value }) =>
        validPoints(value(), limits) ? [] : [{ kind: 'points', message: pointsError(limits) }],
      );
    });
  });
/** Editable fields of a saved or generated document; server-owned provenance stays out. */
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
const validPoints = (value: string, limits: ContentLimits) =>
  isIntegerInput(value) && Number(value) >= 0 && Number(value) <= limits.maxPoints;
const pointsError = (limits: ContentLimits) => `יש להזין מספר שלם בין 0 ל־${limits.maxPoints}.`;
/** Lenient draft projection: structural bounds only; incomplete answers stay repairable server diagnostics. */
export function documentValue(
  raw: DocumentForm,
  limits: ContentLimits,
): Projection<EditableActivity> {
  const errors: string[] = [];
  let total = 0;
  const check = (value: string, max: number) => {
    total += value.length;
    if (value.length > max) errors.push(`שדה תוכן ארוך מדי (עד ${max} תווים).`);
  };
  check(raw.title, limits.titleLength);
  check(raw.instructions, limits.instructionsLength);
  for (const m of raw.materials) {
    check(m.title, limits.titleLength);
    check(m.body, limits.bodyLength);
  }
  for (const q of raw.questions) {
    check(q.prompt, limits.promptLength);
    check(q.answer, limits.answerLength);
    if (!validPoints(q.points, limits)) errors.push(`נקודות: ${pointsError(limits)}`);
    if (q.type === 'single-choice') {
      if (q.options.length > limits.maxChoiceCount)
        errors.push(`אפשר להזין עד ${limits.maxChoiceCount} אפשרויות.`);
      for (const o of q.options) check(o.value, limits.answerLength);
    }
  }
  if (
    total > limits.contentLength ||
    raw.materials.length > limits.maxMaterials ||
    raw.questions.length > limits.maxQuestionCount
  )
    errors.push(
      `תוכן הפעילות חורג מהמגבלה: עד ${count(limits.contentLength)} תווים, ${limits.maxMaterials} טקסטים ו־${limits.maxQuestionCount} שאלות.`,
    );
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
