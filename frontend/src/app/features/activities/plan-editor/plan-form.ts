import { applyEach, maxLength, required, schema } from '@angular/forms/signals';
import {
  ActivityInput,
  ContentLimits,
  IntegerChoice,
  LearningPlan,
  LengthExpectation,
  PlanControl,
  PlanMaterial,
  QuestionFormat,
} from '../../../core/api/models';
import { TaskSettingsDraft, taskSettingsDraft } from '../../../shared/forms/task-settings';

/** Initialized presentation values retain blank/invalid keystrokes outside canonical HTTP records. */
export interface ChoiceForm {
  value: string;
  adjustable: boolean;
}
export interface LengthForm extends ChoiceForm {
  mode: '' | LengthExpectation['mode'];
  lower: string;
  upper: string;
}
export interface ControlForm {
  id: string;
  label: string;
  type: PlanControl['type'];
  meaning: string;
  required: boolean;
  hasDefault: boolean;
  defaultValue: string;
  unit: string;
  min: string;
  max: string;
  maxLength: string;
  options: { value: string; meaning: string }[];
}
export interface MaterialForm {
  id: string;
  label: string;
  source: PlanMaterial['source'];
  guidance: string;
  text: string;
  length: LengthForm;
  controls: ControlForm[];
}
export interface PlanForm {
  schemaVersion: number;
  name: string;
  goal: string;
  guidance: string;
  materials: MaterialForm[];
  controls: ControlForm[];
  totalLength: LengthForm;
  questions: {
    numeric: boolean;
    text: boolean;
    choice: boolean;
    selectableFormat: boolean;
    defaultFormat: '' | QuestionFormat;
    choiceCount: ChoiceForm;
    guidance: string;
    controls: ControlForm[];
  };
}
/** Text overrides use an explicit presence checkbox; an empty string is a real choice. */
export interface ControlInputForm {
  id: string;
  provided: boolean;
  value: string;
}
export interface InputForm {
  settings: TaskSettingsDraft;
  questionFormat: '' | QuestionFormat;
  choiceCount: string;
  totalWordCount: string;
  materials: { id: string; sourceText: string; wordCount: string }[];
  controls: ControlInputForm[];
}

const controlSchema = (limits: ContentLimits) =>
  schema<ControlForm>((path) => {
    required(path.label);
    maxLength(path.label, limits.nameLength);
    required(path.meaning);
    maxLength(path.meaning, limits.meaningLength);
    maxLength(path.unit, limits.nameLength);
    applyEach(path.options, (option) => {
      required(option.value);
      maxLength(option.value, limits.selectOptionLength);
      maxLength(option.meaning, limits.selectOptionMeaningLength);
    });
  });
/** Native field metadata/feedback. Full canonical validation remains at the server boundary. */
export const planFormSchema = (limits: ContentLimits) =>
  schema<PlanForm>((path) => {
    const controls = controlSchema(limits);
    required(path.name);
    maxLength(path.name, limits.nameLength);
    required(path.goal);
    maxLength(path.goal, limits.goalLength);
    maxLength(path.guidance, limits.guidanceLength);
    maxLength(path.questions.guidance, limits.scopedGuidanceLength);
    applyEach(path.controls, controls);
    applyEach(path.questions.controls, controls);
    applyEach(path.materials, (material) => {
      required(material.label);
      maxLength(material.label, limits.nameLength);
      maxLength(material.guidance, limits.scopedGuidanceLength);
      maxLength(material.text, limits.bodyLength);
      applyEach(material.controls, controls);
    });
  });

const text = (value: string | number | boolean | null | undefined) =>
  value == null ? '' : String(value);
const choiceForm = (choice?: IntegerChoice | null): ChoiceForm => ({
  value: text(choice?.value),
  adjustable: choice?.adjustable ?? false,
});
const lengthForm = (length?: LengthExpectation | null): LengthForm => ({
  ...choiceForm(length?.count),
  mode: length?.mode ?? '',
  lower: text(length?.lower),
  upper: text(length?.upper),
});

