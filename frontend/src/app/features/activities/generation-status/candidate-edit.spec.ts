import { candidateEdit } from './candidate-edit';
import { documentForm } from '../activity-document-editor/document-form';
import { numericPlan } from '../learning-plan.fixture';
import { limits } from '../../../core/api/limits.fixture';
describe('Diagnostic candidate editing', () => {
  const current = documentForm({
    title: 'לשמור',
    instructions: 'הוראות',
    materials: [],
    questions: [
      {
        id: 'q',
        prompt: 'ישן',
        interaction: { type: 'numeric-input', options: null },
        answer: { value: '1' },
        points: 1,
      },
    ],
  });
  const question = {
    prompt: 'חדש',
    interaction: { type: 'numeric-input', options: null },
    answer: { value: '2' },
    points: 3,
  };
  it('replaces one whole question while preserving the local title and server-owned identity', () => {
    const result = candidateEdit('replace-question', question, 'q', current, numericPlan, limits);
    expect(result?.title).toBe('לשמור');
    expect(result?.questions[0]).toMatchObject({
      id: 'q',
      prompt: 'חדש',
      answer: '2',
      points: '3',
    });
  });
  it.each([
    'not JSON',
    { ...question, prompt: 'א'.repeat(501) },
    { ...question, points: '3' },
    { ...question, interaction: { type: 'html' } },
  ])('keeps unsafe or unparseable output diagnostic-only', (candidate) => {
    expect(
      candidateEdit('replace-question', candidate, 'q', current, numericPlan, limits),
    ).toBeUndefined();
  });
  it('cannot replace a supplied source or an unrelated target', () => {
    expect(
      candidateEdit(
        'replace-material',
        { id: 'source', body: 'שונה' },
        'source',
        current,
        {
          ...numericPlan,
          materials: [
            {
              id: 'source',
              source: 'fixed',
              label: 'מקור',
              text: 'מקורי',
              guidance: '',
              controls: [],
            },
          ],
        },
        limits,
      ),
    ).toBeUndefined();
    expect(
      candidateEdit('replace-question', question, 'missing', current, numericPlan, limits),
    ).toBeUndefined();
  });
});
