import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../core/api/limits';
import { PlanMaterial } from '../../../core/api/models';
import { TaskSettingsFields } from '../../../shared/forms/task-settings-fields';
import { MaterialForm, PlanForm } from './plan-form';
import { ControlFields } from './control-fields/control-fields';
import { LengthFields } from './length-fields/length-fields';

/** Structural edits are applied by the workspace, which also owns history and source acceptance. */
export type PlanStructureEdit =
  | { kind: 'add-material' }
  | { kind: 'remove-material'; id: string }
  | { kind: 'add-control'; scope: string }
  | { kind: 'remove-control'; scope: string; id: string };

/**
 * Advanced, reusable plan definition: goal, guidance, defaults, structure, length policy and choice
 * definitions. Per-activity choices and source text live in ActivitySetup. No HTTP or copied draft.
 */
@Component({
  imports: [FormField, TaskSettingsFields, ControlFields, LengthFields],
  selector: 'app-plan-editor',
  templateUrl: './plan-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'changed($event)', '(change)': 'changed($event)' },
})
export class PlanEditor {
  protected readonly isGenerated = (material: MaterialForm) => material.source === 'generated';
  protected readonly sourceNames: Record<PlanMaterial['source'], string> = {
    generated: 'הטקסט ייכתב בעזרת AI',
    fixed: 'טקסט קבוע שסיפקתם',
    'per-task': 'טקסט חדש שתזינו בכל פעילות',
  };
  readonly fields = input.required<FieldTree<PlanForm>>();
  protected readonly limits = inject(Limits).current;
  readonly locked = input(false);
  readonly edited = output<{ key: string }>();
  readonly structureChanged = output<PlanStructureEdit>();
  protected changed(event: Event) {
    if (this.locked() || !(event.target instanceof HTMLElement)) return;
    this.edited.emit({ key: event.target.id });
  }
}
