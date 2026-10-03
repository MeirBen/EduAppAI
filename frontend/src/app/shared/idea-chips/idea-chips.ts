import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/** Ready-made wording a parent can pick; an idea only fills its field and never sends. */
@Component({
  selector: 'app-idea-chips',
  templateUrl: './idea-chips.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'grid gap-2' },
})
export class IdeaChips {
  /** Unique element-ID prefix. */
  readonly key = input.required<string>();
  readonly label = input.required<string>();
  readonly ideas = input.required<readonly string[]>();
  readonly disabled = input(false);
  readonly picked = output<string>();
}
