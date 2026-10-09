import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Keep mounted outside aria-busy containers so the live region exists before a request starts.
 * Shared feedback primitives own its appearance and motion; the bar is for navigation and
 * `assistant` is the chat's thinking turn.
 */
@Component({
  selector: 'app-loading-indicator',
  templateUrl: './loading-indicator.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'loading',
    role: 'status',
    'aria-live': 'polite',
    'aria-atomic': 'true',
    '[class.loading-panel]': "active() && variant() === 'panel'",
    '[class.loading-bar]': "variant() === 'bar'",
    '[class.loading-assistant]': "variant() === 'assistant'",
    '[class.loading-active]': 'active()',
  },
})
export class LoadingIndicator {
  readonly active = input(false);
  readonly label = input('טוענים…');
  readonly detail = input('');
  readonly variant = input<'inline' | 'panel' | 'bar' | 'assistant'>('inline');
}
