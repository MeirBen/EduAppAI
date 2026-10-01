import { numericPlan, suppliedPlan, sourceText } from '../learning-plan.fixture';
import { planForm, planValue, requestValue, inputForm } from './plan-form';
import { LearningPlan } from '../../../core/api/models';

describe('Plan form boundary', () => {
  it('omits integer-only metadata after changing a requested choice to text', () => {
    const form = planForm({
      ...numericPlan,
      controls: [
        {
          id: 'a'.repeat(32),
          label: 'משך',
          meaning: 'משך הפעילות',
          type: 'integer',
          unit: 'דקות',
          min: 0,
          max: 10,
        },
      ],
    });
    form.controls[0].type = 'text';
    const control = planValue(form).value?.controls[0];
    expect(control).not.toHaveProperty('unit');
    expect(control).not.toHaveProperty('min');
    expect(control).not.toHaveProperty('max');
    expect(form.controls[0].unit).toBe('דקות');
  });
  it('preserves accepted source text and the server version without normalizing content', () => {
    const form = planForm({ ...suppliedPlan, schemaVersion: 23 });
    expect(planValue(form).value?.materials[0].text).toBe(sourceText);
    expect(planValue(form).value?.schemaVersion).toBe(23);
    form.settings.questionCount = '';
    expect(planValue(form).value).toBeUndefined();
    expect(form.settings.questionCount).toBe('');
  });

  it('maps blanks to omission, preserving explicit empty text, false and zero', () => {
    const plan: LearningPlan = {
      ...numericPlan,
      controls: [
        {
          id: 'a'.repeat(32),
          label: 'טקסט',
          type: 'text',
          meaning: 'פרט',
          required: false,
          default: 'רגיל',
        },
        {
          id: 'b'.repeat(32),
          label: 'מספר',
          type: 'integer',
          meaning: 'היסט',
          required: false,
          default: 2,
        },
        {
          id: 'c'.repeat(32),
          label: 'כן',
          type: 'boolean',
          meaning: 'הצגה',
          required: false,
          default: true,
        },
        {
          id: 'd'.repeat(32),
          label: 'בחירה',
          type: 'select',
          meaning: 'סוג',
          required: false,
          options: [{ value: 'א' }],
        },
      ],
    };
    const form = inputForm(plan);
    form.controls[0].provided = true;
    form.controls[0].value = '';
    form.controls[1].value = '0';
    form.controls[2].value = 'false';
    expect(requestValue(plan, form).value?.controlValues).toEqual({
      ['a'.repeat(32)]: '',
      ['b'.repeat(32)]: 0,
      ['c'.repeat(32)]: false,
    });
    form.controls[1].value = '';
    expect(requestValue(plan, form).value?.controlValues).not.toHaveProperty('b'.repeat(32));
    form.controls[1].value = '1.5';
    expect(requestValue(plan, form).value).toBeUndefined();
  });

  it('keeps form-only values out of conditional source, format and length contracts', () => {
    const form = planForm(suppliedPlan);
    const material = form.materials[0];
    material.source = 'generated';
    material.length.mode = 'target';
    material.length.value = '120';
    material.length.adjustable = false;
    material.length.min = 'nonsense';
    const generated = planValue(form).value!;
    expect(generated.materials[0].text).toBeNull();
    expect(generated.materials[0].length).toEqual({
      mode: 'target',
      count: { value: 120, adjustable: false },
    });
    material.source = 'per-task';
    const perTask = planValue(form).value!;
    expect(perTask.materials[0].length).toBeNull();
    const inputs = inputForm(perTask);
    expect(requestValue(perTask, inputs).value).toBeUndefined();
    inputs.materials[0].sourceText = sourceText;
    inputs.materials[0].wordCount = '999';
    expect(requestValue(perTask, inputs).value?.materialInputs).toEqual({
      [material.id]: { sourceText },
    });
  });

  it('rejects invalid defaults and keeps zero, false and explicit empty defaults distinct', () => {
    const plan: LearningPlan = {
      ...numericPlan,
      controls: [
        { id: 'a'.repeat(32), label: 'טקסט', type: 'text', meaning: 'פרט', default: '' },
        { id: 'b'.repeat(32), label: 'מספר', type: 'integer', meaning: 'היסט', default: 0 },
        { id: 'c'.repeat(32), label: 'כן', type: 'boolean', meaning: 'הצגה', default: false },
      ],
    };
    const form = planForm(plan);
    expect(planValue(form).value?.controls.map((c) => c.default)).toEqual(['', 0, false]);
    form.controls[0].type = 'select';
    form.controls[0].options = [{ value: 'חדש', meaning: '' }];
    form.controls[0].defaultValue = 'לא קיים';
    expect(planValue(form).value).toBeUndefined();
    form.controls[0].defaultValue = 'חדש';
    expect(planValue(form).value?.controls[0].default).toBe('חדש');
  });
});
