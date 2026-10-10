import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  Resource,
  signal,
  WritableResource,
  WritableSignal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { Page } from '../../../core/api/assignment-models';
import { libraryChanges } from '../../../core/api/library-changes';
import { apiError } from '../../../core/api/api-error';
import { whenIdle } from '../../../core/when-idle';
import { focusHolder } from '../../../shared/focus-holder';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { Pager } from '../../../shared/pager/pager';

/** A removed ready activity may live on in the archive, so only drafts say "deleted". */
const removedNotices = {
  all: 'נתוני הלמידה נמחקו.',
  draft: 'הטיוטה נמחקה.',
  snapshot: 'הפעילות הוסרה.',
};

/** A list's size is known only while its first page holds all of it. */
function wholeCount(list: Resource<Page<unknown> | undefined>) {
  return computed(() => {
    const page = list.hasValue() ? list.value() : undefined;
    return page?.page === 1 && !page.hasMore ? page.items.length : 0;
  });
}

/**
 * Content-first library; drafts and immutable snapshots have distinct routes
 * and deletion scopes. Lists follow changes saved elsewhere, such as on another device.
 */
@Component({
  selector: 'app-activity-library',
  imports: [RouterLink, DatePipe, LoadingIndicator, DisabledInteractive, Pager],
  templateUrl: './activity-library.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityLibrary {
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly updates = libraryChanges();
  protected readonly draftPage = signal(1);
  protected readonly snapshotPage = signal(1);
  protected readonly drafts = this.api.activities(this.draftPage);
  protected readonly snapshots = this.api.snapshots(this.snapshotPage);
  protected readonly draftCount = wholeCount(this.drafts);
  protected readonly snapshotCount = wholeCount(this.snapshots);
  /** The failed deletion, shown beside the item (or the reset) it was for. */
  private readonly failure = signal<{ id: string; message: string } | undefined>(undefined);
  protected readonly notice = signal('');
  protected readonly deleting = signal(false);
  protected readonly apiError = apiError;
  constructor() {
    const lists = [this.drafts, this.snapshots];
    // A running read may predate the change, so the reload waits for it to settle.
    const refresh = whenIdle(
      () => lists.some((list) => list.isLoading()),
      () => {
        for (const list of lists) list.reload();
      },
    );
    this.updates.changes.subscribe(refresh);
  }
  protected failed(id: string) {
    const failure = this.failure();
    return failure?.id === id ? failure.message : '';
  }
  protected async remove(kind: 'draft' | 'snapshot' | 'all', id = '', name = '') {
    if (this.deleting()) return;
    const prompt =
      kind === 'all'
        ? 'למחוק את כל נתוני הלמידה של המשפחה, כולל פרופילי הילדים, הגישה מהמכשירים, ההקצאות, התשובות, הציונים, הטיוטות והפעילויות? אי אפשר לבטל את הפעולה. חשבונות ההורים והגדרות ה־AI יישארו.'
        : kind === 'snapshot'
          ? `להסיר את "${name}"? אם הפעילות הוקצתה, היא תעבור לארכיון והעבודה תישמר. אחרת היא תימחק לצמיתות.`
          : `למחוק את "${name}"? אי אפשר לבטל את המחיקה.`;
    if (!window.confirm(prompt)) return;
    const restoreFocus = this.holdFocus();
    this.deleting.set(true);
    this.failure.set(undefined);
    try {
      if (kind === 'draft') await this.api.deleteActivity(id, this.lifetime);
      else if (kind === 'snapshot') await this.api.deleteSnapshot(id, this.lifetime);
      else await this.api.resetLibrary(this.lifetime);
      if (this.lifetime.destroyed) return;
      // Apply a confirmed deletion to the shown page now; a later server hint reconciles the lists.
      const removeFrom = <T extends { id: string }>(
        list: WritableResource<Page<T> | undefined>,
        page: WritableSignal<number>,
      ) => {
        // A reset empties every page; reading the first again also clears a failed read.
        if (kind === 'all') {
          if (page() > 1) page.set(1);
          else list.reload();
          return;
        }
        if (!list.hasValue()) return;
        const current = list.value();
        const items = current.items.filter((item) => item.id !== id);
        // An emptied later page steps back, which reads that page.
        if (!items.length && page() > 1) page.update((value) => value - 1);
        else list.set({ ...current, items });
      };
      if (kind === 'draft' || kind === 'all') removeFrom(this.drafts, this.draftPage);
      if (kind === 'snapshot' || kind === 'all') removeFrom(this.snapshots, this.snapshotPage);
      this.notice.set(removedNotices[kind]);
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.failure.set({ id: kind === 'all' ? 'all' : id, message: apiError(error) });
    } finally {
      if (!this.lifetime.destroyed) {
        this.deleting.set(false);
        restoreFocus();
      }
    }
  }
}
