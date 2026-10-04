import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../../core/api/limits';
import { PlanControl } from '../../../../core/api/models';
import { ControlForm } from '../plan-form';
import type { PlanStructureEdit } from '../plan-editor';
import { FieldDirection } from '../../../../shared/forms/field-direction';

/** The part a choice changes, as its summary names it and its effect field asks about it. */
const partCopy = {
  plan: { owner: 'כל הפעילות', effect: 'איך הבחירה משפיעה על הפעילות?' },
  questions: { owner: 'השאלות', effect: 'איך הבחירה משפיעה על השאלות?' },
  material: { owner: 'הטקסט', effect: 'איך הבחירה משפיעה על הטקסט?' },
};
const typeNames: Record<PlanControl['type'], string> = {
  text: 'טקסט חופשי',
  integer: 'מספר שלם',
  select: 'בחירה מרשימה',
  boolean: 'כן או לא',
};

/**
 * One choice definition as a disclosure whose summary names the choice, its kind, the part it
 * changes and its default. It edits the owner's field tree and never creates draft state.
 */
@Component({
  imports: [FormField, FieldDirection],
  selector: 'app-control-card',
  templateUrl: './control-card.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ControlCard {
  protected readonly limits = inject(Limits).current;
  readonly field = input.required<FieldTree<ControlForm>>();
  /** `plan`, `questions` or the owning material's ID, as structural edits address it. */
  readonly scope = input.required<string>();
  /** The owning text's name, for a choice that changes one text. */
  readonly text = input('');
  readonly open = input(false);
  readonly locked = input(false);
  readonly structureChanged = output<PlanStructureEdit>();
  readonly edited = output<{ key: string }>();
  protected readonly typeNames = typeNames;
  protected readonly part = computed(() => {
    const scope = this.scope();
    return scope === 'plan' || scope === 'questions' ? scope : 'material';
  });
  protected readonly copy = computed(() => partCopy[this.part()]);
  protected readonly defaultText = computed(() => {
    const { type, hasDefault, defaultValue } = this.field()().value();
    if (!hasDefault || !defaultValue) return '';
    return type === 'boolean' ? (defaultValue === 'true' ? 'כן' : 'לא') : defaultValue;
  });
  protected editOption(remove?: number) {
    if (this.locked()) return;
    this.field()
      .options()
      .value.update((values) =>
        remove === undefined
          ? [...values, { value: '', meaning: '' }]
          : values.filter((_, i) => i !== remove),
      );
    this.edited.emit({ key: '' });
  }
}
