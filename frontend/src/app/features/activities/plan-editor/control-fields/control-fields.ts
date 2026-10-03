import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../../core/api/limits';
import { ControlForm } from '../plan-form';
import type { PlanStructureEdit } from '../plan-editor';

/** Reused at plan/material/question scope. It edits the owner's field tree and never creates draft state. */
@Component({
  imports: [FormField],
  selector: 'app-control-fields',
  templateUrl: './control-fields.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ControlFields {
  protected readonly limits = inject(Limits).current;
  readonly fields = input.required<FieldTree<ControlForm[]>>();
  readonly scope = input.required<string>();
  readonly locked = input(false);
  readonly structureChanged = output<PlanStructureEdit>();
  readonly edited = output<{ key: string }>();
  protected editOption(index: number, remove?: number) {
    if (this.locked()) return;
    const options = this.fields()[index].options().value;
    options.update((values) =>
      remove === undefined
        ? [...values, { value: '', meaning: '' }]
        : values.filter((_, i) => i !== remove),
    );
    this.edited.emit({ key: '' });
  }
}
