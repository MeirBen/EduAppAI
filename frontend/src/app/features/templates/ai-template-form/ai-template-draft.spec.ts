import { readingDefinition } from './ai-template.fixture';
import { aiTemplateDefinition, aiTemplateDraft, aiTemplateErrors } from './ai-template-draft';

describe('AI blueprint editor', () => {
  it('round-trips all four parameter types, false defaults and custom count binding without mutation', () => {
    const original = structuredClone(readingDefinition);
    const draft = aiTemplateDraft(readingDefinition);
    expect(aiTemplateErrors(draft)).toEqual([]);
    expect(aiTemplateDefinition(draft)).toEqual(readingDefinition);
    draft.parameters[0].label = 'נושא חדש';
    expect(readingDefinition).toEqual(original);
  });

  it('rejects duplicate keys, invalid defaults and a removed count binding', () => {
    const draft = aiTemplateDraft(readingDefinition);
    draft.parameters[0].key = 'count';
    draft.parameters[1].defaultValue = '1.5';
    draft.questionCountParameter = 'missing';
    expect(aiTemplateErrors(draft).length).toBeGreaterThanOrEqual(3);
  });

  it('does not coerce an empty numeric default to zero', () => {
    const draft = aiTemplateDraft(readingDefinition);
    draft.parameters[1].defaultValue = '';
    expect(aiTemplateDefinition(draft).instanceParameters[1].default).toBeUndefined();
  });

  it('accepts a bound count with a valid default without requiring the parent to enter it', () => {
    const definition = structuredClone(readingDefinition);
    definition.instanceParameters[1].required = false;
    const draft = aiTemplateDraft(definition);
    expect(aiTemplateErrors(draft)).toEqual([]);
    expect(aiTemplateDefinition(draft)).toEqual(definition);
  });

  it.each(['', '0', '21', '1.5', 'four'])(
    'rejects a bound optional count with a missing or invalid default %j',
    (value) => {
      const draft = aiTemplateDraft(readingDefinition);
      draft.parameters[1].required = false;
      draft.parameters[1].defaultValue = value;
      expect(aiTemplateErrors(draft)).not.toEqual([]);
    },
  );

  it('allows a bound count without a default when parent input is required', () => {
    const draft = aiTemplateDraft(readingDefinition);
    draft.parameters[1].defaultValue = '';
    expect(aiTemplateErrors(draft)).toEqual([]);
  });

  it('removes old type settings when a parameter changes type', () => {
    const draft = aiTemplateDraft(readingDefinition);
    const field = draft.parameters[1];
    field.type = 'boolean';
    field.defaultValue = 'false';
    expect(aiTemplateDefinition(draft).instanceParameters[1]).toEqual({
      key: 'count',
      label: field.label,
      type: 'boolean',
      required: true,
      default: false,
    });
  });

  it.each(['theme\n', 'theme\r', 'theme\r\n'])(
    'rejects a key with trailing line breaks: %j',
    (key) => {
      const draft = aiTemplateDraft(readingDefinition);
      draft.parameters[0].key = key;
      expect(aiTemplateErrors(draft)).not.toEqual([]);
    },
  );

  it.each(['', '   '])('preserves the server-valid optional text default %j', (value) => {
    const definition = structuredClone(readingDefinition);
    definition.instanceParameters[0].required = false;
    definition.instanceParameters[0].default = value;
    const draft = aiTemplateDraft(definition);
    expect(aiTemplateErrors(draft)).toEqual([]);
    expect(aiTemplateDefinition(draft)).toEqual(definition);
  });
});
