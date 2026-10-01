import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apply, form } from '@angular/forms/signals';
import { PlanEditor } from './plan-editor';
import { inputForm, planForm, planFormSchema, planValue, requestValue } from './plan-form';
import { numericPlan, suppliedPlan } from '../learning-plan.fixture';
import { LearningPlan } from '../../../core/api/models';

@Component({
  imports: [PlanEditor],
  template: '<app-plan-editor [fields]="fields.plan" [inputFields]="fields.input" />',
})
class Host {
  readonly raw = signal({ plan: planForm(numericPlan), input: inputForm(numericPlan) });
  readonly fields = form(this.raw, (path) => apply(path.plan, planFormSchema));
}
describe('Native plan controls', () => {
  it('shows supplied-source text without generated length fields or invented story choices', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.raw.set({
      plan: planForm(suppliedPlan),
      input: inputForm(suppliedPlan),
    });
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('textarea[data-source]')?.getAttribute('dir')).toBe('auto');
    expect(root.querySelector('[id$="-length-mode"]')).toBeNull();
    expect(root.querySelector('#choice-count')).toBeNull();
    expect(root.textContent).not.toContain('סוג סיפור');
  });

  it('keeps native input false/zero/empty text and omits blank selects from the request', async () => {
    const plan: LearningPlan = {
      ...numericPlan,
      controls: [
        { id: 'a'.repeat(32), label: 'היסט', meaning: 'היסט', type: 'integer', default: 4 },
        { id: 'b'.repeat(32), label: 'רמז', meaning: 'רמז', type: 'boolean', default: true },
        { id: 'c'.repeat(32), label: 'טקסט', meaning: 'תוספת', type: 'text', default: 'רגיל' },
        {
          id: 'd'.repeat(32),
          label: 'סוג',
          meaning: 'סוג',
          type: 'select',
          options: [{ value: 'א' }],
        },
      ],
    };
    const fixture = TestBed.createComponent(Host),
      component = fixture.componentInstance;
    component.raw.set({ plan: planForm(plan), input: inputForm(plan) });
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    for (const [id, value, event] of [
      ['a', '0', 'input'],
      ['b', 'false', 'input'],
    ]) {
      const field = root.querySelector<HTMLInputElement | HTMLSelectElement>(
        `#${id.repeat(32)}-input`,
      )!;
      field.value = value;
      field.dispatchEvent(new Event(event, { bubbles: true }));
    }
    root.querySelector<HTMLInputElement>(`#${'c'.repeat(32)}-provided`)!.click();
    await fixture.whenStable();
    const request = requestValue(plan, component.raw().input);
    expect(request.errors).toEqual([]);
    expect(request.value?.controlValues).toEqual({
      ['a'.repeat(32)]: 0,
      ['b'.repeat(32)]: false,
      ['c'.repeat(32)]: '',
    });
    expect(
      planValue(component.raw().plan).value?.controls.map((control) => control.default),
    ).toEqual([4, true, 'רגיל', undefined]);
  });
});
