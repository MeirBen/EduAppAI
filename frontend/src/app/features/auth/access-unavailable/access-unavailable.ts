import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DisabledInteractive } from '../../../shared/disabled-interactive';

/** Recoverable access failure; retry runs the normal parent guard without asking for credentials. */
@Component({
  imports: [RouterLink, DisabledInteractive],
  selector: 'app-access-unavailable',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel mx-auto grid max-w-lg gap-4" aria-labelledby="access-title">
      <h1 id="access-title" class="text-2xl">המרחב לא זמין כרגע</h1>
      <p class="error" role="alert">לא הצלחנו לפתוח את המרחב. אפשר לנסות שוב בעוד רגע.</p>
      <button
        type="button"
        class="button"
        routerLink="/"
        [replaceUrl]="true"
        [disabledInteractive]="router.currentNavigation() !== null"
      >
        ניסיון נוסף
      </button>
    </section>
  `,
})
export class AccessUnavailable {
  protected readonly router = inject(Router);
}