/** IDs are generated only for deliberate editor additions; labels never recover identity. */
export const newPlanId = () => crypto.randomUUID().replaceAll('-', '');
export function controlForm(control?: PlanControl): ControlForm {
  return {
    id: control?.id ?? newPlanId(),
    label: control?.label ?? '',
    type: control?.type ?? 'text',
    meaning: control?.meaning ?? '',
    required: control?.required ?? false,
    hasDefault: control?.default != null,
    defaultValue: text(control?.default),
    unit: control?.unit ?? '',
    min: text(control?.min),
    max: text(control?.max),
    maxLength: text(control?.maxLength),
    options:
      control?.options?.map((option) => ({ value: option.value, meaning: option.meaning ?? '' })) ??
      [],
  };
}
export function materialForm(material?: PlanMaterial): MaterialForm {
  return {
    id: material?.id ?? newPlanId(),
    label: material?.label ?? '',
    source: material?.source ?? 'generated',
    guidance: material?.guidance ?? '',
    text: material?.text ?? '',
    length: lengthForm(material?.length),
    controls: material?.controls.map(controlForm) ?? [],
  };
}
/** A blank plan has no schema version; the AI status or a loaded record supplies the server's version. */
export function planForm(plan?: LearningPlan): PlanForm {
  return {
    schemaVersion: plan?.schemaVersion ?? 0,
    name: plan?.name ?? '',
    goal: plan?.goal ?? '',
    guidance: plan?.guidance ?? '',
    materials: plan?.materials.map(materialForm) ?? [],
    controls: plan?.controls.map(controlForm) ?? [],
    totalLength: lengthForm(plan?.totalLength),
    questions: {
      numeric: plan?.questions.formats.includes('numeric-input') ?? true,
      text: plan?.questions.formats.includes('text-input') ?? false,
      choice: plan?.questions.formats.includes('single-choice') ?? false,
      selectableFormat: plan?.questions.selectableFormat ?? false,
      defaultFormat: plan?.questions.defaultFormat ?? '',
      choiceCount: choiceForm(plan?.questions.choiceCount),
      guidance: plan?.questions.guidance ?? '',
      controls: plan?.questions.controls.map(controlForm) ?? [],
    },
  };
}

/** Canonical control order; per-activity control inputs are aligned with it. */
export function planControls(plan: LearningPlan): PlanControl[] {
  return [
    ...plan.controls,
    ...plan.materials.flatMap((material) => material.controls),
    ...plan.questions.controls,
  ];
}
/** The same control order over editable plan fields. */
export function formControls(plan: PlanForm): ControlForm[] {
  return [
    ...plan.controls,
    ...plan.materials.flatMap((material) => material.controls),
    ...plan.questions.controls,
  ];
}
/** Formats ticked in the editable plan, in the canonical order. */
export function formFormats(questions: PlanForm['questions']): QuestionFormat[] {
  return [
    ...(questions.numeric ? (['numeric-input'] as const) : []),
    ...(questions.text ? (['text-input'] as const) : []),
    ...(questions.choice ? (['single-choice'] as const) : []),
  ];
}
/** Rebuilds only applicable inputs, preserving values by app identity after a plan edit/proposal. */
export function inputForm(plan: LearningPlan, input?: ActivityInput): InputForm {
  return {
    settings: taskSettingsDraft(input?.settings ?? plan.defaults),
    questionFormat: input?.questionFormat ?? '',
    choiceCount: text(input?.choiceCount),
    totalWordCount: text(input?.totalWordCount),
    materials: plan.materials.map((material) => ({
      id: material.id,
      sourceText: input?.materialInputs?.[material.id]?.sourceText ?? '',
      wordCount: text(input?.materialInputs?.[material.id]?.wordCount),
    })),
    controls: planControls(plan).map((control) => ({
      id: control.id,
      provided: input?.controlValues?.[control.id] !== undefined,
      value: text(input?.controlValues?.[control.id]),
    })),
  };
}
