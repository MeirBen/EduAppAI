import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apply, form } from '@angular/forms/signals';
import { ActivitySetup } from './activity-setup';
import { PlanEditor } from '../plan-editor/plan-editor';
import { inputForm, planForm, planFormSchema } from '../plan-editor/plan-form';
import { planValue, requestValue } from '../plan-editor/plan-projection';
import { numericPlan, readingPlan, suppliedPlan } from '../learning-plan.fixture';
import { LearningPlan } from '../../../core/api/models';
import { limits, provideLimits } from '../../../core/api/limits.fixture';

@Component({
  imports: [ActivitySetup, PlanEditor],
  template: `<app-activity-setup
      [plan]="fields.plan"
      [inputs]="fields.input"
      [pendingSources]="pending()"
      [reusable]="reusable()"
    />
    <app-plan-editor [fields]="fields.plan" />`,
})
class Host {
  readonly raw = signal({ plan: planForm(numericPlan), input: inputForm(numericPlan) });
  readonly fields = form(this.raw, (path) => apply(path.plan, planFormSchema(limits)));
  readonly pending = signal<string[]>([]);
  readonly reusable = signal(false);
}
async function render(plan: LearningPlan, setup?: (host: Host) => void) {
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.raw.set({ plan: planForm(plan), input: inputForm(plan) });
  setup?.(fixture.componentInstance);
  await fixture.whenStable();
  return { root: fixture.nativeElement as HTMLElement, host: fixture.componentInstance, fixture };
}
const visibleText = (root: HTMLElement) =>
  Array.from(root.querySelectorAll('label, p, legend, h3, option'))
    .map((element) => element.textContent)
    .join(' ');

