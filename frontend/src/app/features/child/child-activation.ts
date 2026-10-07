import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { disabled, form, FormField, submit, validate } from '@angular/forms/signals';
import { Router } from '@angular/router';
import { ChildAuth } from '../../core/auth/child-auth';
import { requestResult } from '../../core/api/request-result';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { FieldErrors } from '../../shared/forms/field-errors';
import { FieldValidity } from '../../shared/forms/field-validity';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { childStatus } from './child-error';

/** One-time activation. Recovery checks the cookie; it never resends a possibly consumed code. */
@Component({
  selector: 'app-child-activation',
  imports: [FormField, FieldErrors, FieldValidity, DisabledInteractive, LoadingIndicator],
  templateUrl: './child-activation.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildActivation {
  private readonly auth = inject(ChildAuth);
  private readonly router = inject(Router);
  private readonly lifetime = inject(DestroyRef);
  protected readonly model = signal({ code: '' });
  protected readonly busy = signal(false);
  protected readonly recovery = signal(false);
  protected readonly error = signal('');
  protected readonly fields = form(this.model, (path) => {
    disabled(path, () => this.busy() || this.recovery());
    // The server owns the code's alphabet; this only catches a code that is not yet complete.
    validate(path.code, ({ value }) =>
      /^[a-z]{8}$/i.test(value().replace(/[\s-]/g, ''))
        ? undefined
        : { kind: 'code', message: 'קוד ההפעלה בנוי מ־8 אותיות באנגלית, כמו שמופיע אצל ההורה.' },
    );
  });
  constructor() {
    this.lifetime.onDestroy(() => this.model.set({ code: '' }));
  }

  protected async activate(event: Event) {
    event.preventDefault();
    if (this.busy() || this.recovery()) return;
    await submit(this.fields, async () => {
      const code = this.model().code.trim();
      this.fields().reset({ code: '' });
      this.busy.set(true);
      this.error.set('');
      try {
        const identity = await this.auth.activate(code, this.lifetime);
        if (this.lifetime.destroyed) return;
        if (identity) await this.router.navigateByUrl('/child', { replaceUrl: true });
        else this.error.set('לא נמצאה גישה פעילה. בקשו מההורה קוד חדש.');
      } catch (error) {
        if (this.lifetime.destroyed) return;
        const status = childStatus(error);
        this.recovery.set(status !== 400 && status !== 429);
        this.error.set(
          status === 400
            ? 'הקוד לא תקף או שכבר השתמשו בו. בקשו מההורה קוד חדש.'
            : status === 409
              ? 'כבר פתוח חשבון בדפדפן הזה. הפעילו את מכשיר הילד בדפדפן נפרד, או סגרו קודם את החשבון הפעיל.'
              : status === 429
                ? 'נעשו ניסיונות רבים בזמן קצר. נסו שוב בעוד רגע עם קוד חדש.'
                : 'לא הצלחנו לאשר שההפעלה הסתיימה. בדקו את הגישה למכשיר לפני בקשת קוד חדש.',
        );
      } finally {
        if (!this.lifetime.destroyed) this.busy.set(false);
      }
    });
  }
  protected async checkAccess() {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const identity = await requestResult(this.auth.loadSession(), this.lifetime);
      if (this.lifetime.destroyed) return;
      if (identity) await this.router.navigateByUrl('/child', { replaceUrl: true });
      else {
        this.recovery.set(false);
        this.error.set('לא נמצאה גישה פעילה. בקשו מההורה קוד חדש.');
      }
    } catch {
      if (!this.lifetime.destroyed)
        this.error.set('לא הצלחנו לבדוק את הגישה כרגע. נסו שוב בעוד רגע.');
    } finally {
      if (!this.lifetime.destroyed) this.busy.set(false);
    }
  }
}
