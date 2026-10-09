import { planForm } from './plan-form';
import { planValue } from './plan-projection';
import { limits } from '../../../core/api/limits.fixture';
import { readingPlan, suppliedPlan, sourceText } from '../learning-plan.fixture';

describe('Concrete plan projection', () => {
  it('preserves exact source text and empty optional guidance without an override object', () => {
    const form = planForm(suppliedPlan);
    const result = planValue(form, limits);
    expect(result.errors).toEqual([]);
    expect(result.value?.materials[0].text).toBe(sourceText);
    expect(result.value?.guidance).toBe('');
    expect(result.value?.settings).toEqual(suppliedPlan.settings);
    expect(result.value).not.toHaveProperty('defaults');
    expect(result.value).not.toHaveProperty('controls');
  });
  it('round-trips read-only settings and formats exactly as the server returned them', () => {
    const plan = {
      ...readingPlan,
      questions: { ...readingPlan.questions, formats: ['single-choice', 'numeric-input'] as const },
    };
    const input = {
      ...plan,
      questions: { ...plan.questions, formats: [...plan.questions.formats] },
    };
    expect(planValue(planForm(input), limits).value).toEqual(input);
  });
  it('requires a bounded, nonblank supplied source without trimming it', () => {
    const form = planForm(suppliedPlan);
    for (const text of ['', '  ', 'א'.repeat(limits.bodyLength + 1)]) {
      form.materials[0].text = text;
      expect(planValue(form, limits).value).toBeUndefined();
      expect(form.materials[0].text).toBe(text);
    }
  });
});
