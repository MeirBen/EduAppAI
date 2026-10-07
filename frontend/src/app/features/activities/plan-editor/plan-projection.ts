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
import { FieldIssue, Projection } from '../../../shared/forms/projection';
import {
  difficultyLabels,
  TaskSettingsDraft,
  taskSettingsValue,
} from '../../../shared/forms/task-settings';
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

type Path = FieldIssue['path'];

function checkText(
  value: string,
  limit: number,
  issues: FieldIssue[],
  path: Path,
  required = false,
) {
  if (required && !value.trim()) issues.push({ path, message: 'זהו שדה חובה.' });
  else if (value.length > limit)
    issues.push({ path, message: `אפשר להזין עד ${count(limit)} תווים.` });
}
function integer(
  value: string,
  issues: FieldIssue[],
  path: Path,
  required = false,
  minimum = -2147483648,
): number | undefined {
  if (!value.trim() && !required) return undefined;
  if (!isIntegerInput(value) || Number(value) < minimum) {
    issues.push({ path, message: 'יש להזין מספר שלם.' });
    return undefined;
  }
  return Number(value);
}
function bounds(min: string, max: string, issues: FieldIssue[], path: Path) {
  const lower = integer(min, issues, [...path, 'min']),
    upper = integer(max, issues, [...path, 'max']);
  if (lower !== undefined && upper !== undefined && lower > upper)
    issues.push({ path: [...path, 'max'], message: 'המקסימום קטן מהמינימום.' });
  return {
    ...(lower === undefined ? {} : { min: lower }),
    ...(upper === undefined ? {} : { max: upper }),
  };
}
function inBounds(
  value: number,
  range: { min?: number | null; max?: number | null },
  issues: FieldIssue[],
  path: Path,
) {
  if ((range.min != null && value < range.min) || (range.max != null && value > range.max))
    issues.push({ path, message: 'הערך מחוץ לטווח.' });
}
function choiceValue(
  form: ChoiceForm,
  issues: FieldIssue[],
  path: Path,
  minimum = 1,
): IntegerChoice {
  return {
    value: integer(form.value, issues, [...path, 'value'], true, minimum) ?? 0,
    adjustable: form.adjustable,
  };
}
function lengthValue(form: LengthForm, issues: FieldIssue[], path: Path): LengthExpectation | null {
  if (!form.mode) return null;
  if (form.mode === 'range') {
    const lower = integer(form.lower, issues, [...path, 'lower'], true, 1) ?? 0;
    const upper = integer(form.upper, issues, [...path, 'upper'], true, 1) ?? 0;
    if (lower >= upper)
      issues.push({ path: [...path, 'upper'], message: 'המקסימום חייב להיות גדול מהמינימום.' });
    return { mode: 'range', lower, upper };
  }
  return { mode: 'target', count: choiceValue(form, issues, path) };
}
function settingsValue(form: TaskSettingsDraft, issues: FieldIssue[], limits: ContentLimits) {
  const at = (key: keyof TaskSettingsDraft) => ['input', 'settings', key];
  checkText(form.topic, limits.settingTextLength, issues, at('topic'), true);
  checkText(form.audience, limits.settingTextLength, issues, at('audience'), true);
  if (!Object.hasOwn(difficultyLabels, form.difficulty))
    issues.push({ path: at('difficulty'), message: 'יש לבחור רמת קושי.' });
  const questionCount = Number(form.questionCount);
  if (
    !isIntegerInput(form.questionCount) ||
    questionCount < 1 ||
    questionCount > limits.maxQuestionCount
  )
    issues.push({
      path: at('questionCount'),
      message: `יש להזין מספר שלם בין 1 ל־${limits.maxQuestionCount}.`,
    });
  return taskSettingsValue(form);
}
function controlValue(
  form: ControlForm,
  issues: FieldIssue[],
  limits: ContentLimits,
  path: Path,
): PlanControl {
  checkText(form.label, limits.nameLength, issues, [...path, 'label'], true);
  checkText(form.meaning, limits.meaningLength, issues, [...path, 'meaning'], true);
  const result: PlanControl = {
    id: form.id,
    label: form.label,
    type: form.type,
    meaning: form.meaning,
    required: form.required,
  };
  if (form.type === 'integer' && form.unit) result.unit = form.unit;
  if (form.type === 'integer') Object.assign(result, bounds(form.min, form.max, issues, path));
  if (form.type === 'text') {
    result.maxLength = integer(form.maxLength, issues, [...path, 'maxLength'], false, 1);
    if ((result.maxLength ?? limits.textValueLength) > limits.textValueLength)
      issues.push({
        path: [...path, 'maxLength'],
        message: `יש להזין מספר שלם בין 1 ל־${count(limits.textValueLength)}.`,
      });
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
      issues.push({
        path: [...path, 'options'],
        message: `נדרשות 1–${limits.maxSelectOptions} אפשרויות שונות.`,
      });
    form.options.forEach((option, index) =>
      checkText(
        option.value,
        limits.selectOptionLength,
        issues,
        [...path, 'options', index, 'value'],
        true,
      ),
    );
  }
  if (form.hasDefault)
    result.default = scalarValue(result, form.defaultValue, issues, limits, [
      ...path,
      'defaultValue',
    ]);
  return result;
}
function scalarValue(
  control: PlanControl,
  value: string,
  issues: FieldIssue[],
  limits: ContentLimits,
  path: Path,
): string | number | boolean | undefined {
  switch (control.type) {
    case 'integer': {
      const number = integer(value, issues, path, true);
      if (number !== undefined) inBounds(number, control, issues, path);
      return number;
    }
    case 'boolean':
      if (value !== 'true' && value !== 'false')
        issues.push({ path, message: 'יש לבחור כן או לא.' });
      return value === 'true';
    case 'select':
      if (!control.options?.some((option) => option.value === value))
        issues.push({ path, message: 'יש לבחור אפשרות מהרשימה.' });
      return value;
    case 'text':
      checkText(value, control.maxLength ?? limits.textValueLength, issues, path, control.required);
      return value;
  }
}

