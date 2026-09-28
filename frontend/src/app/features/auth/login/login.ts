import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { email, form, FormField, required, submit } from '@angular/forms/signals';
import { Auth } from '../../../core/auth/auth';
import { apiError } from '../../../core/api/api-error';

@Component({
  selector: 'app-login',
  imports: [FormField],
  templateUrl: './login.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Login {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  protected readonly model = signal({ email: '', password: '' });
  protected readonly fields = form(this.model, (path) => {
    required(path.email);
    email(path.email);
    required(path.password);
  });
  protected readonly error = signal(
    inject(ActivatedRoute).snapshot.queryParamMap.has('connection')
      ? 'Cannot reach the server. Check that it is running, then try again.'
      : '',
  );

  protected async signIn(event: Event) {
    event.preventDefault();
    this.error.set('');
    await submit(this.fields, async () => {
      try {
        await this.auth.login(this.model().email, this.model().password);
        this.model.update((value) => ({ ...value, password: '' }));
        await this.router.navigateByUrl('/templates');
      } catch (error) {
        this.error.set(apiError(error));
      }
    });
  }
}
