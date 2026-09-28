import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { Auth } from './core/auth/auth';
import { apiError } from './core/api/api-error';
import { LoadingIndicator } from './shared/loading-indicator/loading-indicator';

/** Application shell with session-aware navigation and explicit sign-out feedback. */
@Component({
  selector: 'app-root',
  imports: [LoadingIndicator, RouterOutlet, RouterLink],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly auth = inject(Auth);
  protected readonly router = inject(Router);
  protected readonly error = signal('');
  protected readonly signingOut = signal(false);

  protected async logout() {
    this.signingOut.set(true);
    this.error.set('');
    try {
      await this.auth.logout();
      await this.router.navigateByUrl('/login');
    } catch (error) {
      this.error.set(apiError(error));
    } finally {
      this.signingOut.set(false);
    }
  }
}
