import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';

/** Content-first library; drafts, independent templates and immutable snapshots have distinct routes and deletion scopes. */
@Component({
  selector: 'app-activity-library',
  imports: [RouterLink, DatePipe, LoadingIndicator],
  templateUrl: './activity-library.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityLibrary {
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  protected readonly templates = this.api.templates();
  protected readonly drafts = this.api.activities();
  protected readonly snapshots = this.api.snapshots();
  protected readonly loading = computed(
    () => this.templates.isLoading() || this.drafts.isLoading() || this.snapshots.isLoading(),
  );
  protected readonly loadError = computed(
    () => this.templates.error() ?? this.drafts.error() ?? this.snapshots.error(),
  );
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly deleting = signal(false);
  protected readonly apiError = apiError;
  protected reload() {
    this.templates.reload();
    this.drafts.reload();
    this.snapshots.reload();
  }
  protected async remove(kind: 'draft' | 'template' | 'snapshot' | 'all', id = '', name = '') {
    if (this.deleting()) return;
    const prompt =
      kind === 'all'
        ? 'למחוק את כל נתוני הלמידה של המשפחה? החשבון נשמר.'
        : `למחוק את הפריט ״${name}״? פריטים עצמאיים אחרים יישארו.`;
    if (!window.confirm(prompt)) return;
    this.deleting.set(true);
    this.error.set('');
    try {
      if (kind === 'draft') await this.api.deleteActivity(id, this.lifetime);
      else if (kind === 'snapshot') await this.api.deleteSnapshot(id, this.lifetime);
      else if (kind === 'template') await this.api.deleteTemplate(id, this.lifetime);
      else await this.api.resetLibrary(this.lifetime);
      if (this.lifetime.destroyed) return;
      this.notice.set(kind === 'all' ? 'נתוני הלמידה נמחקו.' : 'הפריט נמחק.');
      this.reload();
    } catch (error) {
      if (!this.lifetime.destroyed) this.error.set(apiError(error));
    } finally {
      if (!this.lifetime.destroyed) this.deleting.set(false);
    }
  }
}
