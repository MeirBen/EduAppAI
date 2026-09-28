import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { IonApp, IonContent, IonHeader, IonToolbar } from '@ionic/angular';
import { Auth } from './core/auth/auth';
import { apiError } from './core/api/api-error';

/** Application shell with session-aware navigation and explicit sign-out feedback. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, IonApp, IonContent, IonHeader, IonToolbar],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly auth = inject(Auth);
  private readonly router = inject(Router);
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
