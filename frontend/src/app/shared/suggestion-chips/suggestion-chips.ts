import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';

/**
 * Ready-made wording a parent can pick, in equal cells that stack on phones; a suggestion only fills
 * its field and never sends. A label longer than three lines ends in an ellipsis, and only then its
 * tooltip shows it.
 */
@Component({
  selector: 'app-suggestion-chips',
  templateUrl: './suggestion-chips.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'grid gap-2' },
})
export class SuggestionChips {
  /** Unique element-ID prefix. */
  readonly key = input.required<string>();
  readonly label = input.required<string>();
  readonly suggestions = input.required<readonly string[]>();
  readonly disabled = input(false);
  readonly picked = output<string>();
  /** The suggestion under the pointer or focus whose label is cut short. */
  protected readonly clamped = signal('');

  protected measure(suggestion: string, label: HTMLElement) {
    this.clamped.set(label.scrollHeight > label.clientHeight ? suggestion : '');
  }
}
