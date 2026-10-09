import { ContentLimits, LearningPlan, LengthExpectation } from '../../../core/api/models';
import { count } from '../../../core/api/limits';
import { isIntegerInput } from '../../../shared/forms/integer-input';
import { FieldIssue, Projection } from '../../../shared/forms/projection';
import {
  difficultyLabels,
  TaskSettingsDraft,
  taskSettingsValue,
} from '../../../shared/forms/task-settings';
import { ChoiceForm, formFormats, LengthForm, PlanForm } from './plan-form';

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
  minimum: number,
): number | undefined {
  if (!isIntegerInput(value) || Number(value) < minimum) {
    issues.push({ path, message: 'יש להזין מספר שלם.' });
    return undefined;
  }
  return Number(value);
}
function choiceValue(form: ChoiceForm, issues: FieldIssue[], path: Path, minimum = 1): number {
  return integer(form.value, issues, [...path, 'value'], minimum) ?? 0;
}
function lengthValue(form: LengthForm, issues: FieldIssue[], path: Path): LengthExpectation | null {
  if (!form.mode) return null;
  if (form.mode === 'range') {
    const lower = integer(form.lower, issues, [...path, 'lower'], 1) ?? 0;
    const upper = integer(form.upper, issues, [...path, 'upper'], 1) ?? 0;
    if (lower >= upper)
      issues.push({ path: [...path, 'upper'], message: 'המקסימום חייב להיות גדול מהמינימום.' });
    return { mode: 'range', lower, upper };
  }
  return { mode: 'target', count: choiceValue(form, issues, path) };
}
function settingsValue(form: TaskSettingsDraft, issues: FieldIssue[], limits: ContentLimits) {
  const at = (key: keyof TaskSettingsDraft) => ['plan', 'settings', key];
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
/**
 * UI shape feedback only, with each issue at its workspace field. The shared server validator
 * remains authoritative for full semantic and capacity checks.
 */
export function planValue(form: PlanForm, limits: ContentLimits): Projection<LearningPlan> {
  const errors: FieldIssue[] = [];
  if (!form.schemaVersion)
    errors.push({ path: [], message: 'ההגדרות עדיין נטענות מהשרת. נסו שוב בעוד רגע.' });
  checkText(form.name, limits.nameLength, errors, ['plan', 'name'], true);
  checkText(form.goal, limits.goalLength, errors, ['plan', 'goal'], true);
  const settings = settingsValue(form.settings, errors, limits);
  const formats = formFormats(form.questions);
  if (!formats.length)
    errors.push({ path: ['plan', 'questions'], message: 'יש לבחור לפחות סוג שאלה אחד.' });
  if (settings.questionCount < formats.length)
    errors.push({
      path: ['plan', 'settings', 'questionCount'],
      message: 'נדרשת לפחות שאלה אחת מכל סוג שנבחר.',
    });
  const { minChoiceCount, maxChoiceCount } = limits;
  const choiceCountPath = ['plan', 'questions', 'choiceCount'];
  const choiceCount = form.questions.choice
    ? choiceValue(form.questions.choiceCount, errors, choiceCountPath, minChoiceCount)
    : null;
  if (choiceCount && choiceCount > maxChoiceCount)
    errors.push({
      path: [...choiceCountPath, 'value'],
      message: `מספר האפשרויות חייב להיות בין ${minChoiceCount} ל־${maxChoiceCount}.`,
    });
  const materials = form.materials.map((material, index) => {
    const at = ['plan', 'materials', index];
    checkText(material.label, limits.nameLength, errors, [...at, 'label'], true);
    if (material.source === 'supplied')
      checkText(material.text, limits.bodyLength, errors, [...at, 'text'], true);
    return {
      id: material.id,
      label: material.label,
      source: material.source,
      guidance: material.guidance,
      text: material.source === 'supplied' ? material.text : null,
      length:
        material.source === 'generated'
          ? lengthValue(material.length, errors, [...at, 'length'])
          : null,
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
    settings,
    materials,
    totalLength,
    questions: {
      formats,
      choiceCount,
      guidance: form.questions.guidance,
    },
  };
  if (materials.length > limits.maxMaterials)
    errors.push({
      path: [],
      message: `אפשר להוסיף עד ${limits.maxMaterials} טקסטים.`,
    });
  return { value: errors.length ? undefined : value, errors };
}
