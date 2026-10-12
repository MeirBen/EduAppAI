import { HttpResourceRef } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  linkedSignal,
  model,
} from '@angular/core';
import { apiError } from '../../core/api/api-error';
import { Page } from '../../core/api/assignment-models';
import { LoadingIndicator } from '../loading-indicator/loading-indicator';
import { Pager } from '../pager/pager';
import { ReadFailure } from '../read-failure/read-failure';

/**
 * One paged list around its projected rows: the first read's indicator, a failed read's retry (or
 * the page's own `[failure]` content), the rows with their empty state, and the pager. Another page
 * or view keeps the shown rows, busy, until its own arrive, so the page never shrinks or jumps;
 * meanwhile an empty list says it is loading rather than empty. Rows of another `owner` (such as
 * another child's devices) never show.
 */
@Component({
  selector: 'app-paged-list',
  imports: [LoadingIndicator, Pager, ReadFailure],
  template: `
    <app-loading-indicator [active]="resource().isLoading() && !shown()" [label]="loadingLabel()" />
    @if (resource().error(); as error) {
      <ng-content select="[failure]"
        ><app-read-failure [message]="failure()(error)" (retry)="resource().reload()"
      /></ng-content>
    } @else if (shown(); as shownPage) {
      <div class="list" [attr.aria-busy]="busy()">
        <ng-content />
        @if (!shownPage.items.length) {
          <p class="empty-state">
            {{ resource().status() === 'loading' ? loadingLabel() : empty() }}
          </p>
        }
      </div>
      <app-pager
        [label]="pagerLabel()"
        [(page)]="page"
        [hasMore]="shownPage.hasMore"
        [busy]="busy()"
      />
    }
  `,
  host: { class: 'contents' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PagedList<T> {
  readonly resource = input.required<HttpResourceRef<Page<T> | undefined>>();
  readonly page = model.required<number>();
  readonly owner = input<unknown>();
  /** What an empty list says, such as that a later page has nothing more. */
  readonly empty = input.required<string>();
  readonly loadingLabel = input.required<string>();
  /** Names the pages, such as "עמודי טיוטות". */
  readonly pagerLabel = input.required<string>();
  readonly failure = input<(error: unknown) => string>(apiError);
  /** The page is saving a change to the list. */
  readonly saving = input(false);
  protected readonly shown = linkedSignal<{ owner: unknown; value?: Page<T> }, Page<T> | undefined>(
    {
      source: () => {
        const resource = this.resource();
        return { owner: this.owner(), value: resource.hasValue() ? resource.value() : undefined };
      },
      computation: ({ owner, value }, previous) =>
        value ??
        (this.resource().isLoading() && previous?.source.owner === owner
          ? previous?.value
          : undefined),
    },
  );
  /** The rows the list shows. */
  readonly items = computed(() => this.shown()?.items ?? []);
  protected readonly busy = computed(() => this.resource().isLoading() || this.saving());
}
