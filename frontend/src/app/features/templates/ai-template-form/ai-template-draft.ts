import { ParameterDefinition, TemplateDefinition } from '../../../core/api/models';
import { isIntegerInput } from '../../../shared/forms/integer-input';

/** Editor-only strings retain empty and invalid numeric inputs until validation. */
export interface ParameterDraft {
  id: string;
  key: string;
  label: string;
  type: ParameterDefinition['type'];
  required: boolean;
  defaultValue: string;
  /** Distinguishes an explicit empty optional-text default from an omitted default. */
  emptyTextDefault: boolean;
  min: string;
  max: string;
  maxLength: string;
  options: string;
}

/** Reusable instructions and field definitions, never a generated task or published snapshot. */
export interface AiBlueprintDraft {
  name: string;
  instructions: string;
  questionCountParameter: string;
  parameters: ParameterDraft[];
}

export function blankParameter(): ParameterDraft {
  return {
    id: crypto.randomUUID(),
    key: '',
    label: '',
    type: 'text',
    required: true,
    defaultValue: '',
    emptyTextDefault: false,
    min: '',
    max: '',
    maxLength: '100',
    options: '',
  };
}

/** Copies a server blueprint so local edits cannot mutate the loaded revision. */
export function aiTemplateDraft(definition: TemplateDefinition): AiBlueprintDraft {
  return {
    name: definition.name,
    instructions: definition.generation.instructions,
    questionCountParameter: definition.generation.questionCountParameter ?? '',
    parameters: definition.instanceParameters.map((field) => ({
      id: crypto.randomUUID(),
      key: field.key,
      label: field.label,
      type: field.type,
      required: field.required ?? false,
      defaultValue: field.default == null ? '' : String(field.default),
      emptyTextDefault: field.type === 'text' && field.default === '',
      min: field.min == null ? '' : String(field.min),
      max: field.max == null ? '' : String(field.max),
      maxLength: field.maxLength == null ? '' : String(field.maxLength),
      options: field.options?.join('\n') ?? '',
    })),
  };
}

function options(field: ParameterDraft): string[] {
  return field.options
    .split(/\r?\n/)
    .map((value) => value.trim())
    .filter(Boolean);
}

/** Mirrors the editable field contract for immediate feedback; the server validates again on save. */
export function aiTemplateErrors(draft: AiBlueprintDraft): string[] {
  const errors: string[] = [];
  if (!draft.name.trim() || draft.name.length > 100) errors.push('יש להזין שם עד 100 תווים.');
  if (!draft.instructions.trim() || draft.instructions.length > 4000)
    errors.push('יש להזין הנחיות עד 4,000 תווים.');
  if (draft.parameters.length > 16) errors.push('אפשר להגדיר עד 16 שדות.');
  const keys = new Set<string>();
  for (const [index, field] of draft.parameters.entries()) {
    const prefix = `שדה ${index + 1}: `;
    if (!/^[a-z][a-zA-Z0-9]{0,39}$/.test(field.key) || keys.has(field.key))
      errors.push(prefix + 'נדרש מפתח ייחודי באותיות לטיניות, ללא רווחים.');
    keys.add(field.key);
    if (!field.label.trim() || field.label.length > 100)
      errors.push(prefix + 'נדרשת תווית עד 100 תווים.');
    if (field.type === 'integer') {
      for (const value of [field.min, field.max, field.defaultValue]) {
        if (value !== '' && !isIntegerInput(value))
          errors.push(prefix + 'הגבולות וברירת המחדל חייבים להיות מספרים שלמים.');
      }
      if (field.min !== '' && field.max !== '' && Number(field.min) > Number(field.max))
        errors.push(prefix + 'המינימום גדול מהמקסימום.');
      if (
        field.defaultValue !== '' &&
        ((field.min !== '' && Number(field.defaultValue) < Number(field.min)) ||
          (field.max !== '' && Number(field.defaultValue) > Number(field.max)))
      )
        errors.push(prefix + 'ברירת המחדל מחוץ לטווח.');
    } else if (field.type === 'select') {
      const choices = options(field);
      if (
        !choices.length ||
        choices.length > 20 ||
        new Set(choices).size !== choices.length ||
        choices.some((choice) => choice.length > 100)
      )
        errors.push(prefix + 'נדרשות 1–20 אפשרויות שונות, עד 100 תווים לאפשרות.');
      if (field.defaultValue !== '' && !choices.includes(field.defaultValue))
        errors.push(prefix + 'ברירת המחדל חייבת להתאים לאחת האפשרויות.');
    } else if (field.type === 'text') {
      const limit = field.maxLength === '' ? 500 : Number(field.maxLength);
      if ((field.maxLength !== '' && !isIntegerInput(field.maxLength)) || limit < 1 || limit > 500)
        errors.push(prefix + 'אורך הטקסט חייב להיות בין 1 ל־500.');
      if (
        field.defaultValue.length > limit ||
        (field.required && field.defaultValue !== '' && !field.defaultValue.trim())
      )
        errors.push(prefix + 'ברירת המחדל אינה טקסט תקין בטווח.');
    } else if (field.defaultValue !== '' && !['true', 'false'].includes(field.defaultValue))
      errors.push(prefix + 'יש לבחור כן או לא.');
  }
  if (draft.questionCountParameter) {
    const count = draft.parameters.find((field) => field.key === draft.questionCountParameter);
    if (
      !count ||
      count.type !== 'integer' ||
      !count.required ||
      count.min === '' ||
      count.max === '' ||
      Number(count.min) < 1 ||
      Number(count.max) > 20
    )
      errors.push('מספר השאלות חייב להיות מקושר לשדה מספרי נדרש עם גבולות בין 1 ל־20.');
  }
  return errors;
}

/** Builds the public contract after validation, removing editor identities and irrelevant type settings. */
export function aiTemplateDefinition(draft: AiBlueprintDraft): TemplateDefinition {
  return {
    schemaVersion: 2,
    name: draft.name.trim(),
    generation: {
      instructions: draft.instructions,
      questionCountParameter: draft.questionCountParameter || null,
    },
    instanceParameters: draft.parameters.map(parameterDefinition),
  };
}

function parameterDefinition(field: ParameterDraft): ParameterDefinition {
  const definition: ParameterDefinition = {
    key: field.key,
    label: field.label,
    type: field.type,
    required: field.required,
  };
  const hasDefault = field.defaultValue !== '';
  switch (field.type) {
    case 'integer':
      if (hasDefault) definition.default = Number(field.defaultValue);
      if (field.min !== '') definition.min = Number(field.min);
      if (field.max !== '') definition.max = Number(field.max);
      break;
    case 'boolean':
      if (hasDefault) definition.default = field.defaultValue === 'true';
      break;
    case 'text':
      if (hasDefault || (!field.required && field.emptyTextDefault))
        definition.default = field.defaultValue;
      if (field.maxLength !== '') definition.maxLength = Number(field.maxLength);
      break;
    case 'select':
      if (hasDefault) definition.default = field.defaultValue;
      definition.options = options(field);
      break;
  }
  return definition;
}
