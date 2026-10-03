import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../../core/api/limits';
import { ControlForm } from '../plan-form';
import type { PlanStructureEdit } from '../plan-editor';

/** The block repeats once per part a choice can belong to, so its copy names that part. */
const partCopy = {
  plan: { add: 'הוספת בחירה לפעילות כולה', effect: 'איך הבחירה משפיעה על הפעילות?' },
  questions: { add: 'הוספת בחירה לשאלות', effect: 'איך הבחירה משפיעה על השאלות?' },
  material: { add: 'הוספת בחירה לטקסט הזה', effect: 'איך הבחירה משפיעה על הטקסט?' },
};

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
  /** Any other scope is a material ID; its choices are headed one level below the material. */
  protected readonly part = computed(() => {
    const scope = this.scope();
    return scope === 'plan' || scope === 'questions' ? scope : 'material';
  });
  protected readonly copy = computed(() => partCopy[this.part()]);
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
