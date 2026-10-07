import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../../core/auth/auth';
import { parentSignOut } from '../../core/auth/login-guard';
import { apiError } from '../../core/api/api-error';
import { PageShell } from '../../shared/page-shell/page-shell';
import { DisabledInteractive } from '../../shared/disabled-interactive';

/** Parent shell with session-aware navigation and explicit sign-out feedback. */
@Component({
  selector: 'app-parent-shell',
  imports: [PageShell, RouterOutlet, RouterLink, RouterLinkActive, DisabledInteractive],
  templateUrl: './parent-shell.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParentShell {
  protected readonly auth = inject(Auth);
  protected readonly router = inject(Router);
  protected readonly error = signal('');
  protected readonly signingOut = signal(false);

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