describe('Activity setup', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideLimits()] }));
  it('shows supplied-source text in parent language without length fields or invented choices', async () => {
    const { root } = await render(suppliedPlan, (host) =>
      host.pending.set([suppliedPlan.materials[0].id]),
    );
    expect(root.querySelector('textarea[data-source]')?.getAttribute('dir')).toBe('auto');
    expect(root.querySelector('[id$="-length-mode"]')).toBeNull();
    expect(root.querySelector('[id$="-input-words"]')).toBeNull();
    expect(root.querySelector('#choice-count')).toBeNull();
    expect(root.textContent).not.toContain('סוג סיפור');
    expect(root.textContent).toContain('בדקו שהטקסט הועתק נכון');
    expect(root.textContent).toContain('הטקסט יישמר בדיוק כפי שהוזן.');
    const source = root.querySelector<HTMLSelectElement>(
      `[id="${suppliedPlan.materials[0].id}-source"]`,
    )!;
    expect(source.selectedOptions[0].textContent?.trim()).toBe('יש לי טקסט משלי');
    // The per-activity source mode is template vocabulary; it is offered only where it applies.
    expect(Array.from(source.options).map((option) => option.value)).toEqual([
      'generated',
      'fixed',
    ]);
    expect(visibleText(root)).not.toMatch(/\b(fixed|per-task|generated|target|exact|range)\b/);
  });

  it('offers a per-activity source only for reusable templates and keeps confirmation quiet once accepted', async () => {
    const { root } = await render(suppliedPlan, (host) => host.reusable.set(true));
    const source = root.querySelector<HTMLSelectElement>(
      `[id="${suppliedPlan.materials[0].id}-source"]`,
    )!;
    expect(Array.from(source.options).map((option) => option.value)).toContain('per-task');
    expect(root.querySelector(`#confirm-source-${suppliedPlan.materials[0].id}`)).toBeNull();
    expect(root.textContent).not.toContain('בדקו שהטקסט הועתק נכון');
  });

  it('shows a template its defaults and sources but no per-activity values', async () => {
    const id = 'f'.repeat(32);
    const { root } = await render(
      {
        ...readingPlan,
        controls: [{ id, label: 'סגנון', meaning: 'סגנון', type: 'boolean' }],
        materials: [{ ...readingPlan.materials[0], source: 'per-task', length: null }],
      },
      (host) => host.reusable.set(true),
    );
    const setup = root.querySelector('app-activity-setup')!;
    expect(setup.querySelector('#activity-topic')).not.toBeNull();
    expect(setup.textContent).toContain('כל פעילות חדשה מהתבנית מתחילה מהערכים האלה.');
    expect(setup.querySelector('[id$="-source"]')).not.toBeNull();
    for (const selector of ['#input-format', '#input-choice-count', `#${id}-input`, 'textarea'])
      expect(setup.querySelector(selector)).toBeNull();
  });

  it('shows an approximate adjustable length as a word count with its default, not a mode', async () => {
    const { root } = await render(readingPlan);
    const words = root.querySelector<HTMLInputElement>(
      `[id="${readingPlan.materials[0].id}-input-words"]`,
    )!;
    expect(words.placeholder).toBe('300');
    expect(words.getAttribute('dir')).toBe('ltr');
    expect(root.querySelector(`[id="${words.id}-help"]`)?.textContent).toContain('בערך 300 מילים');
    expect(root.querySelector(`label[for="${words.id}"]`)?.textContent).toContain('אורך הטקסט');
    const format = root.querySelector<HTMLSelectElement>('#input-format')!;
    expect(Array.from(format.options).map((option) => option.textContent?.trim())).toEqual([
      'בחירה מתוך אפשרויות',
      'תשובה קצרה',
    ]);
    expect(root.querySelector<HTMLInputElement>('#input-choice-count')!.placeholder).toBe('4');
  });

  it('reads a strict range as a requirement without offering an output tolerance input', async () => {
    const plan: LearningPlan = {
      ...readingPlan,
      materials: [
        { ...readingPlan.materials[0], length: { mode: 'range', lower: 100, upper: 150 } },
      ],
    };
    const { root } = await render(plan);
    expect(root.querySelector('[id$="-input-words"]')).toBeNull();
    expect(root.textContent).toContain('אורך נדרש:');
    expect(root.textContent).toContain('100–150 מילים');
  });

  it('hides passage and source controls for question-only activities and shows a requested number', async () => {
    const id = 'e'.repeat(32);
    const { root } = await render({
      ...numericPlan,
      controls: [
        {
          id,
          label: 'מספר מקסימלי',
          meaning: 'הגבול העליון לתרגילים',
          type: 'integer',
          default: 50,
          min: 10,
          max: 1000,
        },
      ],
    });
    expect(root.querySelector('#setup-content-title')).toBeNull();
    expect(root.querySelector('[id$="-source"]')).toBeNull();
    expect(root.querySelector('[id$="-input-words"]')).toBeNull();
    expect(root.querySelector<HTMLInputElement>(`#${id}-input`)!.placeholder).toBe('50');
    expect(root.querySelector(`#${id}-input-help`)?.textContent).toContain('אפשר לבחור 10–1000');
  });

  it('names the text that each text choice belongs to', async () => {
    const choice = (id: string) => ({
      id: id.repeat(32),
      label: 'רמה',
      meaning: 'רמה',
      type: 'boolean' as const,
    });
    const reading = readingPlan.materials[0];
    const plan: LearningPlan = {
      ...readingPlan,
      controls: [choice('a')],
      materials: [
        { ...reading, controls: [choice('b')] },
        { ...reading, id: '3'.repeat(32), label: 'שיר' },
      ],
      questions: { ...readingPlan.questions, controls: [choice('c')] },
    };
    const { root } = await render(plan);
    const label = (id: string) =>
      root.querySelector(`label[for="${id.repeat(32)}-input"]`)?.textContent?.trim();
    expect(label('a')).toBe('רמה');
    expect(label('b')).toBe('רמה — קטע קריאה');
    expect(label('c')).toBe('רמה');
    expect(
      root.querySelector('app-activity-setup [role="group"][aria-label="שיר"]'),
    ).not.toBeNull();
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
    const { root, host, fixture } = await render(plan);
    for (const [id, value] of [
      ['a', '0'],
      ['b', 'false'],
    ]) {
      const field = root.querySelector<HTMLInputElement | HTMLSelectElement>(
        `#${id.repeat(32)}-input`,
      )!;
      field.value = value;
      field.dispatchEvent(new Event('input', { bubbles: true }));
    }
    root.querySelector<HTMLInputElement>(`#${'c'.repeat(32)}-provided`)!.click();
    await fixture.whenStable();
    const request = requestValue(plan, host.raw().input, limits);
    expect(request.errors).toEqual([]);
    expect(request.value?.controlValues).toEqual({
      ['a'.repeat(32)]: 0,
      ['b'.repeat(32)]: false,
      ['c'.repeat(32)]: '',
    });
    expect(
      planValue(host.raw().plan, host.raw().input.settings, limits).value?.controls.map(
        (control) => control.default,
      ),
    ).toEqual([4, true, 'רגיל', undefined]);
  });
});
