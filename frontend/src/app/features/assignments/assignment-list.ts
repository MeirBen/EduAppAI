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

/** Paged parent assignment history. Reading/filtering never starts or changes child work. */
@Component({
  selector: 'app-assignment-list',
  imports: [DatePipe, RouterLink, ChildSelector, DisabledInteractive, LoadingIndicator],
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
      'מצב ההקצאה השתנה. רעננו את הרשימה כדי לבדוק אם כבר הוגשה עבודה או שההקצאה בוטלה.',
    );
  protected filterStatus(event: Event) {
    this.status.set((event.target as HTMLSelectElement).value as AssignmentStatus | '');
  }
  protected async withdraw(assignment: AssignmentSummary) {
    if (
      this.busy() ||
      this.assignments.isLoading() ||
      assignment.status !== 'assigned' ||
      !window.confirm(
        `לבטל את ההקצאה "${assignment.title}" עבור ${assignment.childName}? הגישה לעבודה תיחסם. ההיסטוריה תישמר ולא ניתן להקצות שוב את אותו עותק לאותו ילד.`,
      )
    )
      return;
    const restore = this.holdFocus();
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await this.api.withdraw(assignment, this.lifetime);
      if (this.lifetime.destroyed) return;
      this.assignments.reload();
      this.notice.set('ההקצאה בוטלה.');
    } catch (error) {
      if (!this.lifetime.destroyed)
        this.error.set(this.failure(error) + ' ייתכן שהפעולה נשמרה. בדקו ברשימה לפני ניסיון נוסף.');
    } finally {
      if (!this.lifetime.destroyed) {
        this.busy.set(false);
        this.pendingFocus = restore;
        this.restoreAfterLoad();
      }
    }
  }
}
