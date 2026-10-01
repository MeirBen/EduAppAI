import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

/**
 * Contextual AI improvement for one material or question. It keeps only its disclosure state and
 * the unsent instruction; the workspace owns the scoped operation, which never touches siblings.
 */
@Component({
  selector: 'app-scoped-repair',
  templateUrl: './scoped-repair.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
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
  protected readonly instruction = signal('');
  protected readonly tooLong = computed(() => this.instruction().length > 4000);
  protected type(event: Event) {
    // The document editor treats bubbling input as content edits; this text is not activity content.
    event.stopPropagation();
    this.instruction.set((event.target as HTMLTextAreaElement).value);
  }
  protected submit() {
    if (this.disabled() || this.tooLong()) return;
    this.requested.emit(this.instruction().trim());
    this.open.set(false);
    this.instruction.set('');
  }
}
