import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  linkedSignal,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { whenIdle } from '../../core/when-idle';
import { AssignmentApi } from '../../core/api/assignment-api';
import { AssignmentStatus, AssignmentSummary } from '../../core/api/assignment-models';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { focusHolder } from '../../shared/focus-holder';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { ChildSelector } from '../children/child-selector';
import { assignmentStatuses } from './assignment-presentation';
import { parentTaskError } from '../../core/api/parent-task-error';
import { Pager } from '../../shared/pager/pager';
import { writeError } from '../../core/api/api-error';
import { refreshOnReturn } from '../../core/page-visibility';

const changed = {
  withdraw: 'ההקצאה בוטלה. היא נשמרת בבחירה "ההקצאה בוטלה".',
  restore: 'ההקצאה הוחזרה, והעבודה השמורה זמינה שוב.',
};

/** Paged parent assignment history. Reading/filtering never starts or changes child work. */
@Component({
  selector: 'app-assignment-list',
  imports: [DatePipe, RouterLink, ChildSelector, DisabledInteractive, LoadingIndicator, Pager],
  templateUrl: './assignment-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AssignmentList {
  private readonly api = inject(AssignmentApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly childId = signal('');
  protected readonly status = signal<AssignmentStatus | ''>('');
  protected readonly page = linkedSignal(() => {
    this.childId();
    this.status();
    return 1;
  });
  protected readonly assignments = this.api.list(() => ({
    page: this.page(),
    childId: this.childId(),
    status: this.status(),
  }));
  protected readonly statuses = assignmentStatuses;
  protected readonly statusOptions = Object.entries(assignmentStatuses);
  protected readonly busy = signal(false);
  private pendingFocus?: () => void;
  // A reload retains old rows until its response; restore only after that response removes the button.
  private readonly restoreAfterLoad = whenIdle(
    () => this.busy() || this.assignments.isLoading(),
    () => {
      this.pendingFocus?.();
      this.pendingFocus = undefined;
    },
  );
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly failure = (error: unknown) =>
    parentTaskError(
      error,
      'מצב ההקצאה השתנה, או שהפרופיל או הפעילות כבר לא זמינים. רעננו את הרשימה.',
    );
  constructor() {
    refreshOnReturn(() => this.assignments.reload());
  }
  protected filterStatus(event: Event) {
    this.status.set((event.target as HTMLSelectElement).value as AssignmentStatus | '');
  }
  protected withdraw(assignment: AssignmentSummary) {
    if (
      this.idle() &&
      window.confirm(
        `לבטל את ההקצאה "${assignment.title}" עבור ${assignment.childName}? הגישה לעבודה תיחסם, והעבודה השמורה תישאר. אפשר להחזיר את ההקצאה בהמשך.`,
      )
    )
      return this.change(assignment, 'withdraw');
    return undefined;
  }
  private idle() {
    return !this.busy() && !this.assignments.isLoading();
  }
  /** Withdrawal is reversible: restoring gives back access and the saved work. */
  protected restore(assignment: AssignmentSummary) {
    return this.change(assignment, 'restore');
  }
  private async change(assignment: AssignmentSummary, action: 'withdraw' | 'restore') {
    if (!this.idle()) return;
    const restoreFocus = this.holdFocus();
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await this.api.change(assignment, action, this.lifetime);
      if (this.lifetime.destroyed) return;
      this.assignments.reload();
      this.notice.set(changed[action]);
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.error.set(
          writeError(
            this.failure(error),
            error,
            'ייתכן שהפעולה נשמרה. בדקו ברשימה לפני ניסיון נוסף.',
          ),
        );
    } finally {
      if (!this.lifetime.destroyed) {
        this.busy.set(false);
        this.pendingFocus = restoreFocus;
        this.restoreAfterLoad();
      }
    }
  }
}
