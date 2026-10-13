import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChildApi } from '../../core/api/child-api';
import { liveLists } from '../../core/api/live-lists';
import { ChildAuth } from '../../core/auth/child-auth';
import { LiveStatus } from '../../shared/live-status/live-status';
import { ScoreBadge } from '../../shared/score-badge/score-badge';
import { DifficultyBadge } from '../../shared/difficulty-badge/difficulty-badge';
import { childError, childStatus } from './child-error';
import { learnerState } from './child-presentation';
import { PagedList } from '../../shared/paged-list/paged-list';

type InboxView = 'available' | 'submitted';
const views: { value: InboxView; label: string }[] = [
  { value: 'available', label: 'לעשות' },
  { value: 'submitted', label: 'הגשתי' },
];

/** Bounded child inbox that follows the parent's changes; listing work never starts a session. */
@Component({
  selector: 'app-child-inbox',
  imports: [PagedList, DatePipe, RouterLink, LiveStatus, ScoreBadge, DifficultyBadge],
  templateUrl: './child-inbox.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildInbox {
  protected readonly auth = inject(ChildAuth);
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
  protected readonly live = liveLists([this.assignments], { path: '/api/child/changes' });
  protected readonly failure = childError;
  protected readonly status = childStatus;
  protected readonly learnerState = learnerState;
}
