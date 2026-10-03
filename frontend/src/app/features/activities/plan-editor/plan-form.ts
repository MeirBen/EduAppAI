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
import { isIntegerInput } from '../../../shared/forms/integer-input';
import {
  TaskSettingsDraft,
  taskSettingsDraft,
  taskSettingsSchema,
  taskSettingsValue,
} from '../../../shared/forms/task-settings';
import { apply, applyEach, maxLength, required, schema } from '@angular/forms/signals';

/** Initialized presentation values retain blank/invalid keystrokes outside canonical HTTP records. */
export interface ChoiceForm {
  value: string;
  adjustable: boolean;
  min: string;
  max: string;
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
  settings: TaskSettingsDraft;
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
    min: string;
    max: string;
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
export interface Projection<T> {
  value?: T;
  errors: string[];
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
    apply(path.settings, taskSettingsSchema(limits));
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
  min: text(choice?.min),
  max: text(choice?.max),
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
/** A blank plan has no schema counter; status/loading supplies the server's version before submission. */
export function planForm(plan?: LearningPlan): PlanForm {
  return {
    schemaVersion: plan?.schemaVersion ?? 0,
    name: plan?.name ?? '',
    goal: plan?.goal ?? '',
    guidance: plan?.guidance ?? '',
    settings: plan
      ? taskSettingsDraft(plan.defaults)
      : { topic: '', audience: '', difficulty: 'medium', questionCount: '1' },
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
      min: text(plan?.questions.countBounds?.min),
      max: text(plan?.questions.countBounds?.max),
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

function checkText(
  value: string,
  label: string,
  limit: number,
  errors: string[],
  required = false,
) {
  if ((required && !value.trim()) || value.length > limit)
    errors.push(`${label}: יש להזין ${required ? 'טקסט ' : ''}עד ${limit} תווים.`);
}
function integer(
  value: string,
  label: string,
  errors: string[],
  required = false,
  minimum = -2147483648,
): number | undefined {
  if (value === '' && !required) return undefined;
  if (!isIntegerInput(value) || Number(value) < minimum) {
    errors.push(`${label}: יש להזין מספר שלם תקין.`);
    return undefined;
  }
  return Number(value);
}
function bounds(min: string, max: string, label: string, errors: string[], minimum = -2147483648) {
  const lower = integer(min, label, errors, false, minimum),
    upper = integer(max, label, errors, false, minimum);
  if (lower !== undefined && upper !== undefined && lower > upper)
    errors.push(`${label}: המינימום גדול מהמקסימום.`);
  return {
    ...(lower === undefined ? {} : { min: lower }),
    ...(upper === undefined ? {} : { max: upper }),
  };
}
function inBounds(
  value: number,
  range: { min?: number | null; max?: number | null },
  label: string,
  errors: string[],
) {
  if ((range.min != null && value < range.min) || (range.max != null && value > range.max))
    errors.push(`${label}: הערך מחוץ לטווח.`);
}
function choiceValue(
  form: ChoiceForm,
  label: string,
  errors: string[],
  minimum = 1,
): IntegerChoice {
  const value = integer(form.value, label, errors, true, minimum) ?? 0;
  const range = form.adjustable ? bounds(form.min, form.max, label, errors, minimum) : {};
  inBounds(value, range, label, errors);
  return { value, adjustable: form.adjustable, ...range };
}
function lengthValue(form: LengthForm, errors: string[]): LengthExpectation | null {
  if (!form.mode) return null;
  if (form.mode === 'range') {
    const lower = integer(form.lower, 'אורך מינימלי', errors, true, 1) ?? 0;
    const upper = integer(form.upper, 'אורך מרבי', errors, true, 1) ?? 0;
    if (lower > upper) errors.push('אורך: המינימום גדול מהמקסימום.');
    return { mode: 'range', lower, upper };
  }
  return { mode: form.mode, count: choiceValue(form, 'מספר מילים', errors) };
}
const questionCountRange = (limits: ContentLimits) =>
  `מספר השאלות חייב להיות בין 1 ל־${limits.maxQuestionCount}.`;
function settingsValue(form: TaskSettingsDraft, errors: string[], limits: ContentLimits) {
  checkText(form.topic, 'נושא', limits.settingTextLength, errors, true);
  checkText(form.audience, 'למי מיועדת הפעילות', limits.settingTextLength, errors, true);
  const questionCount = integer(form.questionCount, 'מספר שאלות', errors, true, 1);
  if (questionCount !== undefined && questionCount > limits.maxQuestionCount)
    errors.push(questionCountRange(limits));
  return taskSettingsValue(form);
}
function controlValue(form: ControlForm, errors: string[], limits: ContentLimits): PlanControl {
  checkText(form.label, 'שם הבחירה', limits.nameLength, errors, true);
  checkText(form.meaning, form.label || 'משמעות הבחירה', limits.meaningLength, errors, true);
  const result: PlanControl = {
    id: form.id,
    label: form.label,
    type: form.type,
    meaning: form.meaning,
    required: form.required,
  };
  if (form.type === 'integer' && form.unit) result.unit = form.unit;
  if (form.type === 'integer')
    Object.assign(result, bounds(form.min, form.max, form.label, errors));
  if (form.type === 'text') {
    result.maxLength = integer(form.maxLength, form.label, errors, false, 1);
    if ((result.maxLength ?? limits.textValueLength) > limits.textValueLength)
      errors.push(`${form.label}: עד ${limits.textValueLength} תווים.`);
  }
  if (form.type === 'select') {
    result.options = form.options.map((option) => ({
      value: option.value,
      ...(option.meaning ? { meaning: option.meaning } : {}),
    }));
    if (
      !result.options.length ||
      result.options.length > limits.maxSelectOptions ||
      new Set(result.options.map((o) => o.value)).size !== result.options.length
    )
      errors.push(`${form.label}: נדרשות 1–${limits.maxSelectOptions} אפשרויות שונות.`);
    for (const option of form.options) {
      checkText(option.value, form.label, limits.selectOptionLength, errors, true);
      checkText(option.meaning, form.label, limits.selectOptionMeaningLength, errors);
    }
  }
  if (form.hasDefault) result.default = scalarValue(result, form.defaultValue, errors, limits);
  return result;
}
function scalarValue(
  control: PlanControl,
  value: string,
  errors: string[],
  limits: ContentLimits,
): string | number | boolean | undefined {
  switch (control.type) {
    case 'integer': {
      const number = integer(value, control.label, errors, true);
      if (number !== undefined) inBounds(number, control, control.label, errors);
      return number;
    }
    case 'boolean':
      if (value !== 'true' && value !== 'false')
        errors.push(`${control.label}: יש לבחור כן או לא.`);
      return value === 'true';
    case 'select':
      if (!control.options?.some((option) => option.value === value))
        errors.push(`${control.label}: יש לבחור אפשרות מהרשימה.`);
      return value;
    case 'text':
      checkText(
        value,
        control.label,
        control.maxLength ?? limits.textValueLength,
        errors,
        control.required,
      );
      return value;
  }
}

/** UI shape feedback only. The shared server validator remains authoritative for full semantic/capacity checks. */
export function planValue(form: PlanForm, limits: ContentLimits): Projection<LearningPlan> {
  const errors: string[] = [];
  if (!form.schemaVersion) errors.push('ממתינים להגדרות התכנית מהשרת.');
  checkText(form.name, 'שם התבנית', limits.nameLength, errors, true);
  checkText(form.goal, 'מטרת הפעילות', limits.goalLength, errors, true);
  checkText(form.guidance, 'הנחיות משותפות', limits.guidanceLength, errors);
  const defaults = settingsValue(form.settings, errors, limits);
  const formats = formFormats(form.questions);
  if (!formats.length) errors.push('יש לבחור לפחות סוג שאלה אחד.');
  if (
    form.questions.selectableFormat &&
    !formats.includes(form.questions.defaultFormat as QuestionFormat)
  )
    errors.push('יש לבחור סוג שאלה כברירת מחדל.');
  if (!form.questions.selectableFormat && defaults.questionCount < formats.length)
    errors.push('נדרשת לפחות שאלה אחת מכל סוג שנבחר.');
  const questionBounds = bounds(form.questions.min, form.questions.max, 'מספר שאלות', errors, 1);
  inBounds(defaults.questionCount, questionBounds, 'מספר שאלות', errors);
  if ((questionBounds.max ?? 0) > limits.maxQuestionCount) errors.push(questionCountRange(limits));
  const { minChoiceCount, maxChoiceCount } = limits;
  const choiceCount = form.questions.choice
    ? choiceValue(form.questions.choiceCount, 'מספר אפשרויות', errors, minChoiceCount)
    : null;
  if (
    choiceCount &&
    (choiceCount.value > maxChoiceCount ||
      (choiceCount.max ?? maxChoiceCount) > maxChoiceCount ||
      (choiceCount.min ?? minChoiceCount) < minChoiceCount)
  )
    errors.push(`מספר האפשרויות חייב להיות בין ${minChoiceCount} ל־${maxChoiceCount}.`);
  checkText(form.questions.guidance, 'הנחיות לשאלות', limits.scopedGuidanceLength, errors);
  const materials = form.materials.map((material) => {
    checkText(material.label, 'שם החומר', limits.nameLength, errors, true);
    checkText(material.guidance, 'הנחיות לחומר', limits.scopedGuidanceLength, errors);
    if (material.source === 'fixed')
      checkText(material.text, material.label, limits.bodyLength, errors, true);
    return {
      id: material.id,
      label: material.label,
      source: material.source,
      guidance: material.guidance,
      text: material.source === 'fixed' ? material.text : null,
      length: material.source === 'generated' ? lengthValue(material.length, errors) : null,
      controls: material.controls.map((control) => controlValue(control, errors, limits)),
    };
  });
  const totalLength = materials.some((material) => material.source === 'generated')
    ? lengthValue(form.totalLength, errors)
    : null;
  if (totalLength && materials.some((material) => material.length))
    errors.push('בחרו אורך כולל או אורך לכל חומר, לא את שניהם.');
  const value: LearningPlan = {
    schemaVersion: form.schemaVersion,
    name: form.name,
    goal: form.goal,
    guidance: form.guidance,
    defaults,
    materials,
    totalLength,
    controls: form.controls.map((control) => controlValue(control, errors, limits)),
    questions: {
      formats,
      selectableFormat: form.questions.selectableFormat,
      defaultFormat: form.questions.selectableFormat
        ? (form.questions.defaultFormat as QuestionFormat)
        : null,
      choiceCount,
      countBounds: Object.keys(questionBounds).length ? questionBounds : null,
      guidance: form.questions.guidance,
      controls: form.questions.controls.map((control) => controlValue(control, errors, limits)),
    },
  };
  if (materials.length > limits.maxMaterials || planControls(value).length > limits.maxControls)
    errors.push(`אפשר להוסיף עד ${limits.maxMaterials} חומרים ועד ${limits.maxControls} בחירות.`);
  return { value: errors.length ? undefined : value, errors };
}

/** Omits inapplicable/unset overrides; never sends form-only nulls or confirmation metadata. */
export function requestValue(
  plan: LearningPlan,
  form: InputForm,
  limits: ContentLimits,
): Projection<ActivityInput> {
  const errors: string[] = [];
  const value: ActivityInput = { settings: settingsValue(form.settings, errors, limits) };
  if (plan.questions.selectableFormat && form.questionFormat) {
    if (!plan.questions.formats.includes(form.questionFormat))
      errors.push('סוג השאלה אינו זמין בתכנית.');
    value.questionFormat = form.questionFormat;
  }
  const formats = plan.questions.selectableFormat
    ? [value.questionFormat ?? plan.questions.defaultFormat]
    : plan.questions.formats;
  if (
    formats.includes('single-choice') &&
    plan.questions.choiceCount?.adjustable &&
    form.choiceCount !== ''
  ) {
    value.choiceCount = integer(
      form.choiceCount,
      'מספר אפשרויות',
      errors,
      true,
      limits.minChoiceCount,
    );
    if (value.choiceCount !== undefined) {
      inBounds(value.choiceCount, plan.questions.choiceCount, 'מספר אפשרויות', errors);
      if (value.choiceCount > limits.maxChoiceCount)
        errors.push(`עד ${limits.maxChoiceCount} אפשרויות.`);
    }
  }
  if (plan.totalLength?.count?.adjustable && form.totalWordCount !== '') {
    value.totalWordCount = integer(form.totalWordCount, 'מספר מילים כולל', errors, true, 1);
    if (value.totalWordCount !== undefined)
      inBounds(value.totalWordCount, plan.totalLength.count, 'מספר מילים כולל', errors);
  }
  const materialInputs: NonNullable<ActivityInput['materialInputs']> = {};
  for (const material of plan.materials) {
    const fields = form.materials.find((input) => input.id === material.id);
    if (material.source === 'per-task') {
      const sourceText = fields?.sourceText ?? '';
      checkText(sourceText, material.label, limits.bodyLength, errors, true);
      materialInputs[material.id] = { sourceText };
    } else if (
      material.source === 'generated' &&
      material.length?.count?.adjustable &&
      fields?.wordCount
    ) {
      const wordCount = integer(fields.wordCount, material.label, errors, true, 1);
      if (wordCount !== undefined) {
        inBounds(wordCount, material.length.count, material.label, errors);
        materialInputs[material.id] = { wordCount };
      }
    }
  }
  if (Object.keys(materialInputs).length) value.materialInputs = materialInputs;
  const controlValues: NonNullable<ActivityInput['controlValues']> = {};
  for (const control of planControls(plan)) {
    const input = form.controls.find((input) => input.id === control.id);
    const selected = controlInputValue(control, limits, input);
    errors.push(...selected.errors);
    if (selected.value !== undefined) controlValues[control.id] = selected.value;
  }
  if (Object.keys(controlValues).length) value.controlValues = controlValues;
  inBounds(value.settings.questionCount, plan.questions.countBounds ?? {}, 'מספר שאלות', errors);
  return { value: errors.length ? undefined : value, errors };
}

/** Shared by native field feedback and the HTTP projection; omission remains distinct from false/zero/empty text. */
export function controlInputValue(
  control: PlanControl,
  limits: ContentLimits,
  input?: ControlInputForm,
): Projection<string | number | boolean> {
  const errors: string[] = [];
  const provided = input && (control.type === 'text' ? input.provided : input.value !== '');
  const value = provided ? scalarValue(control, input.value, errors, limits) : undefined;
  if (!provided && control.required && control.default == null)
    errors.push(`${control.label}: נדרש ערך.`);
  return { value: errors.length ? undefined : value, errors };
}
