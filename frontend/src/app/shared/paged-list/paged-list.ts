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
 * the owner's own `[failure]` content), the rows, busy while another page loads or the owner saves,
 * and the pager. Paging keeps the shown page until the next arrives; another `filter` (a view, a
 * child or an owner) shows the indicator instead, so rows never belong to another filter.
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
      <div class="list" [attr.aria-busy]="busy()"><ng-content /></div>
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
  readonly filter = input<unknown>();
  readonly loadingLabel = input.required<string>();
  /** Names the pages, such as "עמודי טיוטות". */
  readonly pagerLabel = input.required<string>();
  readonly failure = input<(error: unknown) => string>(apiError);
  /** The owner is saving a change to the list. */
  readonly saving = input(false);
  protected readonly shown = linkedSignal<
    { filter: unknown; value?: Page<T> },
    Page<T> | undefined
  >({
    source: () => {
      const resource = this.resource();
      return { filter: this.filter(), value: resource.hasValue() ? resource.value() : undefined };
    },
    computation: ({ filter, value }, previous) =>
      value ??
      (this.resource().isLoading() && previous?.source.filter === filter
        ? previous?.value
        : undefined),
  });
  /** The rows the list shows. */
  readonly items = computed(() => this.shown()?.items ?? []);
  protected readonly busy = computed(() => this.resource().isLoading() || this.saving());
}
