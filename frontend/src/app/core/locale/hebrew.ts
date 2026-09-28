import { MathOperation, ParameterDefinition } from '../api/models';

/** Hebrew operation labels, independent of the immutable wire keys. */
export const mathOperations: { value: MathOperation; label: string }[] = [
  { value: 'addition', label: 'חיבור' },
  { value: 'subtraction', label: 'חיסור' },
  { value: 'multiplication', label: 'כפל' },
  { value: 'division', label: 'חילוק' },
];

/** Describes the generator's operand bounds; division ranges refer to divisor and quotient. */
export function mathRange(operation: MathOperation, difficulty: string): string {
  const larger = operation === 'addition' || operation === 'subtraction';
  const maximum =
    difficulty === 'easy'
      ? larger
        ? 10
        : 5
      : difficulty === 'medium'
        ? larger
          ? 50
          : 10
        : larger
          ? 100
          : 12;
  return operation === 'division'
    ? `המחלק והתוצאה מ־1 עד ${maximum}`
    : `המספרים בתרגיל מ־1 עד ${maximum}`;
}

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
