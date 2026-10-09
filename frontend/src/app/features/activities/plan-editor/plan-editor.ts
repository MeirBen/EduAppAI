import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../core/api/limits';
import { PlanMaterial } from '../../../core/api/models';
import { PlanForm } from './plan-form';
import { LengthFields } from './length-fields/length-fields';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { focusHolder } from '../../../shared/focus-holder';
import { FieldErrors } from '../../../shared/forms/field-errors';
import { FieldValidity } from '../../../shared/forms/field-validity';

/** Structural edits are applied by the workspace, which also owns history and source acceptance. */
export type PlanStructureEdit = { kind: 'add-material' } | { kind: 'remove-material'; id: string };

/**
 * Advanced concrete requirements: goal, guidance, materials, lengths and question formats.
 * ActivitySetup owns settings and supplied text. Both edit the workspace buffer without HTTP.
 */
@Component({
  imports: [
    FieldValidity,
    FieldErrors,
    DisabledInteractive,
    FormField,
    FieldDirection,
    LengthFields,
  ],
  selector: 'app-plan-editor',
  templateUrl: './plan-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'changed($event)', '(change)': 'changed($event)' },
})
export class PlanEditor {
  protected readonly limits = inject(Limits).current;
  private readonly holdFocus = focusHolder();
  readonly fields = input.required<FieldTree<PlanForm>>();
  readonly locked = input(false);
  readonly edited = output<{ key: string }>();
  readonly structureChanged = output<PlanStructureEdit>();
  /** A combined length only differs from a text's own length when several texts are generated. */
  protected readonly generatedCount = computed(
    () =>
      this.fields()()
        .value()
        .materials.filter((m) => m.source === 'generated').length,
  );
  protected readonly sourceNames: Record<PlanMaterial['source'], string> = {
    generated: 'הטקסט ייכתב בעזרת AI',
    supplied: 'טקסט שסיפקתם',
  };
  /** Removing a text moves focus to adding one; the texts have no other safe control. */
  protected removeMaterial(id: string) {
    const restore = this.holdFocus();
    this.structureChanged.emit({ kind: 'remove-material', id });
    restore('add-material');
  }
  protected changed(event: Event) {
    if (this.locked() || !(event.target instanceof HTMLElement)) return;
    this.edited.emit({ key: event.target.id });
  }
}
