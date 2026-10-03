import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Keep mounted outside aria-busy containers so the live region exists before a request starts.
 * Feedback appears only after a short delay. Theme with --loader-color, --loader-size and
 * --loader-duration; reduced motion stops the spin and sweep. The out-of-flow bar is for navigation.
 */
@Component({
  selector: 'app-loading-indicator',
  styleUrl: './loading-indicator.css',
  templateUrl: './loading-indicator.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    role: 'status',
    'aria-live': 'polite',
    'aria-atomic': 'true',
    '[class.loading-panel]': "active() && variant() === 'panel'",
    '[class.loading-bar]': "variant() === 'bar'",
    '[class.loading-idle]': '!active()',
  },
})
export class LoadingIndicator {
  readonly active = input(false);
  readonly label = input('טוענים…');
  readonly detail = input('');
  readonly variant = input<'inline' | 'panel' | 'bar'>('inline');
}
