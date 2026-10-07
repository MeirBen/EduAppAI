import { ChangeDetectionStrategy, Component, Directive, inject, signal } from '@angular/core';

/** A page's pinned `action-bar` that can shrink to its status line, so it stops covering content. */
@Directive({
  selector: 'section[actionBar]',
  host: { '[attr.data-minimized]': "minimized() ? '' : null" },
})
export class ActionBar {
  readonly minimized = signal(false);
}

/** Shrinks or restores the enclosing action bar; it shows only while the bar is pinned. */
@Component({
  selector: 'app-action-bar-toggle',
  template: `
    <button
      type="button"
      class="icon-button icon-button-quiet"
      [attr.aria-expanded]="!bar.minimized()"
      (click)="bar.minimized.update((minimized) => !minimized)"
    >
      <span
        class="icon icon-chevron motion-safe:transition-[rotate]"
        [class.rotate-180]="bar.minimized()"
        aria-hidden="true"
      ></span>
      <span class="tooltip">{{ bar.minimized() ? 'הצגת כל הפעולות' : 'הקטנת הסרגל' }}</span>
    </button>
  `,
  host: { class: 'hidden pinned-actions:contents' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActionBarToggle {
  protected readonly bar = inject(ActionBar);
}
