import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../core/api/limits';
import { FieldDirection } from '../../../shared/forms/field-direction';

/**
 * New text for a fixed or per-activity source, kept exactly as typed. Edits the owner's field and
 * emits explicit actions; the workspace applies the text and confirms the source.
 */
@Component({
  selector: 'app-source-replacement',
  imports: [FormField, FieldDirection, DecimalPipe],
  templateUrl: './source-replacement.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SourceReplacement {
  readonly field = input.required<FieldTree<string>>();
  readonly locked = input(false);
  /** Each keystroke, so the owner can invalidate replies about the earlier text. */
  readonly typed = output<void>();
  readonly accepted = output<void>();
  readonly cancelled = output<void>();
  protected readonly limits = inject(Limits).current;
}
