import { applyEach, maxLength, required, schema } from '@angular/forms/signals';
import {
  ContentLimits,
  LearningPlan,
  LengthExpectation,
  PlanMaterial,
  QuestionFormat,
} from '../../../core/api/models';
import { TaskSettingsDraft, taskSettingsDraft } from '../../../shared/forms/task-settings';

/** Initialized presentation values retain blank/invalid keystrokes outside canonical HTTP records. */
export interface ChoiceForm {
  value: string;
}
export interface LengthForm extends ChoiceForm {
  mode: '' | LengthExpectation['mode'];
  lower: string;
  upper: string;
}
export interface MaterialForm {
  id: string;
  label: string;
  source: PlanMaterial['source'];
  guidance: string;
  text: string;
  length: LengthForm;
}
export interface PlanForm {
  schemaVersion: number;
  settings: TaskSettingsDraft;
  name: string;
  goal: string;
  guidance: string;
  materials: MaterialForm[];
  totalLength: LengthForm;
  questions: {
    numeric: boolean;
    text: boolean;
    choice: boolean;
    choiceCount: ChoiceForm;
    guidance: string;
  };
}
/** Native field metadata/feedback. Full canonical validation remains at the server boundary. */
export const planFormSchema = (limits: ContentLimits) =>
  schema<PlanForm>((path) => {
    required(path.name);
    maxLength(path.name, limits.nameLength);
    required(path.goal);
    maxLength(path.goal, limits.goalLength);
    maxLength(path.guidance, limits.guidanceLength);
    maxLength(path.questions.guidance, limits.scopedGuidanceLength);
    applyEach(path.materials, (material) => {
      required(material.label);
      maxLength(material.label, limits.nameLength);
      maxLength(material.guidance, limits.scopedGuidanceLength);
      maxLength(material.text, limits.bodyLength);
    });
  });

const text = (value: string | number | boolean | null | undefined) =>
  value == null ? '' : String(value);
const choiceForm = (choice?: number | null): ChoiceForm => ({
  value: text(choice),
});
const lengthForm = (length?: LengthExpectation | null): LengthForm => ({
  ...choiceForm(length?.count),
  mode: length?.mode ?? '',
  lower: text(length?.lower),
  upper: text(length?.upper),
});

/** IDs are generated only for deliberate editor additions; labels never recover identity. */
export const newPlanId = () => crypto.randomUUID().replaceAll('-', '');
export function materialForm(material?: PlanMaterial): MaterialForm {
  return {
    id: material?.id ?? newPlanId(),
    label: material?.label ?? '',
    source: material?.source ?? 'generated',
    guidance: material?.guidance ?? '',
    text: material?.text ?? '',
    length: lengthForm(material?.length),
  };
}
/** A blank plan has no schema version; the AI status or a loaded record supplies the server's version. */
export function planForm(plan?: LearningPlan): PlanForm {
  return {
    schemaVersion: plan?.schemaVersion ?? 0,
    settings: taskSettingsDraft(
      plan?.settings ?? { topic: '', audience: '', difficulty: 'medium', questionCount: 1 },
    ),
    name: plan?.name ?? '',
    goal: plan?.goal ?? '',
    guidance: plan?.guidance ?? '',
    materials: plan?.materials.map(materialForm) ?? [],
    totalLength: lengthForm(plan?.totalLength),
    questions: {
      numeric: plan?.questions.formats.includes('numeric-input') ?? true,
      text: plan?.questions.formats.includes('text-input') ?? false,
      choice: plan?.questions.formats.includes('single-choice') ?? false,
      choiceCount: choiceForm(plan?.questions.choiceCount),
      guidance: plan?.questions.guidance ?? '',
    },
  };
}

/** Formats ticked in the editable plan, in the canonical order. */
export function formFormats(questions: PlanForm['questions']): QuestionFormat[] {
  return [
    ...(questions.numeric ? (['numeric-input'] as const) : []),
    ...(questions.text ? (['text-input'] as const) : []),
    ...(questions.choice ? (['single-choice'] as const) : []),
  ];
}
