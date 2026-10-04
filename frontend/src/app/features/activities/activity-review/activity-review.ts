import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MeasurementItem } from '../activity-document-view/measurements';
import { MeasurementList } from '../measurement-list/measurement-list';

/**
 * Readiness of the saved revision in parent language: what still blocks Mark Ready, and the saved
 * length evidence. Technical checks never replace the parent's own educational review.
 */
@Component({
  selector: 'app-activity-review',
  imports: [MeasurementList],
  templateUrl: './activity-review.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'grid gap-4' },
})
export class ActivityReview {
  readonly issues = input.required<string[]>();
  readonly measurements = input.required<MeasurementItem[]>();
  /** The checks describe the latest saved revision. */
  readonly saved = input(false);
  /** Local edits or a running generation may still change the checks. */
  readonly outdated = input(false);
  protected readonly summary = computed(() =>
    this.issues().length
      ? 'לפני סימון כמוכנה יש לטפל בדברים הבאים:'
      : this.saved() && !this.outdated()
        ? 'הפעילות מוכנה לבדיקה שלכם.'
        : '',
  );
}
