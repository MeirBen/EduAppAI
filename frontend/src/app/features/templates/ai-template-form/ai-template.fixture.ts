import { TemplateDefinition } from '../../../core/api/models';

/** Test-only blueprint; production templates come from AI and parent review. */
export const readingDefinition: TemplateDefinition = {
  schemaVersion: 4,
  name: 'הבנת הנקרא',
  generation: {
    instructions: 'צרו קטע לפי sourceText, paragraphs, style ו-hints.',
    defaults: { topic: 'חלל', audience: 'כיתה ג׳', difficulty: 'easy', questionCount: 5 },
  },
  instanceParameters: [
    {
      key: 'sourceText',
      label: 'טקסט מקור',
      type: 'text',
      required: true,
      default: 'חלל',
      maxLength: 100,
    },
    {
      key: 'paragraphs',
      label: 'מספר פסקאות',
      type: 'integer',
      required: true,
      default: 5,
      min: 1,
      max: 20,
    },
    {
      key: 'style',
      label: 'סגנון',
      type: 'select',
      required: true,
      default: 'מידעי',
      options: ['מידעי', 'סיפורי'],
    },
    { key: 'hints', label: 'רמזים', type: 'boolean', required: false, default: false },
  ],
};