/**
 * UI shape feedback only, with each issue at its workspace field. The shared server validator
 * remains authoritative for full semantic and capacity checks.
 */
export function planValue(
  form: PlanForm,
  settings: TaskSettingsDraft,
  limits: ContentLimits,
): Projection<LearningPlan> {
  const errors: FieldIssue[] = [];
  if (!form.schemaVersion)
    errors.push({ path: [], message: 'ההגדרות עדיין נטענות מהשרת. נסו שוב בעוד רגע.' });
  checkText(form.name, limits.nameLength, errors, ['plan', 'name'], true);
  checkText(form.goal, limits.goalLength, errors, ['plan', 'goal'], true);
  const defaults = settingsValue(settings, errors, limits);
  const formats = formFormats(form.questions);
  if (!formats.length)
    errors.push({ path: ['plan', 'questions'], message: 'יש לבחור לפחות סוג שאלה אחד.' });
  if (
    form.questions.selectableFormat &&
    !formats.includes(form.questions.defaultFormat as QuestionFormat)
  )
    errors.push({
      path: ['plan', 'questions', 'defaultFormat'],
      message: 'יש לבחור סוג שאלה כברירת מחדל.',
    });
  if (!form.questions.selectableFormat && defaults.questionCount < formats.length)
    errors.push({
      path: ['input', 'settings', 'questionCount'],
      message: 'נדרשת לפחות שאלה אחת מכל סוג שנבחר.',
    });
  const { minChoiceCount, maxChoiceCount } = limits;
  const choiceCountPath = ['plan', 'questions', 'choiceCount'];
  const choiceCount = form.questions.choice
    ? choiceValue(form.questions.choiceCount, errors, choiceCountPath, minChoiceCount)
    : null;
  if (choiceCount && choiceCount.value > maxChoiceCount)
    errors.push({
      path: [...choiceCountPath, 'value'],
      message: `מספר האפשרויות חייב להיות בין ${minChoiceCount} ל־${maxChoiceCount}.`,
    });
  const materials = form.materials.map((material, index) => {
    const at = ['plan', 'materials', index];
    checkText(material.label, limits.nameLength, errors, [...at, 'label'], true);
    if (material.source === 'fixed')
      checkText(material.text, limits.bodyLength, errors, [...at, 'text'], true);
    return {
      id: material.id,
      label: material.label,
      source: material.source,
      guidance: material.guidance,
      text: material.source === 'fixed' ? material.text : null,
      length:
        material.source === 'generated'
          ? lengthValue(material.length, errors, [...at, 'length'])
          : null,
      controls: material.controls.map((control, item) =>
        controlValue(control, errors, limits, [...at, 'controls', item]),
      ),
    };
  });
  const totalLength = materials.some((material) => material.source === 'generated')
    ? lengthValue(form.totalLength, errors, ['plan', 'totalLength'])
    : null;
  if (totalLength && materials.some((material) => material.length))
    errors.push({
      path: ['plan', 'totalLength', 'mode'],
      message: 'בחרו אורך כולל או אורך לכל טקסט, לא את שניהם.',
    });
  const value: LearningPlan = {
    schemaVersion: form.schemaVersion,
    name: form.name,
    goal: form.goal,
    guidance: form.guidance,
    defaults,
    materials,
    totalLength,
    controls: form.controls.map((control, item) =>
      controlValue(control, errors, limits, ['plan', 'controls', item]),
    ),
    questions: {
      formats,
      selectableFormat: form.questions.selectableFormat,
      defaultFormat: form.questions.selectableFormat
        ? (form.questions.defaultFormat as QuestionFormat)
        : null,
      choiceCount,
      guidance: form.questions.guidance,
      controls: form.questions.controls.map((control, item) =>
        controlValue(control, errors, limits, ['plan', 'questions', 'controls', item]),
      ),
    },
  };
  if (materials.length > limits.maxMaterials || planControls(value).length > limits.maxControls)
    errors.push({
      path: [],
      message: `אפשר להוסיף עד ${limits.maxMaterials} טקסטים ועד ${limits.maxControls} בחירות.`,
    });
  return { value: errors.length ? undefined : value, errors };
}

