import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
  WritableResource,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { Limits } from '../../../core/api/limits';
import { apiError } from '../../../core/api/api-error';
import { whenIdle } from '../../../core/when-idle';
import { focusHolder } from '../../../shared/focus-holder';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';

/**
 * Content-first library; drafts, independent templates and immutable snapshots have distinct routes
 * and deletion scopes. Lists follow changes saved elsewhere, such as on another device.
 */
@Component({
  selector: 'app-activity-library',
  imports: [RouterLink, DatePipe, LoadingIndicator],
  templateUrl: './activity-library.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityLibrary {
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly limits = inject(Limits).current;
  protected readonly templates = this.api.templates();
  protected readonly drafts = this.api.activities();
  protected readonly snapshots = this.api.snapshots();
  private readonly lists: WritableResource<unknown>[] = [
    this.templates,
    this.drafts,
    this.snapshots,
  ];
  // A reload keeps the shown lists in place; only a list without a value shows the loader.
  protected readonly loading = computed(() =>
    this.lists.some((list) => list.isLoading() && !list.hasValue()),
  );
  protected readonly loadError = computed(
    () => this.templates.error() ?? this.drafts.error() ?? this.snapshots.error(),
  );
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly deleting = signal(false);
  protected readonly apiError = apiError;
  constructor() {
    // A running read may predate the change, so the reload waits for it to settle.
    const refresh = whenIdle(
      () => this.lists.some((list) => list.isLoading()),
      () => this.reload(),
    );
    this.api.libraryChanges().pipe(takeUntilDestroyed()).subscribe(refresh);
  }
  protected reload() {
    for (const list of this.lists) list.reload();
  }
  protected async remove(kind: 'draft' | 'template' | 'snapshot' | 'all', id = '', name = '') {
    if (this.deleting()) return;
    const prompt =
      kind === 'all'
        ? 'למחוק את כל נתוני הלמידה של המשפחה? אי אפשר לבטל את הפעולה. החשבון יישאר.'
        : `למחוק את "${name}"? שאר הפריטים יישארו.`;
    if (!window.confirm(prompt)) return;
    const restoreFocus = this.holdFocus();
    this.deleting.set(true);
    this.error.set('');
    try {
      if (kind === 'draft') await this.api.deleteActivity(id, this.lifetime);
      else if (kind === 'snapshot') await this.api.deleteSnapshot(id, this.lifetime);
      else if (kind === 'template') await this.api.deleteTemplate(id, this.lifetime);
      else await this.api.resetLibrary(this.lifetime);
      if (this.lifetime.destroyed) return;
      // Deletions are independent, so the confirmed one leaves its list without refetching all three.
      const kept = <T extends { id: string }>(items: T[] | undefined) =>
        kind === 'all' ? [] : (items ?? []).filter((item) => item.id !== id);
      if (kind === 'draft' || kind === 'all') this.drafts.update(kept);
      if (kind === 'snapshot' || kind === 'all') this.snapshots.update(kept);
      if (kind === 'template' || kind === 'all') this.templates.update(kept);
      this.notice.set(kind === 'all' ? 'נתוני הלמידה נמחקו.' : 'הפריט נמחק.');
    } catch (error) {
      if (!this.lifetime.destroyed) this.error.set(apiError(error));
    } finally {
      if (!this.lifetime.destroyed) {
        this.deleting.set(false);
        restoreFocus();
      }
    }
  }
}
