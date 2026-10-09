import { documentForm, documentValue } from './document-form';
import { limits } from '../../../core/api/limits.fixture';

describe('Editable document boundary', () => {
  it('keeps incomplete answers and invalid option associations without guessing a replacement', () => {
    const raw = documentForm({
      title: 'תרגול',
      instructions: null,
      materials: [],
      questions: [
        {
          id: 'q1',
          prompt: 'בחרו',
          interaction: { type: 'single-choice', options: ['א', 'ב'] },
          answer: { value: 'א' },
          points: 1,
        },
      ],
    });
    raw.questions[0].options[0].value = 'ג';
    expect(documentValue(raw, limits).value?.questions[0].answer).toEqual({ value: 'א' });
    raw.questions[0].answer = '';
    expect(documentValue(raw, limits).value?.questions[0].answer).toEqual({ value: '' });
  });
  it('preserves raw invalid points and rejects them before HTTP submission', () => {
    const raw = documentForm({
      title: '',
      instructions: '',
      materials: [],
      questions: [
        {
          id: 'q1',
          prompt: '',
          interaction: { type: 'numeric-input', options: null },
          answer: null,
          points: 0,
        },
      ],
    });
    raw.questions[0].points = '1.5';
    expect(documentValue(raw, limits).value).toBeUndefined();
    expect(raw.questions[0].points).toBe('1.5');
    raw.questions[0].points = '0';
    expect(documentValue(raw, limits).value?.questions[0]).toMatchObject({ id: 'q1', points: 0 });
  });
  it('bounds aggregate content and strips snapshot metadata at the editable boundary', () => {
    const snapshot = {
      title: 'x',
      instructions: '',
      materials: [
        {
          id: 'a',
          title: null,
          body: 'א'.repeat(4000),
          revision: 4,
          origin: { kind: 'generated' },
          acceptance: null,
          idea: { premise: 'גילוי', structure: 'מסע' },
        },
        {
          id: 'b',
          title: null,
          body: 'ב'.repeat(4000),
          revision: 1,
          origin: { kind: 'supplied' },
          acceptance: null,
        },
      ],
      questions: [],
    };
    const raw = documentForm(snapshot);
    expect(raw.materials[0]).toEqual({ id: 'a', title: '', body: 'א'.repeat(4000) });
    expect(documentValue(raw, limits).value).toBeUndefined();
    raw.materials[1].body = 'Hello\nשלום!';
    expect(documentValue(raw, limits).value?.materials[1]).toEqual({
      id: 'b',
      title: null,
      body: 'Hello\nשלום!',
    });
  });
});
