import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apply, form } from '@angular/forms/signals';
import { ActivitySetup } from './activity-setup';
import { PlanEditor } from '../plan-editor/plan-editor';
import { planForm, planFormSchema } from '../plan-editor/plan-form';
import { planValue } from '../plan-editor/plan-projection';
import { numericPlan, suppliedPlan } from '../learning-plan.fixture';
import { LearningPlan } from '../../../core/api/models';
import { limits, provideLimits } from '../../../core/api/limits.fixture';

@Component({
  imports: [ActivitySetup, PlanEditor],
  template: `<app-activity-setup [plan]="fields.plan" [pendingSources]="pending()" />
    <app-plan-editor [fields]="fields.plan" />`,
})
class Host {
  readonly raw = signal({ plan: planForm(numericPlan) });
  readonly fields = form(this.raw, (path) => apply(path.plan, planFormSchema(limits)));
  readonly pending = signal<string[]>([]);
}
async function render(plan: LearningPlan, setup?: (host: Host) => void) {
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.raw.set({ plan: planForm(plan) });
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
      'supplied',
    ]);
    expect(visibleText(root)).not.toMatch(/\b(fixed|per-task|generated|target|exact|range)\b/);
  });

  it('edits the concrete settings in the same plan buffer', async () => {
    const { root, host, fixture } = await render(numericPlan);
    const field = root.querySelector<HTMLInputElement>('#activity-topic')!;
    field.value = 'נושא חדש';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    expect(planValue(host.raw().plan, limits).value?.settings.topic).toBe('נושא חדש');
    expect(root.querySelector('#input-format')).toBeNull();
  });
});
