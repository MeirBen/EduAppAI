import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apply, form } from '@angular/forms/signals';
import { PlanEditor, PlanStructureEdit } from './plan-editor';
import { planFormSchema } from './plan-form';
import { editPlanStructure, workspaceForm } from '../activity-workspace/workspace-form';
import { numericPlan, readingPlan } from '../learning-plan.fixture';
import { LearningPlan } from '../../../core/api/models';
import { limits, provideLimits } from '../../../core/api/limits.fixture';

@Component({
  imports: [PlanEditor],
  template: `<app-plan-editor
    [fields]="fields.plan"
    (edited)="edits = edits + 1"
    (structureChanged)="change($event)"
  />`,
})
class Host {
  readonly raw = signal(workspaceForm(readingPlan));
  readonly fields = form(this.raw, (path) => apply(path.plan, planFormSchema(limits)));
  edits = 0;
  change(edit: PlanStructureEdit) {
    const next = editPlanStructure(this.raw(), edit, limits);
    if (next) this.raw.set(next);
  }
}
async function render(plan: LearningPlan) {
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.raw.set(workspaceForm(plan));
  await fixture.whenStable();
  const root: HTMLElement = fixture.nativeElement;
  return { root, host: fixture.componentInstance, fixture, section: choices(root) };
}
const choices = (root: HTMLElement) =>
  root.querySelector<HTMLElement>('section[aria-labelledby="choices-title"]')!;
const choice = (id: string, meaning = 'רמה') => ({
  id: id.repeat(32),
  label: 'רמה',
  meaning,
  type: 'boolean' as const,
});
const material = readingPlan.materials[0];

describe('Plan editor choices', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideLimits()] }));

  it('gathers every per-activity choice and adjustable requirement in one section', async () => {
    const { root, section } = await render({
      ...readingPlan,
      controls: [choice('a')],
      materials: [{ ...material, controls: [choice('b')] }],
      questions: { ...readingPlan.questions, controls: [choice('c')] },
    });
    for (const id of [`${material.id}-adjustable`, 'choice-adjustable', 'question-selectable'])
      expect(section.querySelector(`[id="${id}"]`)).not.toBeNull();
    const meanings = Array.from(root.querySelectorAll('[id$="-meaning"]'));
    expect(meanings).toHaveLength(3);
    expect(meanings.every((meaning) => section.contains(meaning))).toBe(true);
    const owners = Array.from(section.querySelectorAll('details > summary'), (summary) =>
      summary.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(owners[0]).toContain('כל הפעילות');
    expect(owners[1]).toContain('הטקסט קטע קריאה');
    expect(owners[2]).toContain('השאלות');
  });

  it('offers only the adjustable requirements that apply', async () => {
    const { section } = await render(numericPlan);
    expect(section.querySelector('fieldset')).toBeNull();
    expect(section.querySelector('#add-choice')).not.toBeNull();
  });

  it('adds a choice to the chosen part, opens it and focuses its name', async () => {
    const { root, host, fixture } = await render(readingPlan);
    const scope = root.querySelector<HTMLSelectElement>('#choice-scope')!;
    scope.value = material.id;
    scope.dispatchEvent(new Event('change', { bubbles: true }));
    await fixture.whenStable();
    // Picking where a choice goes is not a plan edit; it must not reach Undo or cancel a request.
    expect(host.edits).toBe(0);
    root.querySelector<HTMLButtonElement>('#add-choice')!.click();
    await fixture.whenStable();
    const [added] = host.raw().plan.materials[0].controls;
    expect(added).toBeDefined();
    const name = root.querySelector<HTMLInputElement>(`[id="${added.id}-label"]`)!;
    expect(name.closest('details')!.open).toBe(true);
    expect(document.activeElement).toBe(name);
    // An open card shows its own fields, so a new blank choice is not flagged as a mistake.
    expect(name.closest('details')!.querySelector('summary')!.textContent).not.toContain('יש לתקן');
  });

  it('marks an invalid collapsed choice in its summary', async () => {
    const { section } = await render({ ...numericPlan, controls: [choice('a', '')] });
    const card = section.querySelector('details')!;
    expect(card.open).toBe(false);
    expect(card.querySelector('summary')!.textContent).toContain('יש לתקן את הבחירה');
  });
});
