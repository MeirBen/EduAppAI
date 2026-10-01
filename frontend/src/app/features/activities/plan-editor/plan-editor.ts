import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { TaskSettingsFields } from '../../../shared/forms/task-settings-fields';
import { InputForm, MaterialForm, PlanForm } from './plan-form';
import { ControlFields } from './control-fields/control-fields';
import { LengthFields } from './length-fields/length-fields';

/** Structural edits are applied by the workspace, which also owns history and source acceptance. */
export type PlanStructureEdit =
  | { kind: 'add-material' }
  | { kind: 'remove-material'; id: string }
  | { kind: 'add-control'; scope: string }
  | { kind: 'remove-control'; scope: string; id: string };

/** Native presentation of app-owned plan fields; no HTTP, copied draft or authoritative content validation. */
@Component({
  imports: [FormField, TaskSettingsFields, ControlFields, LengthFields],
  selector: 'app-plan-editor',
  templateUrl: './plan-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'changed($event)', '(change)': 'changed($event)' },
})
export class PlanEditor {
  protected readonly isGenerated = (material: MaterialForm) => material.source === 'generated';
  readonly fields = input.required<FieldTree<PlanForm>>();
  readonly inputFields = input.required<FieldTree<InputForm>>();
  protected readonly controls = computed(() => {
    const plan = this.fields()().value();
    return [
      ...plan.controls,
      ...plan.materials.flatMap((material) => material.controls),
      ...plan.questions.controls,
    ];
  });
  readonly locked = input(false);
  readonly pendingSources = input<string[]>([]);
  readonly edited = output<{ key: string; sourceId?: string }>();
  readonly structureChanged = output<PlanStructureEdit>();
  readonly sourceConfirmed = output<string>();
  protected changed(event: Event) {
    if (this.locked() || !(event.target instanceof HTMLElement)) return;
    this.edited.emit({ key: event.target.id, sourceId: event.target.dataset['source'] });
  }
}
