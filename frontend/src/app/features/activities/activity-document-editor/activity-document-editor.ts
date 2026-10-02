import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { PlanMaterial } from '../../../core/api/models';
import { maxQuestionCount } from '../../../shared/forms/task-settings';
import { ScopedRepair } from '../scoped-repair/scoped-repair';
import { DocumentForm } from './document-form';

/** Structural edits remain in the route owner. New questions receive server IDs on save. */
export type DocumentEdit =
  | { kind: 'add-question' }
  | { kind: 'remove-question' | 'move-up' | 'move-down' | 'add-option'; index: number }
  | { kind: 'remove-option'; index: number; option: number }
  | { kind: 'add-material'; id: string };
/** Presentation only: edits the owner's native fields and emits explicit scoped actions. */
@Component({
  selector: 'app-activity-document-editor',
  imports: [FormField, ScopedRepair],
  templateUrl: './activity-document-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'onEdit($event)', '(change)': 'onEdit($event)' },
})
export class ActivityDocumentEditor {
  readonly fields = input.required<FieldTree<DocumentForm>>();
  readonly materials = input.required<Pick<PlanMaterial, 'id' | 'source' | 'label'>[]>();
  readonly locked = input(false);
  protected readonly maxQuestionCount = maxQuestionCount;
  readonly aiAvailable = input(false);
  readonly operationActive = input(false);
  /** No content yet: the editor stays available for manual writing without dominating the page. */
  readonly empty = input(false);
  /**
   * Saved content whose server diagnostics ask for review under changed requirements. Adoption is
   * offered only here: for content that is already current it changes nothing release checks read.
   */
  readonly staleMaterials = input<ReadonlySet<string>>(new Set());
  readonly staleQuestions = input<ReadonlySet<string>>(new Set());
  /** Target of the running scoped operation, if any. */
  readonly activeTarget = input<string | null>(null);
  readonly edited = output<{ key: string }>();
  readonly structureChanged = output<DocumentEdit>();
  readonly replaced = output<{
    kind: 'ReplaceMaterial' | 'ReplaceQuestion';
    targetId: string;
    instruction: string;
  }>();
  readonly adopted = output<{ materialIds: string[]; questionIds: string[] }>();
  readonly sourceReplaced = output<string>();
  protected missing(id: string) {
    return !this.fields()
      .materials()
      .value()
      .some((m) => m.id === id);
  }
  protected generated(id: string) {
    return this.material(id)?.source === 'generated';
  }
  protected label(id: string) {
    return this.material(id)?.label || 'טקסט';
  }
  /** An answer is never re-pointed automatically; a mismatch stays visible until the parent chooses. */
  protected matches(question: DocumentForm['questions'][number]) {
    return question.options.some((option) => option.value === question.answer);
  }
  protected onEdit(event: Event) {
    const field = event.target;
    if (
      field instanceof HTMLInputElement ||
      field instanceof HTMLTextAreaElement ||
      field instanceof HTMLSelectElement
    )
      this.edited.emit({ key: field.id });
  }
  private material(id: string) {
    return this.materials().find((m) => m.id === id);
  }
}
