import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { DisabledInteractive } from '../disabled-interactive';

/** Previous/next paging for a bounded server list; a list that fits on one page shows none. */
@Component({
  selector: 'app-pager',
  imports: [DisabledInteractive],
  template: `
    @if (page() > 1 || hasMore()) {
      <nav class="flex items-center justify-center gap-2" [attr.aria-label]="label()">
        <button
          type="button"
          class="icon-button icon-button-quiet"
          [disabledInteractive]="busy() || page() === 1"
          (click)="page.set(page() - 1)"
        >
          <span class="icon icon-previous" aria-hidden="true"></span
          ><span class="tooltip">העמוד הקודם</span>
        </button>
        <span class="text-sm text-muted" aria-live="polite">עמוד {{ page() }}</span>
        <button
          type="button"
          class="icon-button icon-button-quiet"
          [disabledInteractive]="busy() || !hasMore()"
          (click)="page.set(page() + 1)"
        >
          <span class="icon icon-next" aria-hidden="true"></span
          ><span class="tooltip">העמוד הבא</span>
        </button>
      </nav>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // A list that fits on one page shows no pager, and its empty host takes no slot in the list's gaps.
  host: { class: 'contents' },
})
export class Pager {
  readonly page = model.required<number>();
  readonly hasMore = input.required<boolean>();
  /** Names the list, such as "עמודי פרופילים". */
  readonly label = input.required<string>();
  readonly busy = input(false);
}
