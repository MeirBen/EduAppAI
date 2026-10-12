import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { applyEach, disabled, form, FormField, submit, validate } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { AssignmentApi } from '../../core/api/assignment-api';
import { liveLists } from '../../core/api/live-lists';
import { ParentAssignmentResult } from '../../core/api/assignment-models';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { focusHolder } from '../../shared/focus-holder';
import { FieldErrors } from '../../shared/forms/field-errors';
import { FieldValidity } from '../../shared/forms/field-validity';
import { isIntegerInput } from '../../shared/forms/integer-input';
import { LiveStatus } from '../../shared/live-status/live-status';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { ActivityDocumentView } from '../activities/activity-document-view/activity-document-view';
import { assignmentState } from './assignment-presentation';
import { parentTaskError } from '../../core/api/parent-task-error';
import { writeError } from '../../core/api/api-error';

/**
 * Frozen parent report with a local grade buffer that follows the family's changes, so a submission
 * shows up by itself. Reconciliation reads never replace entered grades.
 */
@Component({
  selector: 'app-assignment-result',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    FormField,
    FieldErrors,
    FieldValidity,
    DisabledInteractive,
    LiveStatus,
    LoadingIndicator,
    ActivityDocumentView,
  ],
  templateUrl: './assignment-result.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class AssignmentResult {
  readonly assignmentId = input.required<string>();
  private readonly api = inject(AssignmentApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly detail = this.api.detail(this.assignmentId);
  /** Changes only when the work becomes submitted, so rereading the detail never refetches the report. */
  private readonly submittedId = computed(() => {
    const status = this.detail.hasValue() ? this.detail.value().assignment.status : undefined;
    return status === 'completed' || status === 'awaiting-review' ? this.assignmentId() : undefined;
  });
  protected readonly initialResult = this.api.result(this.submittedId);
  protected readonly result = signal<ParentAssignmentResult | undefined>(undefined);
  /** A submission or review elsewhere rereads the detail; an accepted report and its grades stay. */
  protected readonly live = liveLists([this.detail]);
  protected readonly summary = computed(
    () =>
      this.result()?.assignment ??
      (this.detail.hasValue() ? this.detail.value().assignment : undefined),
  );
  protected readonly savedResult = signal<ParentAssignmentResult | undefined>(undefined);
  protected readonly timing = computed(() => {
    const source = this.result() ?? (this.detail.hasValue() ? this.detail.value() : undefined);
    if (!source) return undefined;
    const start = Date.parse(source.startedAtUtc ?? ''),
      end = Date.parse(source.submittedAtUtc ?? ''),
      saved = Date.parse(source.savedAtUtc ?? '');
    let elapsed = 'משך הזמן לא זמין';
    if (Number.isFinite(start) && Number.isFinite(end) && end >= start) {
      const minutes = Math.floor((end - start) / 60_000),
        hours = Math.floor(minutes / 60),
        remainder = minutes % 60;
      if (minutes === 0) elapsed = 'פחות מדקה';
      else if (hours === 0) elapsed = minutes === 1 ? 'דקה' : `${minutes} דקות`;
      else {
        elapsed = hours === 1 ? 'שעה' : `${hours} שעות`;
        if (remainder > 0) elapsed += remainder === 1 ? ' ודקה' : ` ו־${remainder} דקות`;
      }
    }
    return {
      startedAt: Number.isFinite(start) ? start : null,
      submittedAt: Number.isFinite(end) ? end : null,
      savedAt: Number.isFinite(saved) ? saved : null,
      submitted:
        source.assignment.status === 'awaiting-review' || source.assignment.status === 'completed',
      elapsed,
    };
  });
  protected readonly grades = signal<
    { questionId: string; possiblePoints: number; points: string }[]
  >([]);
  protected readonly busy = signal(false);
  protected readonly reading = signal(false);
  protected readonly recovery = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly assignmentState = assignmentState;
  protected readonly failure = (error: unknown) =>
    parentTaskError(
      error,
      'ההגשה השתנתה או שכבר נבדקה. קראו את התוצאה השמורה לפני המשך הבדיקה. הציונים שהקלדתם נשמרו כאן.',
    );
  protected readonly fields = form(this.grades, (path) => {
    disabled(path, { when: () => this.busy() || this.reading() || this.recovery() });
    applyEach(path, (row) =>
      validate(row.points, ({ value, valueOf }) => {
        const max = valueOf(row.possiblePoints);
        return isIntegerInput(value()) && Number(value()) >= 0 && Number(value()) <= max
          ? undefined
          : { kind: 'points', message: `יש להזין מספר שלם בין 0 ל־${max}.` };
      }),
    );
  });
  protected readonly rows = computed(() => {
    const result = this.result();
    if (!result) return [];
    const answers = new Map(result.answers.map((answer) => [answer.questionId, answer.value]));
    const awards = new Map(result.evaluation.questions.map((award) => [award.questionId, award]));
    const indices = new Map(this.grades().map((grade, index) => [grade.questionId, index]));
    return result.document.questions.map((question) => ({
      question,
      answer: answers.get(question.id),
      award: awards.get(question.id)!,
      gradeIndex: indices.get(question.id),
    }));
  });
  private readonly dirty = computed(() => this.grades().some((grade) => grade.points !== ''));

  constructor() {
    effect(() => {
      const incoming = this.initialResult.hasValue() ? this.initialResult.value() : undefined;
      if (incoming && !untracked(this.result)) untracked(() => this.accept(incoming));
    });
  }
  /** Native navigation/close protection for local-only grade keystrokes. */
  canLeave() {
    return !this.dirty() || window.confirm('יש ציונים שלא נשמרו. לצאת מהעמוד בלי לשמור?');
  }
  protected beforeUnload(event: BeforeUnloadEvent) {
    if (this.dirty()) event.preventDefault();
  }
  private accept(result: ParentAssignmentResult) {
    this.result.set(result);
    this.fields().reset(
      result.evaluation.questions
        .filter((row) => row.awardedPoints === null)
        .map((row) => ({
          questionId: row.questionId,
          possiblePoints: row.possiblePoints,
          points: '',
        })),
    );
    this.savedResult.set(undefined);
    this.recovery.set(false);
    this.error.set('');
  }
  protected async finalize(event: Event) {
    event.preventDefault();
    const result = this.result();
    if (!result?.evaluation.pendingCount || this.busy() || this.reading() || this.recovery())
      return;
    this.error.set('');
    await submit(this.fields, async () => {
      if (!window.confirm('לשמור את כל הציונים ולסיים את הבדיקה? אחרי השמירה אי אפשר לשנות אותם.'))
        return;
      const restore = this.holdFocus();
      this.busy.set(true);
      this.notice.set('');
      try {
        const saved = await this.api.review(
          this.assignmentId(),
          result.revision,
          this.grades().map(({ questionId, points }) => ({ questionId, points: Number(points) })),
          this.lifetime,
        );
        if (this.lifetime.destroyed) return;
        this.accept(saved);
        this.notice.set('הבדיקה הושלמה והציונים נשמרו.');
      } catch (error) {
        if (this.lifetime.destroyed) return;
        this.error.set(
          writeError(
            this.failure(error),
            error,
            'ייתכן שהציונים נשמרו. קראו את התוצאה השמורה כדי לבדוק.',
          ),
        );
        this.recovery.set(true);
      } finally {
        if (!this.lifetime.destroyed) {
          this.busy.set(false);
          restore();
        }
      }
    });
    if (this.fields().invalid()) this.error.set('תקנו את הניקוד המסומן.');
  }
  protected async readSaved() {
    if (this.busy() || this.reading()) return;
    this.reading.set(true);
    this.savedResult.set(undefined);
    try {
      const saved = await this.api.readResult(this.assignmentId(), this.lifetime);
      if (this.lifetime.destroyed) return;
      if (saved.evaluation.pendingCount && saved.revision === this.result()?.revision) {
        this.recovery.set(false);
        this.error.set('');
        this.notice.set(
          'עדיין לא נשמר ציון סופי. הציונים שהקלדתם נשמרו כאן, ואפשר לסיים את הבדיקה.',
        );
      } else {
        this.savedResult.set(saved);
        this.recovery.set(true);
      }
    } catch (error) {
      if (!this.lifetime.destroyed) this.error.set(this.failure(error));
    } finally {
      if (!this.lifetime.destroyed) this.reading.set(false);
    }
  }
  protected useSaved() {
    const saved = this.savedResult();
    if (
      !saved ||
      this.busy() ||
      this.reading() ||
      (this.dirty() && !window.confirm('להחליף את הציונים שהקלדתם בתוצאה השמורה?'))
    )
      return;
    const restore = this.holdFocus();
    this.accept(saved);
    this.notice.set('התוצאה השמורה מוצגת.');
    restore();
  }
}
