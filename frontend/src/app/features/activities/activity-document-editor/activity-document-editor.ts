import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { PlanMaterial } from '../../../core/api/models';
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
  imports: [FormField],
  templateUrl: './activity-document-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'onEdit($event)', '(change)': 'onEdit($event)' },
})
export class ActivityDocumentEditor {
  readonly fields = input.required<FieldTree<DocumentForm>>();
  readonly materials = input.required<Pick<PlanMaterial, 'id' | 'source' | 'label'>[]>();
  readonly locked = input(false);
  readonly aiAvailable = input(false);
  readonly operationActive = input(false);
  readonly edited = output<{ key: string }>();
  readonly structureChanged = output<DocumentEdit>();
  readonly replaced = output<{ kind: 'ReplaceMaterial' | 'ReplaceQuestion'; targetId: string }>();
  readonly adopted = output<{ materialIds: string[]; questionIds: string[] }>();
  readonly sourceReplaced = output<string>();
  protected missing(id: string) {
    return !this.fields()
      .materials()
      .value()
      .some((m) => m.id === id);
  }
  protected generated(id: string) {
    return this.materials().find((m) => m.id === id)?.source === 'generated';
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
}
