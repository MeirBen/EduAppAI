import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const heroes = {
  family: {
    title: 'לומדים משהו חדש, ביחד.',
    lead: 'מתארים מה לתרגל, מכינים פעילות ובודקים אותה לפני שהילדים מתחילים.',
    points: [
      { icon: 'icon-ai', text: 'מתארים במילים שלכם, והפעילות נבנית' },
      { icon: 'icon-check', text: 'בודקים ומאשרים לפני שהילדים מתחילים' },
      { icon: 'icon-users', text: 'הילדים עונים מכל מכשיר, ואתם רואים את התוצאות' },
    ],
  },
  child: {
    title: 'שלום! מוכנים לפעילות?',
    lead: 'הקוד מההורה פותח את הפעילויות שלכם.',
    points: [
      { icon: 'icon-book', text: 'קוראים' },
      { icon: 'icon-edit', text: 'עונים על השאלות' },
      { icon: 'icon-up', text: 'מגישים להורה' },
    ],
  },
};

/**
 * The frame of every signed-out page: the app's promise beside one card that holds the page's
 * heading and its single task, which the page projects. The card's heading is the page's `h1`.
 */
@Component({
  selector: 'app-entry-layout',
  template: `
    <div
      class="grid content-center items-center gap-8 lg:grid-cols-2 lg:gap-20 pinned-header:min-h-pinned-fill"
    >
      <div class="grid max-w-card gap-stack">
        <p class="text-3xl leading-snug font-semibold text-balance sm:text-4xl">
          {{ hero().title }}
        </p>
        <p class="text-lg text-muted">{{ hero().lead }}</p>
        <ul role="list" class="grid gap-3 max-lg:hidden">
          @for (point of hero().points; track point.icon) {
            <li class="flex items-center gap-3">
              <span class="icon-tile size-9" aria-hidden="true"
                ><span class="icon" [class]="point.icon"></span
              ></span>
              {{ point.text }}
            </li>
          }
        </ul>
      </div>
      <section
        class="panel grid w-full max-w-card gap-section lg:justify-self-end"
        aria-labelledby="entry-title"
      >
        <header class="grid gap-1">
          <h1 id="entry-title" class="text-2xl">{{ heading() }}</h1>
          @if (lead()) {
            <p class="text-muted">{{ lead() }}</p>
          }
        </header>
        <ng-content />
      </section>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntryLayout {
  readonly audience = input<keyof typeof heroes>('family');
  readonly heading = input.required<string>();
  readonly lead = input('');
  protected readonly hero = computed(() => heroes[this.audience()]);
}
