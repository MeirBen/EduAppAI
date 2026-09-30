import { readingDefinition } from './ai-template.fixture';
import { aiTemplateDefinition, aiTemplateDraft, aiTemplateErrors } from './ai-template-draft';

describe('AI blueprint editor', () => {
  it('round-trips all four parameter types, false defaults and question count without mutation', () => {
    const original = structuredClone(readingDefinition);
    const draft = aiTemplateDraft(readingDefinition);
    expect(aiTemplateErrors(draft)).toEqual([]);
    expect(aiTemplateDefinition(draft)).toEqual(readingDefinition);
    draft.parameters[0].label = 'נושא חדש';
    expect(readingDefinition).toEqual(original);
  });

  it('rejects duplicate keys and invalid defaults', () => {
    const draft = aiTemplateDraft(readingDefinition);
    draft.parameters[0].key = 'paragraphs';
    draft.parameters[1].defaultValue = '1.5';
    expect(aiTemplateErrors(draft).length).toBeGreaterThanOrEqual(2);
  });

  it('does not coerce an empty numeric default to zero', () => {
    const draft = aiTemplateDraft(readingDefinition);
    draft.parameters[1].defaultValue = '';
    expect(aiTemplateDefinition(draft).instanceParameters[1].default).toBeUndefined();
  });

  it.each(['topic', 'audience', 'difficulty', 'questionCount'])(
    'rejects reserved key %s',
    (key) => {
      const draft = aiTemplateDraft(readingDefinition);
      draft.parameters[0].key = key;
      expect(aiTemplateErrors(draft)).not.toEqual([]);
    },
  );

  it('removes old type settings when a parameter changes type', () => {
    const draft = aiTemplateDraft(readingDefinition);
    const field = draft.parameters[1];
    field.type = 'boolean';
    field.defaultValue = 'false';
    expect(aiTemplateDefinition(draft).instanceParameters[1]).toEqual({
      key: 'paragraphs',
      label: field.label,
      type: 'boolean',
      required: true,
      default: false,
    });
  });

  it.each(['sourceText\n', 'sourceText\r', 'sourceText\r\n'])(
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
