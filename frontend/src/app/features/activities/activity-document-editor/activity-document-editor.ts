import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { PlanMaterial } from '../../../core/api/models';
import { ContentIssues, questionFormatLabels } from '../activity-presentation';
import { DocumentForm } from './document-form';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { FieldErrors } from '../../../shared/forms/field-errors';
import { FieldValidity } from '../../../shared/forms/field-validity';

import { MeasurementItem } from '../activity-document-view/measurements';
import { MeasurementList } from '../measurement-list/measurement-list';

/** Presentation only: edits the owner's native fields and emits explicit scoped actions. */
@Component({
  selector: 'app-activity-document-editor',
  imports: [
    MeasurementList,
    FieldValidity,
    FieldErrors,
    DisabledInteractive,
    FormField,
    FieldDirection,
  ],
  templateUrl: './activity-document-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'onEdit($event)', '(change)': 'onEdit($event)' },
})
export class ActivityDocumentEditor {
  protected readonly questionFormatLabels = questionFormatLabels;
  readonly measurements = input<MeasurementItem[]>([]);
  readonly fields = input.required<FieldTree<DocumentForm>>();
  readonly materials = input.required<Pick<PlanMaterial, 'id' | 'source' | 'label'>[]>();
  readonly locked = input(false);
  /**
   * Saved content whose server diagnostics ask for review under changed requirements. Adoption is
   * offered only here: for content that is already current it changes nothing release checks read.
   */
  readonly staleMaterials = input<ReadonlySet<string>>(new Set());
  /** Release problems the saved check found, each shown at its field. */
  readonly contentIssues = input<ContentIssues>({ materials: new Map(), questions: new Map() });
  readonly finished = output<void>();
  readonly edited = output<{ key: string }>();
  readonly adopted = output<{ materialIds: string[]; questionIds: string[] }>();
  readonly sourceReplaced = output<string>();
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
