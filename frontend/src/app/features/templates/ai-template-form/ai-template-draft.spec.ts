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

  it.each(['', '   '])('preserves the server-valid optional text default %j', (value) => {
    const definition = structuredClone(readingDefinition);
    definition.instanceParameters[0].required = false;
    definition.instanceParameters[0].default = value;
    const draft = aiTemplateDraft(definition);
    expect(aiTemplateErrors(draft)).toEqual([]);
    expect(aiTemplateDefinition(draft)).toEqual(definition);
  });
});
