import { planForm } from './plan-form';
import { planValue } from './plan-projection';
import { limits } from '../../../core/api/limits.fixture';
import { numericPlan, readingPlan, suppliedPlan, sourceText } from '../learning-plan.fixture';

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
  it('rejects unfinished counts without changing the local value', () => {
    const form = planForm(numericPlan);
    form.settings.questionCount = '1.';
    expect(planValue(form, limits).value).toBeUndefined();
    expect(form.settings.questionCount).toBe('1.');
    form.settings.questionCount = String(limits.maxQuestionCount + 1);
    expect(planValue(form, limits).value).toBeUndefined();
  });
  it('requires enough questions for the requested mixture and checks choice bounds', () => {
    const form = planForm(readingPlan);
    form.questions.numeric = true;
    form.settings.questionCount = '1';
    expect(planValue(form, limits).value).toBeUndefined();
    form.settings.questionCount = '2';
    form.questions.choiceCount.value = '7';
    expect(planValue(form, limits).value).toBeUndefined();
    form.questions.choiceCount.value = '2';
    expect(planValue(form, limits).value?.questions.choiceCount).toBe(2);
  });
  it('keeps only applicable length and source fields and rejects conflicting length scopes', () => {
    const form = planForm(readingPlan);
    form.materials[0].text = 'local unused text';
    expect(planValue(form, limits).value?.materials[0].text).toBeNull();
    form.totalLength = { mode: 'range', value: '', lower: '100', upper: '200' };
    expect(planValue(form, limits).value).toBeUndefined();
    form.materials[0].length.mode = '';
    expect(planValue(form, limits).value?.totalLength).toEqual({
      mode: 'range',
      lower: 100,
      upper: 200,
    });
    form.totalLength.upper = '100';
    expect(planValue(form, limits).value).toBeUndefined();
  });
});
