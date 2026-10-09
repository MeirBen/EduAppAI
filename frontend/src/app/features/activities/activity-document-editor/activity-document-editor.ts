import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output, TemplateRef } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { PlanMaterial } from '../../../core/api/models';
import { ScopedRepair } from '../scoped-repair/scoped-repair';
import { ContentIssues } from '../activity-presentation';
import { DocumentForm } from './document-form';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { FieldErrors } from '../../../shared/forms/field-errors';
import { FieldValidity } from '../../../shared/forms/field-validity';

/** Improvements that keep the activity's settings, so a picked idea cannot contradict the plan. */
const materialSuggestions = [
  'שפה פשוטה יותר',
  'דוגמאות מוחשיות יותר',
  'פתיחה מעניינת יותר',
  'טקסט אחר באותו נושא',
];
const questionSuggestions = [
  'ניסוח פשוט וברור יותר',
  'שאלה קלה יותר',
  'שאלה מאתגרת יותר',
  'שאלה אחרת באותו נושא',
];
/** Presentation only: edits the owner's native fields and emits explicit scoped actions. */
@Component({
  selector: 'app-activity-document-editor',
  imports: [
    FieldValidity,
    FieldErrors,
    DisabledInteractive,
    FormField,
    FieldDirection,
    NgTemplateOutlet,
    ScopedRepair,
  ],
  templateUrl: './activity-document-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(input)': 'onEdit($event)', '(change)': 'onEdit($event)' },
})
export class ActivityDocumentEditor {
  protected readonly materialSuggestions = materialSuggestions;
  protected readonly questionSuggestions = questionSuggestions;
  readonly fields = input.required<FieldTree<DocumentForm>>();
  readonly materials = input.required<Pick<PlanMaterial, 'id' | 'source' | 'label'>[]>();
  readonly locked = input(false);
  readonly aiAvailable = input(false);
  readonly operationActive = input(false);
  /** No content yet: keep the document heading and fields visually secondary until generation. */
  readonly empty = input(false);
  /**
   * Saved content whose server diagnostics ask for review under changed requirements. Adoption is
   * offered only here: for content that is already current it changes nothing release checks read.
   */
  readonly staleMaterials = input<ReadonlySet<string>>(new Set());
  readonly staleQuestions = input<ReadonlySet<string>>(new Set());
  /** Release problems the saved check found, each shown at its field. */
  readonly contentIssues = input<ContentIssues>({ materials: new Map(), questions: new Map() });
  /** The owner's operation status, rendered in the card of the material or question it changes. */
  readonly status = input<TemplateRef<{ inCard: boolean }> | null>(null);
  readonly statusTarget = input<string | null>(null);
  readonly edited = output<{ key: string }>();
  readonly replaced = output<{
    kind: 'ReplaceMaterial' | 'ReplaceQuestion';
    targetId: string;
    instruction: string;
  }>();
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
