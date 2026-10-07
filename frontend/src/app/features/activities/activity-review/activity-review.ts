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
  /** Only the text exists so far; its questions are the next step. */
  readonly questionsNext = input(false);
  protected readonly summary = computed(() => {
    if (this.questionsNext())
      return this.issues().length
        ? 'כדי ליצור את השאלות, טפלו בדברים הבאים:'
        : 'קראו את הטקסט ותקנו אותו לפי הצורך. כשהוא מוכן, צרו את השאלות.';
    return this.issues().length
      ? 'כדי לסמן את הפעילות כמוכנה, טפלו בדברים הבאים:'
      : this.saved() && !this.outdated()
        ? 'הפעילות מוכנה לבדיקה שלכם.'
        : '';
  });
}
