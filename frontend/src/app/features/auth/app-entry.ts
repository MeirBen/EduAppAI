import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EntryLayout } from '../../shared/entry-layout/entry-layout';
import { PageShell } from '../../shared/page-shell/page-shell';

/** Anonymous devices can reach either sign-in flow without leaving the installed app. */
@Component({
  imports: [EntryLayout, PageShell, RouterLink],
  selector: 'app-entry',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-shell homeUrl="/">
      <app-entry-layout heading="ברוכים הבאים" lead="בחרו איך להיכנס.">
        <div class="grid gap-3">
          <article class="list-row">
            <span class="icon-tile size-10" aria-hidden="true"
              ><span class="icon icon-book"></span
            ></span>
            <div class="grid min-w-1/2 flex-1 gap-1">
              <h2><a class="row-link" routerLink="/child/activate">כניסה לפעילויות</a></h2>
              <p class="text-sm text-muted">לילדים, עם הקוד מההורה.</p>
            </div>
          </article>
          <article class="list-row">
            <span class="icon-tile size-10" aria-hidden="true"
              ><span class="icon icon-users"></span
            ></span>
            <div class="grid min-w-1/2 flex-1 gap-1">
              <h2><a class="row-link" routerLink="/login">כניסת הורים</a></h2>
              <p class="text-sm text-muted">עם כתובת דוא״ל וסיסמה.</p>
            </div>
          </article>
        </div>
      </app-entry-layout>
    </app-page-shell>
  `,
})
export class AppEntry {}
