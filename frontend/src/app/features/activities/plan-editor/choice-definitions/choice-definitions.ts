import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DOCUMENT,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  signal,
} from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../../core/api/limits';
import { formFormats, PlanForm } from '../plan-form';
import type { PlanStructureEdit } from '../plan-editor';
import { ControlCard } from '../control-card/control-card';

/**
 * Everything a parent may change per activity, in one place: the adjustable length, option-count
 * and format requirements, and the plan's choices at every scope. Structural edits go to the owner;
 * the part a new choice will join is local state, not an edit.
 */
@Component({
  imports: [FormField, ControlCard],
  selector: 'app-choice-definitions',
  templateUrl: './choice-definitions.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChoiceDefinitions {
  protected readonly limits = inject(Limits).current;
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
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
  /** The part a new choice joins; it falls back to the whole activity when its text is removed. */
  protected readonly scope = linkedSignal({
    source: this.scopes,
    computation: (scopes, previous?: { value: string }) =>
      previous && scopes.some((scope) => scope.value === previous.value) ? previous.value : 'plan',
  });
  /** Choices added here open for editing; loaded and proposed ones start as summaries. */
  protected readonly added = signal<ReadonlySet<string>>(new Set());

  protected add() {
    if (this.locked()) return;
    const known = new Set(this.choices().map((choice) => choice.id));
    this.structureChanged.emit({ kind: 'add-control', scope: this.scope() });
    // The owner applies structural edits synchronously, so the new choice is the unknown identity.
    const id = this.choices().find((choice) => !known.has(choice.id))?.id;
    if (!id) return;
    this.added.update((ids) => new Set(ids).add(id));
    afterNextRender(() => this.document.getElementById(id + '-label')?.focus(), {
      injector: this.injector,
    });
  }
}
