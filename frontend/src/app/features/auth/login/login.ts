import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { disabled, email, form, FormField, required, submit } from '@angular/forms/signals';
import { Auth } from '../../../core/auth/auth';
import { apiError } from '../../../core/api/api-error';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { FieldValidity } from '../../../shared/forms/field-validity';
import { FieldErrors } from '../../../shared/forms/field-errors';
import { DisabledInteractive } from '../../../shared/disabled-interactive';

/** Parent sign-in form with local feedback and navigation after cookie authentication. */
@Component({
  selector: 'app-login',
  imports: [FieldErrors, FieldValidity, LoadingIndicator, FormField, DisabledInteractive],
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
    const invalidEmail = { message: 'יש להזין כתובת דוא״ל תקינה.' };
    required(path.email, invalidEmail);
    email(path.email, invalidEmail);
    required(path.password, { message: 'יש להזין סיסמה.' });
  });
  protected readonly error = signal('');

  protected async signIn(event: Event) {
    event.preventDefault();
    if (this.fields().submitting()) return;
    this.error.set('');
    await submit(this.fields, async () => {
      try {
        await this.auth.login(this.model().email, this.model().password, this.lifetime);
        if (this.lifetime.destroyed) return;
        this.model.update((value) => ({ ...value, password: '' }));
        await this.router.navigateByUrl('/', { replaceUrl: true });
      } catch (error) {
        if (!this.lifetime.destroyed) this.error.set(apiError(error));
      }
    });
  }
}
