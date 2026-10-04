import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../../core/api/limits';
import { formFormats, newPlanId, PlanForm } from '../plan-form';
import type { PlanStructureEdit } from '../plan-editor';
import { ControlCard } from '../control-card/control-card';
import { focusHolder } from '../../../../shared/focus-holder';
import { DisabledInteractive } from '../../../../shared/disabled-interactive';
import { FieldErrors } from '../../../../shared/forms/field-errors';
import { FieldValidity } from '../../../../shared/forms/field-validity';

/**
 * Everything a parent may change per activity, in one place: the adjustable length, option-count
 * and format requirements, and the plan's choices at every scope. Structural edits go to the owner;
 * picking the part a new choice joins is not an edit, so its events never reach change tracking.
 */
@Component({
  imports: [FieldValidity, FieldErrors, DisabledInteractive, FormField, ControlCard],
  selector: 'app-choice-definitions',
  templateUrl: './choice-definitions.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChoiceDefinitions {
  protected readonly limits = inject(Limits).current;
  private readonly holdFocus = focusHolder();
  readonly fields = input.required<FieldTree<PlanForm>>();
  readonly locked = input(false);
  readonly structureChanged = output<PlanStructureEdit>();
  readonly edited = output<{ key: string }>();
  protected readonly plan = computed(() => this.fields()().value());
  /** With several texts, a text's length row names it. */
  protected readonly severalTexts = computed(() => this.plan().materials.length > 1);
  /** Only a generated text's approximate length can follow a per-activity word count. */
  protected readonly lengths = computed(() =>
    this.plan()
      .materials.map((material, index) => ({ material, index }))
      .filter(
        ({ material }) => material.source === 'generated' && material.length.mode === 'target',
      ),
  );
  /** One format per activity only matters when the plan offers several. */
  protected readonly formatCount = computed(() => formFormats(this.plan().questions).length);
  protected readonly hasRequirements = computed(() => {
    const { totalLength, questions } = this.plan();
    return (
      !!this.lengths().length ||
      totalLength.mode === 'target' ||
      questions.choice ||
      questions.selectableFormat ||
      this.formatCount() > 1
    );
  });
  /** Every choice in canonical order, with the scope its structural edits address. */
  protected readonly choices = computed(() => {
    const fields = this.fields(),
      { controls, materials, questions } = this.plan();
    return [
      ...controls.map((c, i) => ({ id: c.id, scope: 'plan', text: '', field: fields.controls[i] })),
      ...materials.flatMap((material, m) =>
        material.controls.map((c, i) => ({
          id: c.id,
          scope: material.id,
          text: material.label,
          field: fields.materials[m].controls[i],
        })),
      ),
      ...questions.controls.map((c, i) => ({
        id: c.id,
        scope: 'questions',
        text: '',
        field: fields.questions.controls[i],
      })),
    ];
  });
  protected readonly scopes = computed(() => [
    { value: 'plan', label: 'כל הפעילות' },
    ...this.plan().materials.map((m) => ({ value: m.id, label: `הטקסט ${m.label || 'טקסט חדש'}` })),
    { value: 'questions', label: 'השאלות' },
  ]);
  /** The choice just added opens for editing; loaded and proposed ones start as summaries. */
  protected readonly opened = signal('');

  /** Removes the choice at `index`; focus moves to its neighbour's summary, else to adding one. */
  protected remove(edit: PlanStructureEdit, index: number) {
    const choices = this.choices(),
      neighbour = choices[index + 1] ?? choices[index - 1];
    const restore = this.holdFocus();
    this.structureChanged.emit(edit);
    restore(neighbour ? neighbour.id + '-summary' : 'add-choice');
  }
  /** Adds a choice to `scope`, a value of `scopes`; focus stays on the add button. */
  protected add(scope: string) {
    const id = newPlanId();
    this.structureChanged.emit({ kind: 'add-control', scope, id });
    this.opened.set(id);
  }
}
