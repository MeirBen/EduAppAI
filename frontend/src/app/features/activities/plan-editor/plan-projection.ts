import {
  ActivityInput,
  ContentLimits,
  IntegerChoice,
  LearningPlan,
  LengthExpectation,
  PlanControl,
  QuestionFormat,
} from '../../../core/api/models';
import { count } from '../../../core/api/limits';
import { isIntegerInput } from '../../../shared/forms/integer-input';
import { Projection } from '../../../shared/forms/projection';
import { TaskSettingsDraft, taskSettingsValue } from '../../../shared/forms/task-settings';
import {
  ChoiceForm,
  ControlForm,
  ControlInputForm,
  formFormats,
  InputForm,
  LengthForm,
  PlanForm,
  planControls,
} from './plan-form';

function checkText(
  value: string,
  label: string,
  limit: number,
  errors: string[],
  required = false,
) {
  if ((required && !value.trim()) || value.length > limit)
    errors.push(`${label}: יש להזין ${required ? 'טקסט ' : ''}עד ${count(limit)} תווים.`);
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
function bounds(min: string, max: string, label: string, errors: string[]) {
  const lower = integer(min, label, errors),
    upper = integer(max, label, errors);
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
  return {
    value: integer(form.value, label, errors, true, minimum) ?? 0,
    adjustable: form.adjustable,
  };
}
function lengthValue(form: LengthForm, errors: string[]): LengthExpectation | null {
  if (!form.mode) return null;
  if (form.mode === 'range') {
    const lower = integer(form.lower, 'אורך מינימלי', errors, true, 1) ?? 0;
    const upper = integer(form.upper, 'אורך מרבי', errors, true, 1) ?? 0;
    if (lower >= upper) errors.push('אורך: המינימום חייב להיות קטן מהמקסימום.');
    return { mode: 'range', lower, upper };
  }
  return { mode: 'target', count: choiceValue(form, 'מספר מילים', errors) };
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
export function planValue(
  form: PlanForm,
  settings: TaskSettingsDraft,
  limits: ContentLimits,
): Projection<LearningPlan> {
  const errors: string[] = [];
  if (!form.schemaVersion) errors.push('ההגדרות עדיין נטענות מהשרת. נסו שוב בעוד רגע.');
  checkText(form.name, 'שם התבנית', limits.nameLength, errors, true);
  checkText(form.goal, 'מטרת הפעילות', limits.goalLength, errors, true);
  checkText(form.guidance, 'הנחיות משותפות', limits.guidanceLength, errors);
  const defaults = settingsValue(settings, errors, limits);
  const formats = formFormats(form.questions);
  if (!formats.length) errors.push('יש לבחור לפחות סוג שאלה אחד.');
  if (
    form.questions.selectableFormat &&
    !formats.includes(form.questions.defaultFormat as QuestionFormat)
  )
    errors.push('יש לבחור סוג שאלה כברירת מחדל.');
  if (!form.questions.selectableFormat && defaults.questionCount < formats.length)
    errors.push('נדרשת לפחות שאלה אחת מכל סוג שנבחר.');
  const { minChoiceCount, maxChoiceCount } = limits;
  const choiceCount = form.questions.choice
    ? choiceValue(form.questions.choiceCount, 'מספר אפשרויות', errors, minChoiceCount)
    : null;
  if (choiceCount && choiceCount.value > maxChoiceCount)
    errors.push(`מספר האפשרויות חייב להיות בין ${minChoiceCount} ל־${maxChoiceCount}.`);
  checkText(form.questions.guidance, 'הנחיות לשאלות', limits.scopedGuidanceLength, errors);
  const materials = form.materials.map((material) => {
    checkText(material.label, 'שם הטקסט', limits.nameLength, errors, true);
    checkText(material.guidance, 'הנחיות לטקסט', limits.scopedGuidanceLength, errors);
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
    errors.push('בחרו אורך כולל או אורך לכל טקסט, לא את שניהם.');
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
      guidance: form.questions.guidance,
      controls: form.questions.controls.map((control) => controlValue(control, errors, limits)),
    },
  };
  if (materials.length > limits.maxMaterials || planControls(value).length > limits.maxControls)
    errors.push(`אפשר להוסיף עד ${limits.maxMaterials} טקסטים ועד ${limits.maxControls} בחירות.`);
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
      errors.push('סוג השאלה אינו זמין בהגדרות.');
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
    if (value.choiceCount !== undefined && value.choiceCount > limits.maxChoiceCount)
      errors.push(`עד ${limits.maxChoiceCount} אפשרויות.`);
  }
  if (plan.totalLength?.count?.adjustable && form.totalWordCount !== '') {
    value.totalWordCount = integer(form.totalWordCount, 'מספר מילים כולל', errors, true, 1);
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
      if (wordCount !== undefined) materialInputs[material.id] = { wordCount };
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
