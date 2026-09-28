import { TemplateDefinition } from '../../../core/api/models';

/** Test-only blueprint; production templates come from AI and parent review. */
export const readingDefinition: TemplateDefinition = {
  schemaVersion: 2,
  name: 'הבנת הנקרא',
  generation: {
    instructions: 'צרו קטע לפי theme עם count שאלות.',
    questionCountParameter: 'count',
  },
  instanceParameters: [
    { key: 'theme', label: 'נושא', type: 'text', required: true, default: 'חלל', maxLength: 100 },
    {
      key: 'count',
      label: 'מספר שאלות',
      type: 'integer',
      required: true,
      default: 5,
      min: 1,
      max: 20,
    },
    {
      key: 'level',
      label: 'רמה',
      type: 'select',
      required: true,
      default: 'קלה',
      options: ['קלה', 'קשה'],
    },
    { key: 'hints', label: 'רמזים', type: 'boolean', required: false, default: false },
  ],
};