/**
 * Omits inapplicable/unset overrides; never sends form-only nulls or confirmation metadata. The
 * settings are checked with the plan whose defaults they are.
 */
export function requestValue(
  plan: LearningPlan,
  form: InputForm,
  limits: ContentLimits,
): Projection<ActivityInput> {
  const errors: FieldIssue[] = [];
  const value: ActivityInput = { settings: taskSettingsValue(form.settings) };
  if (plan.questions.selectableFormat && form.questionFormat) {
    if (!plan.questions.formats.includes(form.questionFormat))
      errors.push({ path: ['input', 'questionFormat'], message: 'סוג השאלה אינו זמין בהגדרות.' });
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
    const path = ['input', 'choiceCount'];
    value.choiceCount = integer(form.choiceCount, errors, path, true, limits.minChoiceCount);
    if (value.choiceCount !== undefined && value.choiceCount > limits.maxChoiceCount)
      errors.push({
        path,
        message: `מספר האפשרויות חייב להיות בין ${limits.minChoiceCount} ל־${limits.maxChoiceCount}.`,
      });
  }
  if (plan.totalLength?.count?.adjustable && form.totalWordCount !== '') {
    value.totalWordCount = integer(
      form.totalWordCount,
      errors,
      ['input', 'totalWordCount'],
      true,
      1,
    );
  }
  // Inputs mirror the plan in order; the workspace reconciles them after every edit.
  const materialInputs: NonNullable<ActivityInput['materialInputs']> = {};
  plan.materials.forEach((material, index) => {
    const { sourceText, wordCount } = form.materials[index],
      at = ['input', 'materials', index];
    if (material.source === 'per-task') {
      checkText(sourceText, limits.bodyLength, errors, [...at, 'sourceText'], true);
      materialInputs[material.id] = { sourceText };
    } else if (material.source === 'generated' && material.length?.count?.adjustable && wordCount) {
      const words = integer(wordCount, errors, [...at, 'wordCount'], true, 1);
      if (words !== undefined) materialInputs[material.id] = { wordCount: words };
    }
  });
  if (Object.keys(materialInputs).length) value.materialInputs = materialInputs;
  const controlValues: NonNullable<ActivityInput['controlValues']> = {};
  planControls(plan).forEach((control, index) => {
    const path = ['input', 'controls', index, 'value'];
    const selected = controlInputValue(control, limits, form.controls[index], path);
    errors.push(...selected.errors);
    if (selected.value !== undefined) controlValues[control.id] = selected.value;
  });
  if (Object.keys(controlValues).length) value.controlValues = controlValues;
  return { value: errors.length ? undefined : value, errors };
}

/** One requested choice; omission remains distinct from false/zero/empty text. */
function controlInputValue(
  control: PlanControl,
  limits: ContentLimits,
  input: ControlInputForm,
  path: Path,
): Projection<string | number | boolean> {
  const errors: FieldIssue[] = [];
  const provided = control.type === 'text' ? input.provided : input.value !== '';
  const value = provided ? scalarValue(control, input.value, errors, limits, path) : undefined;
  if (!provided && control.required && control.default == null)
    errors.push({ path, message: 'זהו שדה חובה.' });
  return { value: errors.length ? undefined : value, errors };
}
