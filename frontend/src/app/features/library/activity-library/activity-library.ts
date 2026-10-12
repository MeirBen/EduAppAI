import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  linkedSignal,
  Signal,
  signal,
  WritableResource,
  WritableSignal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { Page } from '../../../core/api/assignment-models';
import { LibraryCounts, SnapshotSummary, SnapshotView } from '../../../core/api/models';
import { liveLists } from '../../../core/api/live-lists';
import { apiError } from '../../../core/api/api-error';
import { focusHolder } from '../../../shared/focus-holder';
import { LiveStatus } from '../../../shared/live-status/live-status';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { ChildBadge } from '../../../shared/child-badge/child-badge';
import { Count } from '../../../shared/count/count';
import { Pager } from '../../../shared/pager/pager';
import { shownValue } from '../../../core/api/shown-value';

const snapshotViews: { value: SnapshotView; label: string; empty: string }[] = [
  { value: 'assigned', label: 'מוקצות', empty: 'אין כרגע פעילויות שמחכות לילדים או לבדיקה.' },
  {
    value: 'unassigned',
    label: 'לא הוקצו',
    empty: 'אין פעילויות שעוד לא הוקצו. פעילות שתאשרו תופיע כאן.',
  },
  { value: 'completed', label: 'הושלמו', empty: 'עוד אין פעילויות שכל העבודה בהן הושלמה.' },
];

/** A ready row's date: the event that put it in its view, as the server orders them. */
function rowDate({ assignedAtUtc, completedAtUtc, createdAtUtc }: SnapshotSummary) {
  if (assignedAtUtc) return { label: 'הוקצתה לאחרונה', at: assignedAtUtc };
  if (completedAtUtc) return { label: 'הושלמה', at: completedAtUtc };
  return { label: 'אושרה', at: createdAtUtc };
}

/**
 * What each removal asks and reports. A removed ready activity may live on in the archive, so only
 * drafts and archive deletions say "deleted"; deleting from the archive also erases children's work.
 */
const removals = {
  activities: {
    confirm: () =>
      'למחוק את כל הפעילויות: הטיוטות, הפעילויות המוכנות והארכיון, עם ההקצאות, התשובות והציונים שבהן? פרופילי הילדים והמכשירים יישארו. אי אפשר לבטל את המחיקה.',
    done: 'כל הפעילויות נמחקו.',
  },
  children: {
    confirm: () =>
      'למחוק את כל נתוני הילדים: הפרופילים, הגישה מהמכשירים, ההקצאות, התשובות והציונים? גם הארכיון יימחק, כי הוא שומר רק את העבודה הזו. הטיוטות והפעילויות המוכנות יישארו. אי אפשר לבטל את המחיקה.',
    done: 'נתוני הילדים נמחקו.',
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

/**
 * Ready activities open with the page unless its first count finds none; drafts and the archive
 * start folded. Only that first count decides; switching views, paging and live changes leave the
 * parent's own toggle alone.
 */
function sectionOpen(count: Signal<number | undefined>) {
  const first = linkedSignal<number | undefined, number | undefined>({
    source: count,
    computation: (value, previous) => previous?.value ?? value,
  });
  return computed(() => first() !== 0);
}

/**
 * Content-first library; drafts, immutable snapshots and the archive have distinct routes and
 * deletion scopes, each in a folding section that keeps its count. Ready activities open on those
 * with work left to assign or finish. Lists follow changes saved elsewhere, such as on another device.
 */
@Component({
  selector: 'app-activity-library',
  imports: [
    ChildBadge,
    Count,
    RouterLink,
    DatePipe,
    LiveStatus,
    LoadingIndicator,
    DisabledInteractive,
    Pager,
  ],
  templateUrl: './activity-library.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityLibrary {
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly draftPage = signal(1);
  protected readonly snapshotViews = snapshotViews;
  protected readonly snapshotView = signal<SnapshotView>('assigned');
  /** Choosing another view starts from its first page. */
  protected readonly snapshotPage = linkedSignal(() => {
    this.snapshotView();
    return 1;
  });
  protected readonly snapshotEmpty = computed(
    () => snapshotViews.find((view) => view.value === this.snapshotView())!.empty,
  );
  protected readonly archivePage = signal(1);
  protected readonly drafts = this.api.activities(this.draftPage);
  protected readonly snapshots = this.api.snapshots(() => ({
    page: this.snapshotPage(),
    status: this.snapshotView(),
  }));
  protected readonly archive = this.api.archivedSnapshots(this.archivePage);
  protected readonly shownDrafts = shownValue(this.drafts);
  protected readonly shownSnapshots = shownValue(this.snapshots);
  protected readonly shownArchive = shownValue(this.archive);
  /** Exact section and view sizes, so a folded section or an unchosen view says what it holds. */
  protected readonly counts = this.api.libraryCounts();
  protected readonly draftCount = computed(() => this.count('drafts'));
  protected readonly snapshotCount = computed(() => {
    const counts = this.counts.hasValue() ? this.counts.value() : undefined;
    return counts && snapshotViews.reduce((sum, view) => sum + counts[view.value], 0);
  });
  protected readonly archiveCount = computed(() => this.count('archived'));
  protected readonly snapshotsOpen = sectionOpen(this.snapshotCount);
  protected readonly deleting = signal(false);
  protected readonly live = liveLists([this.counts, this.drafts, this.snapshots, this.archive], {
    busy: this.deleting,
  });
  /** The failed deletion, shown beside the item (or the data deletions) it was for. */
  private readonly failure = signal<{ id: string; message: string } | undefined>(undefined);
  protected readonly notice = signal('');
  protected readonly apiError = apiError;
  protected readonly rowDate = rowDate;
  protected count(section: keyof LibraryCounts) {
    return this.counts.hasValue() ? this.counts.value()[section] : undefined;
  }
  protected failed(id: string) {
    const failure = this.failure();
    return failure?.id === id ? failure.message : '';
  }
  protected async remove(kind: Removal, id = '', name = '') {
    if (this.deleting() || !window.confirm(removals[kind].confirm(name))) return;
    const restoreFocus = this.holdFocus();
    const bulk = kind === 'activities' || kind === 'children';
    this.deleting.set(true);
    this.failure.set(undefined);
    try {
      if (kind === 'draft') await this.api.deleteActivity(id, this.lifetime);
      else if (kind === 'snapshot') await this.api.deleteSnapshot(id, this.lifetime);
      else if (kind === 'archived') await this.api.deleteArchivedSnapshot(id, this.lifetime);
      else await this.api.deleteData(kind, this.lifetime);
      if (this.lifetime.destroyed) return;
      // Apply a confirmed deletion to the shown page now; a later server hint reconciles the lists.
      const removeFrom = <T extends { id: string }>(
        list: WritableResource<Page<T> | undefined>,
        page: WritableSignal<number>,
      ) => {
        // A data deletion changes every page; reading the first again also clears a failed read.
        if (bulk) {
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
      if (kind === 'draft' || kind === 'activities') removeFrom(this.drafts, this.draftPage);
      if (kind === 'snapshot' || bulk) removeFrom(this.snapshots, this.snapshotPage);
      if (kind === 'archived' || bulk) removeFrom(this.archive, this.archivePage);
      // A removed ready activity may have moved into the archive.
      if (kind === 'snapshot') this.archive.reload();
      this.counts.reload();
      this.notice.set(removals[kind].done);
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.failure.set({ id: bulk ? 'data' : id, message: apiError(error) });
    } finally {
      if (!this.lifetime.destroyed) {
        this.deleting.set(false);
        restoreFocus();
      }
    }
  }
}
