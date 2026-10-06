import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChildApi } from '../../core/api/child-api';
import { ChildAuth } from '../../core/auth/child-auth';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { childError, childStatus } from './child-error';

/** Bounded child inbox; listing work never starts a session. */
@Component({
  selector: 'app-child-inbox',
  imports: [DatePipe, RouterLink, DisabledInteractive, LoadingIndicator],
  templateUrl: './child-inbox.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildInbox {
  protected readonly auth = inject(ChildAuth);
  protected readonly filter = signal<{ state: 'available' | 'submitted'; page: number }>({
    state: 'available',
    page: 1,
  });
  protected readonly assignments = inject(ChildApi).inbox(this.filter);
  protected readonly failure = childError;
  protected readonly status = childStatus;
  protected changeState(event: Event) {
    const state = (event.target as HTMLSelectElement).value;
    if (state === 'available' || state === 'submitted') this.filter.set({ state, page: 1 });
  }
  protected movePage(delta: number) {
    this.filter.update((value) => ({ ...value, page: value.page + delta }));
  }
}
