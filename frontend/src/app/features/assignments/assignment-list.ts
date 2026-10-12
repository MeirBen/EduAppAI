import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  linkedSignal,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { whenIdle } from '../../core/when-idle';
import { AssignmentApi } from '../../core/api/assignment-api';
import { liveLists } from '../../core/api/live-lists';
import { AssignmentSummary, AssignmentView } from '../../core/api/assignment-models';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { focusHolder } from '../../shared/focus-holder';
import { LiveStatus } from '../../shared/live-status/live-status';
import { ChildSelector } from '../children/child-selector';
import { assignmentState, assignmentViews } from './assignment-presentation';
import { parentTaskError } from '../../core/api/parent-task-error';
import { Count } from '../../shared/count/count';
import { ChildBadge } from '../../shared/child-badge/child-badge';
import { writeError } from '../../core/api/api-error';
import { PagedList } from '../../shared/paged-list/paged-list';

const changed = {
  withdraw: 'ההקצאה בוטלה. היא נשמרת בתצוגה "בוטלו".',
  restore: 'ההקצאה הוחזרה, עם כל מה שנשמר בה.',
};

/**
 * Paged parent assignment history that follows the family's changes, open work first. Reading or
 * filtering never starts or changes child work; every write outcome rereads the list.
 */
@Component({
  selector: 'app-assignment-list',
  imports: [
    PagedList,
    Count,
    DatePipe,
    RouterLink,
    ChildSelector,
    DisabledInteractive,
    LiveStatus,
    ChildBadge,
  ],
  templateUrl: './assignment-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AssignmentList {
  private readonly api = inject(AssignmentApi);
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly childId = signal('');
  protected readonly view = signal<AssignmentView>('open');
  protected readonly page = linkedSignal(() => {
    this.childId();
    this.view();
    return 1;
  });
  protected readonly assignments = this.api.list(() => ({
    page: this.page(),
    childId: this.childId(),
    status: this.view(),
  }));
  protected readonly views = assignmentViews;
  protected readonly emptyView = computed(
    () => assignmentViews.find((option) => option.value === this.view())!.empty,
  );
  protected readonly assignmentState = assignmentState;
  protected readonly busy = signal(false);
  protected readonly counts = this.api.counts(this.childId);
  protected readonly live = liveLists([this.assignments, this.counts], { busy: this.busy });
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
      'מצב ההקצאה השתנה, או שהפרופיל או הפעילות כבר לא זמינים. הרשימה עודכנה.',
    );
  protected withdraw(assignment: AssignmentSummary) {
    if (
      this.idle() &&
      window.confirm(
        `לבטל את "${assignment.title}" ל־${assignment.childName}? העבודה תיחסם, אבל מה שנשמר בה יישאר, ואפשר להחזיר אותה בהמשך.`,
      )
    )
      return this.change(assignment, 'withdraw');
    return undefined;
  }
  protected count(view: AssignmentView) {
    return this.counts.hasValue() ? this.counts.value()[view] : undefined;
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
      if (!this.lifetime.destroyed) this.notice.set(changed[action]);
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
        // A success changed the row and a failure means the shown state may be stale; either way, reread.
        this.live.reload();
        this.busy.set(false);
        this.pendingFocus = restoreFocus;
        this.restoreAfterLoad();
      }
    }
  }
}
