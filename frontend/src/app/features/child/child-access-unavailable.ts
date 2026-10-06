import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DisabledInteractive } from '../../shared/disabled-interactive';

/** Availability retry keeps child identity recovery out of parent sign-in. */
@Component({
  selector: 'app-child-access-unavailable',
  imports: [RouterLink, DisabledInteractive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: ` <section
    class="panel mx-auto my-gutter grid max-w-lg gap-4"
    aria-labelledby="child-unavailable-title"
  >
    <h1 id="child-unavailable-title" class="text-2xl">הפעילויות לא זמינות כרגע</h1>
    <p class="error" role="alert">לא הצלחנו לבדוק את הגישה למכשיר. אפשר לנסות שוב בעוד רגע.</p>
    <button
      class="button"
      routerLink="/child"
      [replaceUrl]="true"
      [disabledInteractive]="!!router.currentNavigation()"
    >
      ניסיון נוסף
    </button>
  </section>`,
})
export class ChildAccessUnavailable {
  protected readonly router = inject(Router);
}
