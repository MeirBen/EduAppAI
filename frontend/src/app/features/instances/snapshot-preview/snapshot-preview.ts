import { measurementItems } from '../../activities/activity-document-view/measurements';
import { MeasurementList } from '../../activities/measurement-list/measurement-list';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
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
import { focusHolder } from '../../../shared/focus-holder';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { AssignmentApi } from '../../../core/api/assignment-api';
import { AssignmentSummary } from '../../../core/api/assignment-models';
import { ChildSelector } from '../../children/child-selector';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { parentTaskError } from '../../../core/api/parent-task-error';

/** Immutable parent-only preview. Copying creates a separate editable draft without review or an AI call. */
@Component({
  selector: 'app-snapshot-preview',
  imports: [
    DatePipe,
    RouterLink,
    ActivityDocumentView,
    LoadingIndicator,
    MeasurementList,
    ChildSelector,
    DisabledInteractive,
  ],
  templateUrl: './snapshot-preview.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SnapshotPreviewPage {
  readonly instanceId = input.required<string>();
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly snapshot = this.api.snapshot(this.instanceId);
  protected readonly copying = signal(false);
  protected readonly copiedId = signal('');
  protected readonly error = signal('');
  protected readonly apiError = apiError;
  private readonly assignmentApi = inject(AssignmentApi);
  protected readonly childId = signal('');
  protected readonly assigning = signal(false);
  protected readonly assigned = signal<
    { assignment: AssignmentSummary; replay: boolean } | undefined
  >(undefined);
  protected readonly assignmentError = signal('');
  protected readonly measurements = computed(() => {
    const snapshot = this.snapshot.hasValue() ? this.snapshot.value() : undefined;
    return snapshot ? measurementItems(snapshot.measurements, snapshot.plan) : [];
  });
  protected async assign() {
    if (
      this.assigning() ||
      !this.childId() ||
      !this.snapshot.hasValue() ||
      this.snapshot.value().archivedAtUtc
    )
      return;
    this.assigning.set(true);
    this.assigned.set(undefined);
    this.assignmentError.set('');
    try {
      const response = await this.assignmentApi.create(
        this.childId(),
        this.instanceId(),
        this.lifetime,
      );
      if (!this.lifetime.destroyed && response.body)
        this.assigned.set({ assignment: response.body, replay: response.status === 200 });
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.assignmentError.set(
          parentTaskError(
            error,
            'לא ניתן ליצור הקצאה חדשה. ייתכן שהפרופיל הושבת או שהפעילות הועברה לארכיון.',
          ) + ' ייתכן שההקצאה נשמרה. בדקו בפעילויות לילדים לפני ניסיון נוסף.',
        );
    } finally {
      if (!this.lifetime.destroyed) this.assigning.set(false);
    }
  }
  protected async copy() {
    if (this.copying() || this.copiedId()) return;
    const restoreFocus = this.holdFocus();
    this.copying.set(true);
    this.error.set('');
    try {
      const draft = await this.api.copySnapshot(this.instanceId(), this.lifetime);
      if (!this.lifetime.destroyed) this.copiedId.set(draft.id);
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.error.set(apiError(error) + ' ייתכן שהעותק נשמר. בדקו בספרייה לפני ניסיון נוסף.');
    } finally {
      if (!this.lifetime.destroyed) {
        this.copying.set(false);
        restoreFocus();
      }
    }
  }
}
