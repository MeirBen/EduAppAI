import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from './core/auth/auth';
import { parentSignOut } from './core/auth/login-guard';
import { apiError } from './core/api/api-error';
import { dismissibleTooltips } from './shared/dismissible-tooltips';
import { LoadingIndicator } from './shared/loading-indicator/loading-indicator';
import { ThemePicker } from './shared/theme-picker/theme-picker';

/** Application shell with session-aware navigation and explicit sign-out feedback. */
@Component({
  selector: 'app-root',
  imports: [LoadingIndicator, RouterOutlet, RouterLink, RouterLinkActive, ThemePicker],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The footer rests at the window's bottom while a page is shorter, so loading never moves it up.
  host: { class: 'flex min-h-dvh flex-col' },
})
export class App {
  protected readonly auth = inject(Auth);
  protected readonly router = inject(Router);
  protected readonly error = signal('');
  protected readonly signingOut = signal(false);

  constructor() {
    dismissibleTooltips();
  }

  protected async logout() {
    if (this.signingOut()) return;
    this.signingOut.set(true);
    this.error.set('');
    try {
      await this.router.navigateByUrl('/login', {
        info: parentSignOut,
        onSameUrlNavigation: 'reload',
      });
    } catch (error) {
      this.error.set(apiError(error));
    } finally {
      this.signingOut.set(false);
    }
  }
}
