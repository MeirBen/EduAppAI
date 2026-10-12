import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { EntryLayout } from '../../../shared/entry-layout/entry-layout';

/** Recoverable access failure; retry repeats app entry routing without asking for credentials. */
@Component({
  imports: [RouterLink, DisabledInteractive, EntryLayout],
  selector: 'app-access-unavailable',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-entry-layout heading="המרחב לא זמין כרגע">
      <p class="error" role="alert">לא הצלחנו לפתוח את המרחב. נסו שוב בעוד רגע.</p>
      <button
        type="button"
        class="button w-full"
        routerLink="/"
        [replaceUrl]="true"
        [disabledInteractive]="router.currentNavigation() !== null"
      >
        <span class="icon icon-refresh" aria-hidden="true"></span>ניסיון נוסף
      </button>
    </app-entry-layout>
  `,
})
export class AccessUnavailable {
  protected readonly router = inject(Router);
}
