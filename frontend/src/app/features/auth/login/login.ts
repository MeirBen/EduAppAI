import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  linkedSignal,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { disabled, email, form, FormField, required, submit } from '@angular/forms/signals';
import { Auth } from '../../../core/auth/auth';
import { parentAccessUnavailable } from '../../../core/auth/parent-guard';
import { apiError } from '../../../core/api/api-error';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';

/** Parent sign-in form with local feedback and navigation after cookie authentication. */
@Component({
  selector: 'app-login',
  imports: [LoadingIndicator, FormField],
  templateUrl: './login.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Login {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly lifetime = inject(DestroyRef);
  protected readonly model = signal({ email: '', password: '' });
  protected readonly fields = form(this.model, (path) => {
    disabled(path, ({ state }) => state.submitting());
    required(path.email);
    email(path.email);
    required(path.password);
  });
  protected readonly error = linkedSignal({
    source: this.router.lastSuccessfulNavigation,
    computation: (navigation): string =>
      navigation?.extras.info === parentAccessUnavailable
        ? 'לא הצלחנו לפתוח את המרחב. אפשר לנסות להיכנס שוב.'
        : '',
  });

  protected async signIn(event: Event) {
    event.preventDefault();
    if (this.fields().submitting()) return;
    this.error.set('');
    await submit(this.fields, async () => {
      try {
        await this.auth.login(this.model().email, this.model().password, this.lifetime);
        if (this.lifetime.destroyed) return;
        this.model.update((value) => ({ ...value, password: '' }));
        await this.router.navigateByUrl('/');
      } catch (error) {
        if (!this.lifetime.destroyed) this.error.set(apiError(error));
      }
    });
  }
}
