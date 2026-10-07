import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { DisabledInteractive } from '../disabled-interactive';

/** Previous/next paging for a bounded server list; a list that fits on one page shows none. */
@Component({
  selector: 'app-pager',
  imports: [DisabledInteractive],
  template: `
    @if (page() > 1 || hasMore()) {
      <nav class="flex flex-wrap items-center gap-3" [attr.aria-label]="label()">
        <button
          type="button"
          class="button button-secondary"
          [disabledInteractive]="busy() || page() === 1"
          (click)="page.set(page() - 1)"
        >
          הקודם
        </button>
        <span>עמוד {{ page() }}</span>
        <button
          type="button"
          class="button button-secondary"
          [disabledInteractive]="busy() || !hasMore()"
          (click)="page.set(page() + 1)"
        >
          הבא
        </button>
      </nav>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Pager {
  readonly page = model.required<number>();
  readonly hasMore = input.required<boolean>();
  /** Names the list, such as "עמודי פרופילים". */
  readonly label = input.required<string>();
  readonly busy = input(false);
}
