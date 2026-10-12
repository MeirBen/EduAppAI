import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/** A read that failed: what went wrong, announced, and one way to try again. */
@Component({
  selector: 'app-read-failure',
  template: `
    <p class="error" role="alert">{{ message() }}</p>
    <button type="button" class="text-link" (click)="retry.emit()">
      <span class="icon icon-refresh" aria-hidden="true"></span>ניסיון נוסף
    </button>
  `,
  host: { class: 'grid gap-2' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReadFailure {
  readonly message = input.required<string>();
  readonly retry = output<void>();
}
