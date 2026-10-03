import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { form, FormField, maxLength } from '@angular/forms/signals';
import { Limits } from '../../../core/api/limits';

/**
 * Contextual AI improvement for one material or question. It keeps only its disclosure state and
 * the unsent instruction; the workspace owns the scoped operation, which never touches siblings.
 */
@Component({
  selector: 'app-scoped-repair',
  imports: [FormField],
  templateUrl: './scoped-repair.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The instruction is not activity content, so its edits must not reach the editor's change tracking.
  host: { '(input)': '$event.stopPropagation()', '(change)': '$event.stopPropagation()' },
})
export class ScopedRepair {
  /** Unique element-ID prefix. */
  readonly key = input.required<string>();
  /** Distinguishes repeated triggers for assistive technology, for example "שאלה 2". */
  readonly name = input.required<string>();
  readonly prompt = input('מה תרצו לשנות?');
  readonly example = input('');
  readonly submitLabel = input('שיפור');
  readonly disabled = input(false);
  /** Sent as the operation's optional instruction; blank means a plain replacement. */
  readonly requested = output<string>();
  protected readonly open = signal(false);
  private readonly draft = signal({ instruction: '' });
  private readonly limits = inject(Limits).current;
  protected readonly fields = form(this.draft, (path) =>
    maxLength(path.instruction, this.limits.messageLength),
  );
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  protected submit() {
    if (this.disabled()) return;
    this.requested.emit(this.draft().instruction.trim());
    this.close();
  }
  protected close() {
    this.open.set(false);
    this.draft.set({ instruction: '' });
    // Focus was inside the form that is now hidden; return it to the disclosure's trigger.
    this.trigger().nativeElement.focus();
  }
}
