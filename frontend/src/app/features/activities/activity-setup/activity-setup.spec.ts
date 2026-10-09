import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apply, form } from '@angular/forms/signals';
import { ActivitySetup } from './activity-setup';
import { planForm, planFormSchema } from '../activity-workspace/plan-form';
import { numericPlan, readingPlan, suppliedPlan } from '../learning-plan.fixture';
import { LearningPlan } from '../../../core/api/models';
import { limits, provideLimits } from '../../../core/api/limits.fixture';

@Component({
  imports: [ActivitySetup],
  template: `<app-activity-setup [plan]="fields.plan" [pendingSources]="pending()" />`,
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
    expect(root.querySelector('select')).toBeNull();
    expect(root.textContent).toContain('יש לי טקסט משלי');
    expect(visibleText(root)).not.toMatch(/\b(fixed|per-task|generated|target|exact|range)\b/);
  });

  it('shows settings as a read-only summary', async () => {
    const { root } = await render(numericPlan);
    expect(root.querySelector('input, select')).toBeNull();
    expect(root.textContent).toContain(numericPlan.name);
    expect(root.textContent).toContain(numericPlan.settings.topic);
    expect(root.textContent).toContain(numericPlan.goal);
  });

  it('keeps each text requirement with its source and shows the number of answer choices', async () => {
    const plan = {
      ...readingPlan,
      materials: [
        { ...readingPlan.materials[0], label: 'סיפור', guidance: 'סיפור עם דיאלוג' },
        {
          ...readingPlan.materials[0],
          id: '33333333333333333333333333333333',
          label: 'מידע',
          guidance: 'הסבר עובדתי',
        },
      ],
    };
    const { root } = await render(plan);
    for (const material of plan.materials) {
      const group = root.querySelector(`[role="group"][aria-label="${material.label}"]`)!;
      expect(group.textContent).toContain(material.guidance);
      expect(group.textContent).toContain('בערך 300 מילים');
    }
    expect(root.textContent).toContain('4 אפשרויות לכל שאלת בחירה');
  });
});
