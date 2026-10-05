import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/** Ready-made wording a parent can pick; a suggestion only fills its field and never sends. */
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
}
