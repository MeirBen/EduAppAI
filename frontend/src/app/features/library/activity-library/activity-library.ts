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
import { liveLists } from '../../../core/api/live-lists';
import { apiError } from '../../../core/api/api-error';
import { focusHolder } from '../../../shared/focus-holder';
import { LiveStatus } from '../../../shared/live-status/live-status';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { Pager } from '../../../shared/pager/pager';

/**
 * What each removal asks and reports. A removed ready activity may live on in the archive, so only
 * drafts and archive deletions say "deleted"; deleting from the archive also erases children's work.
 */
const removals = {
  all: {
    confirm: () =>
      'למחוק את כל נתוני הלמידה של המשפחה, כולל פרופילי הילדים, הגישה מהמכשירים, ההקצאות, התשובות, הציונים, הטיוטות והפעילויות? אי אפשר לבטל את הפעולה. חשבונות ההורים והגדרות ה־AI יישארו.',
    done: 'נתוני הלמידה נמחקו.',
  },
  draft: {
    confirm: (name: string) => `למחוק את "${name}"? אי אפשר לבטל את המחיקה.`,
    done: 'הטיוטה נמחקה.',
  },
  snapshot: {
    confirm: (name: string) =>
      `להסיר את "${name}"? אם הפעילות הוקצתה, היא תעבור לארכיון והעבודה תישמר. אחרת היא תימחק לצמיתות.`,
    done: 'הפעילות הוסרה.',
  },
  archived: {
    confirm: (name: string) =>
      `למחוק לצמיתות את "${name}"? גם ההקצאות, התשובות והציונים של הילדים בפעילות הזו יימחקו. אי אפשר לבטל את המחיקה.`,
    done: 'הפעילות נמחקה לצמיתות, עם העבודה שהוקצתה בה.',
  },
};
type Removal = keyof typeof removals;

/** A list's size is known only while its first page holds all of it. */
function wholeCount(list: Resource<Page<unknown> | undefined>) {
  return computed(() => {
    const page = list.hasValue() ? list.value() : undefined;
    return page?.page === 1 && !page.hasMore ? page.items.length : 0;
  });
}

/**
 * Content-first library; drafts, immutable snapshots and the archive have distinct routes and
 * deletion scopes. Lists follow changes saved elsewhere, such as on another device; the archive is
 * read only while open.
 */
@Component({
  selector: 'app-activity-library',
  imports: [RouterLink, DatePipe, LiveStatus, LoadingIndicator, DisabledInteractive, Pager],
  templateUrl: './activity-library.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityLibrary {
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly draftPage = signal(1);
  protected readonly snapshotPage = signal(1);
  protected readonly archivePage = signal(1);
  protected readonly archiveOpen = signal(false);
  protected readonly drafts = this.api.activities(this.draftPage);
  protected readonly snapshots = this.api.snapshots(this.snapshotPage);
  protected readonly archive = this.api.archivedSnapshots(() =>
    this.archiveOpen() ? this.archivePage() : undefined,
  );
  protected readonly draftCount = wholeCount(this.drafts);
  protected readonly snapshotCount = wholeCount(this.snapshots);
  protected readonly archiveCount = wholeCount(this.archive);
  protected readonly deleting = signal(false);
  protected readonly live = liveLists([this.drafts, this.snapshots, this.archive], {
    busy: this.deleting,
  });
  /** The failed deletion, shown beside the item (or the reset) it was for. */
  private readonly failure = signal<{ id: string; message: string } | undefined>(undefined);
  protected readonly notice = signal('');
  protected readonly apiError = apiError;
  protected failed(id: string) {
    const failure = this.failure();
    return failure?.id === id ? failure.message : '';
  }
  protected async remove(kind: Removal, id = '', name = '') {
    if (this.deleting() || !window.confirm(removals[kind].confirm(name))) return;
    const restoreFocus = this.holdFocus();
    this.deleting.set(true);
    this.failure.set(undefined);
    try {
      if (kind === 'draft') await this.api.deleteActivity(id, this.lifetime);
      else if (kind === 'snapshot') await this.api.deleteSnapshot(id, this.lifetime);
      else if (kind === 'archived') await this.api.deleteArchivedSnapshot(id, this.lifetime);
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
      if (kind === 'archived' || kind === 'all') removeFrom(this.archive, this.archivePage);
      // A removed ready activity may have moved into an open archive; a closed one reads when opened.
      if (kind === 'snapshot') this.archive.reload();
      this.notice.set(removals[kind].done);
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
