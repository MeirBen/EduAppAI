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
import { apiError, writeError } from '../../../core/api/api-error';
import { ActivityDocumentView } from '../../activities/activity-document-view/activity-document-view';
import { focusHolder } from '../../../shared/focus-holder';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { AssignmentApi } from '../../../core/api/assignment-api';
import { AssignmentSummary } from '../../../core/api/assignment-models';
import { ChildSelector } from '../../children/child-selector';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { parentTaskError } from '../../../core/api/parent-task-error';

const outcomes = {
  created: 'הפעילות הוקצתה',
  existing: 'הפעילות כבר הוקצתה',
  restored: 'ההקצאה הוחזרה',
};
interface Assigned {
  assignment: AssignmentSummary;
  outcome: keyof typeof outcomes;
}

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
  protected readonly assigned = signal<Assigned | undefined>(undefined);
  protected readonly outcomes = outcomes;
  protected readonly assignmentError = signal('');
  protected readonly measurements = computed(() => {
    const snapshot = this.snapshot.hasValue() ? this.snapshot.value() : undefined;
    return snapshot ? measurementItems(snapshot.measurements, snapshot.plan) : [];
  });
  protected assign() {
    if (
      this.assigning() ||
      !this.childId() ||
      !this.snapshot.hasValue() ||
      this.snapshot.value().archivedAtUtc
    )
      return;
    this.assigned.set(undefined);
    return this.request(async () => {
      const response = await this.assignmentApi.create(
        this.childId(),
        this.instanceId(),
        this.lifetime,
      );
      return response.body
        ? { assignment: response.body, outcome: response.status === 200 ? 'existing' : 'created' }
        : undefined;
    });
  }
  /** Assigning a withdrawn pair finds it again; only this explicit action gives the work back. */
  protected restore(assignment: AssignmentSummary) {
    if (this.assigning()) return;
    return this.request(async () => ({
      assignment: await this.assignmentApi.change(assignment, 'restore', this.lifetime),
      outcome: 'restored',
    }));
  }
  private async request(send: () => Promise<Assigned | undefined>) {
    const restoreFocus = this.holdFocus();
    this.assigning.set(true);
    this.assignmentError.set('');
    try {
      const result = await send();
      if (!this.lifetime.destroyed && result) this.assigned.set(result);
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.assignmentError.set(
          writeError(
            parentTaskError(
              error,
              'אי אפשר להקצות את הפעילות. ייתכן שהפרופיל הושבת, שהפעילות הועברה לארכיון או שההקצאה השתנתה.',
            ),
            error,
            'ייתכן שההקצאה נשמרה. בדקו בפעילויות לילדים לפני ניסיון נוסף.',
          ),
        );
    } finally {
      if (!this.lifetime.destroyed) {
        this.assigning.set(false);
        restoreFocus();
      }
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
        this.error.set(
          writeError(
            apiError(error),
            error,
            'ייתכן שהטיוטה נוצרה. בדקו במרחב שלנו לפני ניסיון נוסף.',
          ),
        );
    } finally {
      if (!this.lifetime.destroyed) {
        this.copying.set(false);
        restoreFocus();
      }
    }
  }
}
