import { ContentLimits, EditableQuestion, LearningPlan } from '../../../core/api/models';
import {
  DocumentForm,
  documentForm,
  documentValue,
} from '../activity-document-editor/document-form';

const object = (value: unknown): Record<string, unknown> | undefined =>
  value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
const text = (value: unknown, max: number): value is string =>
  typeof value === 'string' && value.length <= max;
function question(value: unknown, limits: ContentLimits): EditableQuestion | undefined {
  const q = object(value),
    interaction = object(q?.['interaction']),
    answer = object(q?.['answer']);
  if (
    !q ||
    !interaction ||
    !text(q['prompt'], limits.promptLength) ||
    !text(answer?.['value'], limits.answerLength) ||
    typeof q['points'] !== 'number' ||
    !Number.isInteger(q['points']) ||
    q['points'] < 0 ||
    q['points'] > limits.maxPoints
  )
    return;
  const type = interaction['type'],
    options = interaction['options'];
  if (type !== 'numeric-input' && type !== 'text-input' && type !== 'single-choice') return;
  if (
    type === 'single-choice'
      ? !Array.isArray(options) ||
        options.length > limits.maxChoiceCount ||
        !options.every((o) => text(o, limits.answerLength))
      : options != null
  )
    return;
  return {
    id: null,
    prompt: q['prompt'],
    interaction: { type, options: type === 'single-choice' ? (options as string[]) : null },
    answer: { value: answer!['value'] as string },
    points: q['points'],
  };
}
/** Explicit recovery into editable-only fields. Shape/size checks precede mapping; Save still applies server draft validation. */
export function candidateEdit(
  stage: string,
  candidate: unknown,
  target: string | null,
  current: DocumentForm,
  plan: LearningPlan,
  limits: ContentLimits,
): DocumentForm | undefined {
  if (typeof candidate === 'string') {
    if (candidate.length > 64000) return;
    try {
      candidate = JSON.parse(candidate);
    } catch {
      return;
    }
  }
  const value = object(candidate);
  if (!value) return;
  const next = structuredClone(current);
  if (stage === 'replace-question') {
    const index = next.questions.findIndex((q) => q.id === target),
      q = question(value, limits);
    if (index < 0 || !q) return;
    next.questions[index] = documentForm({
      title: '',
      instructions: null,
      materials: [],
      questions: [{ ...q, id: target }],
    }).questions[0];
  } else if (stage === 'questions') {
    if (
      !text(value['title'], limits.titleLength) ||
      !(value['instructions'] == null || text(value['instructions'], limits.instructionsLength)) ||
      !Array.isArray(value['questions']) ||
      value['questions'].length > limits.maxQuestionCount
    )
      return;
    const questions = value['questions'].map((q) => question(q, limits));
    if (questions.some((q) => !q)) return;
    next.title = value['title'];
    next.instructions = (value['instructions'] as string) ?? '';
    next.questions = documentForm({
      title: '',
      instructions: null,
      materials: [],
      questions: questions as EditableQuestion[],
    }).questions;
  } else if (stage === 'materials' || stage === 'replace-material') {
    const candidates = stage === 'materials' ? value['materials'] : [value];
    if (!Array.isArray(candidates) || !candidates.length || candidates.length > limits.maxMaterials)
      return;
    const seen = new Set<string>();
    for (const item of candidates) {
      const material = object(item),
        id = material?.['id'];
      if (
        typeof id !== 'string' ||
        seen.has(id) ||
        !plan.materials.some((m) => m.id === id && m.source === 'generated') ||
        (stage === 'replace-material' && id !== target) ||
        !text(material?.['body'], limits.bodyLength) ||
        !(material?.['title'] == null || text(material['title'], limits.titleLength))
      )
        return;
      seen.add(id);
      const edit = {
        id,
        title: (material!['title'] as string) ?? '',
        body: material!['body'] as string,
      };
      const index = next.materials.findIndex((m) => m.id === id);
      if (index < 0) next.materials.push(edit);
      else next.materials[index] = edit;
    }
  } else return;
  return documentValue(next, limits).value ? next : undefined;
}
