import { ParameterDefinition } from '../api/models';

/** Presents legacy built-in labels in Hebrew without rewriting immutable template snapshots. */
export function parameterLabel(definition: ParameterDefinition): string {
  if (definition.key === 'difficulty' && definition.label === 'Difficulty') return 'רמת קושי';
  if (definition.key === 'questionCount' && definition.label === 'Number of questions')
    return 'מספר שאלות';
  return definition.label;
}

/** Translates only the math difficulty display; submitted enum values stay unchanged. */
export function parameterOption(definition: ParameterDefinition, value: string): string {
  if (definition.key !== 'difficulty') return value;
  switch (value) {
    case 'easy':
      return 'קלה';
    case 'medium':
      return 'בינונית';
    case 'hard':
      return 'מאתגרת';
    default:
      return value;
  }
}

/** Localizes the old generator's fixed instruction; authored content is displayed verbatim. */
export function taskInstructions(value: string | null): string | null {
  return value === 'Multiply the two numbers.' ? 'מהי המכפלה של שני המספרים?' : value;
}
