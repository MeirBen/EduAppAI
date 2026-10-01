import { measurementText } from '../../activities/activity-document-view/measurements';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';
import { ActivityDocumentView } from '../../activities/activity-document-view/activity-document-view';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';

/** Immutable parent-only preview. Copying creates a separate editable draft without review or an AI call. */
@Component({
  selector: 'app-snapshot-preview',
  imports: [DatePipe, RouterLink, ActivityDocumentView, LoadingIndicator],
  templateUrl: './snapshot-preview.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SnapshotPreviewPage {
  readonly instanceId = input.required<string>();
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  protected readonly snapshot = this.api.snapshot(this.instanceId);
  protected readonly copying = signal(false);
  protected readonly copiedId = signal('');
  protected readonly error = signal('');
  protected readonly apiError = apiError;
  protected readonly measurementText = measurementText;
  protected async copy() {
    if (this.copying() || this.copiedId()) return;
    this.copying.set(true);
    this.error.set('');
    try {
      const draft = await this.api.copySnapshot(this.instanceId(), this.lifetime);
      if (!this.lifetime.destroyed) this.copiedId.set(draft.id);
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.error.set(apiError(error) + ' ייתכן שהעותק נשמר. בדקו בספרייה לפני ניסיון נוסף.');
    } finally {
      if (!this.lifetime.destroyed) this.copying.set(false);
    }
  }
}
