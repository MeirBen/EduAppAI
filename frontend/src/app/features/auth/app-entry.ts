import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PageShell } from '../../shared/page-shell/page-shell';

/** Anonymous devices can reach either sign-in flow without leaving the installed app. */
@Component({
  imports: [PageShell, RouterLink],
  selector: 'app-entry',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-shell homeUrl="/">
      <section class="panel mx-auto grid max-w-card gap-4" aria-labelledby="entry-title">
        <h1 id="entry-title">ברוכים הבאים ללומדים ביחד</h1>
        <p class="text-muted">בחרו איך להיכנס. לפעילויות הילדים צריך קוד מההורה.</p>
        <a class="button" routerLink="/child/activate">כניסה לפעילויות</a>
        <a class="button button-secondary" routerLink="/login">כניסת הורים</a>
      </section>
    </app-page-shell>
  `,
})
export class AppEntry {}
