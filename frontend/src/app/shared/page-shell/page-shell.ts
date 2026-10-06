import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ThemePicker } from '../theme-picker/theme-picker';
import { LoadingIndicator } from '../loading-indicator/loading-indicator';

/** Shared responsive frame; each authentication area supplies its own navigation and content. */
@Component({
  selector: 'app-page-shell',
  imports: [RouterLink, ThemePicker, LoadingIndicator],
  templateUrl: './page-shell.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-h-dvh flex-col' },
})
export class PageShell {
  readonly homeUrl = input.required<string>();
  readonly compactBrand = input(false);
  readonly busy = input(false);
  protected readonly router = inject(Router);
}
