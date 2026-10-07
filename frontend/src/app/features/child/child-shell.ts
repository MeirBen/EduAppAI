import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ChildAuth } from '../../core/auth/child-auth';
import { childSignOut } from '../../core/auth/child-guard';
import { PageShell } from '../../shared/page-shell/page-shell';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { childError } from './child-error';

/** Child navigation has no parent links or same-browser mode switch. */
@Component({
  selector: 'app-child-shell',
  imports: [PageShell, RouterLink, RouterLinkActive, RouterOutlet, DisabledInteractive],
  templateUrl: './child-shell.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildShell {
  protected readonly auth = inject(ChildAuth);
  private readonly router = inject(Router);
  protected readonly signingOut = signal(false);
  protected readonly error = signal('');
  protected async logout() {
    if (
      this.signingOut() ||
      !window.confirm('לנתק את המכשיר? כדי לחזור לפעילויות תצטרכו קוד חדש מההורה.')
    )
      return;
    this.signingOut.set(true);
    this.error.set('');
    try {
      await this.router.navigateByUrl('/child/activate', {
        info: childSignOut,
        onSameUrlNavigation: 'reload',
        replaceUrl: true,
      });
    } catch (error) {
      this.error.set(childError(error));
    } finally {
      this.signingOut.set(false);
    }
  }
}
