import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChildApi } from '../../core/api/child-api';
import { libraryChanges } from '../../core/api/library-changes';
import { ChildAuth } from '../../core/auth/child-auth';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { Pager } from '../../shared/pager/pager';
import { childError, childStatus } from './child-error';
import { refreshOnReturn } from '../../core/page-visibility';
import { whenIdle } from '../../core/when-idle';

type InboxView = 'available' | 'submitted';
const views: { value: InboxView; label: string }[] = [
  { value: 'available', label: 'לעשות' },
  { value: 'submitted', label: 'הגשתי' },
];

/** Bounded child inbox; listing work never starts a session. */
@Component({
  selector: 'app-child-inbox',
  imports: [DatePipe, RouterLink, DisabledInteractive, LoadingIndicator, Pager],
  templateUrl: './child-inbox.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildInbox {
  protected readonly auth = inject(ChildAuth);
  protected readonly updates = libraryChanges('/api/child/changes');
  protected readonly views = views;
  protected readonly state = signal<InboxView>('available');
  /** Choosing another view starts from its first page. */
  protected readonly page = linkedSignal(() => {
    this.state();
    return 1;
  });
  protected readonly assignments = inject(ChildApi).inbox(() => ({
    state: this.state(),
    page: this.page(),
  }));
  protected readonly failure = childError;
  protected readonly status = childStatus;
  constructor() {
    const refresh = whenIdle(
      () => this.assignments.isLoading(),
      () => this.assignments.reload(),
    );
    this.updates.changes.subscribe(refresh);
    refreshOnReturn(refresh);
  }
  protected refresh() {
    if (this.updates.state() === 'unavailable') this.updates.reconnect();
    else this.assignments.reload();
  }
}
