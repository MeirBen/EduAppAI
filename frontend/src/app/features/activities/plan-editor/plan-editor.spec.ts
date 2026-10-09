import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apply, form } from '@angular/forms/signals';
import { PlanEditor, PlanStructureEdit } from './plan-editor';
import { planFormSchema } from './plan-form';
import { editPlanStructure, workspaceForm } from '../activity-workspace/workspace-form';
import { readingPlan } from '../learning-plan.fixture';
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
    this.raw.set(editPlanStructure(this.raw(), edit, limits));
  }
}
async function render(plan: LearningPlan) {
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.raw.set(workspaceForm(plan));
  await fixture.whenStable();
  const root: HTMLElement = fixture.nativeElement;
  return { root, host: fixture.componentInstance, fixture };
}
describe('Plan editor choices', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideLimits()] }));

  it('moves focus from a removed material to the add control', async () => {
    const { root, fixture, host } = await render(readingPlan);
    const remove = Array.from(root.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('הסרת הטקסט'),
    )!;
    remove.focus();
    remove.click();
    await fixture.whenStable();
    expect(host.raw().plan.materials).toHaveLength(0);
    expect((document.activeElement as HTMLElement).id).toBe('add-material');
  });
});
