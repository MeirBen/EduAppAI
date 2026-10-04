import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  signal,
  WritableResource,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { libraryChanges } from '../../../core/api/library-changes';
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
  protected readonly updates = libraryChanges();
  protected readonly templates = this.api.templates();
  protected readonly drafts = this.api.activities();
  protected readonly snapshots = this.api.snapshots();
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly deleting = signal(false);
  protected readonly apiError = apiError;
  constructor() {
    const lists = [this.templates, this.drafts, this.snapshots];
    // A running read may predate the change, so the reload waits for it to settle.
    const refresh = whenIdle(
      () => lists.some((list) => list.isLoading()),
      () => {
        for (const list of lists) list.reload();
      },
    );
    this.updates.changes.subscribe(refresh);
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
      // Apply the confirmed deletion now; a later server hint reconciles the lists.
      const removeFrom = <T extends { id: string }>(list: WritableResource<T[] | undefined>) => {
        if (kind === 'all') list.set([]);
        else if (list.hasValue()) list.set(list.value().filter((item) => item.id !== id));
      };
      if (kind === 'draft' || kind === 'all') removeFrom(this.drafts);
      if (kind === 'snapshot' || kind === 'all') removeFrom(this.snapshots);
      if (kind === 'template' || kind === 'all') removeFrom(this.templates);
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
