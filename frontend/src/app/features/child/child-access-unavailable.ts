import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { EntryLayout } from '../../shared/entry-layout/entry-layout';

/** Availability retry keeps child identity recovery out of parent sign-in. */
@Component({
  selector: 'app-child-access-unavailable',
  imports: [RouterLink, DisabledInteractive, EntryLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-entry-layout audience="child" heading="הפעילויות לא זמינות כרגע">
      <p class="error" role="alert">משהו לא עבד. נסו שוב עוד רגע.</p>
      <button
        class="button w-full"
        routerLink="/child"
        [replaceUrl]="true"
        [disabledInteractive]="!!router.currentNavigation()"
      >
        <span class="icon icon-refresh" aria-hidden="true"></span>ניסיון נוסף
      </button>
    </app-entry-layout>
  `,
})
export class ChildAccessUnavailable {
  protected readonly router = inject(Router);
}
